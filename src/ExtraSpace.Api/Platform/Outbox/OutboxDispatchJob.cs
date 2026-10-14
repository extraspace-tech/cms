using System.Text.Json;
using ExtraSpace.Api.Platform.Data;
using ExtraSpace.Api.Platform.Jobs;
using ExtraSpace.Api.Platform.Tracing;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace ExtraSpace.Api.Platform.Outbox;

/// <summary>Runs due outbox steps one at a time (S1 spec §9.10). Several runs at once are safe: each row is claimed with SKIP LOCKED.</summary>
internal sealed partial class OutboxDispatchJob(IServiceScopeFactory scopes, TimeProvider time, ILogger<OutboxDispatchJob> logger) : IRecurringJob
{
    public const string Name = "OutboxDispatcher";
    public const string Cron = "* * * * *";
    internal const int MaxPerRun = 100;
    internal static readonly TimeSpan[] RetryDelays =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(60)];

    [AutomaticRetry(Attempts = 0, LogEvents = false)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < MaxPerRun; i++)
            if (!await DispatchNextAsync(cancellationToken)) return;
    }

    private async Task<bool> DispatchNextAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        var message = await db.Set<OutboxMessage>()
            .FromSql($"""
                SELECT * FROM outbox_message
                WHERE processed_at IS NULL AND failed_at IS NULL AND available_at <= {now}
                ORDER BY available_at, id LIMIT 1 FOR UPDATE SKIP LOCKED
                """)
            .SingleOrDefaultAsync(cancellationToken);
        // Nothing was written: dispose rolls the empty transaction back.
        if (message is null) return false;

        using (Correlation.Begin(message.CorrelationId ?? message.Id.ToString("N")))
        {
            try
            {
                await HandleAsync(message, cancellationToken);
                message.ProcessedAt = time.GetUtcNow().UtcDateTime;
                // A done step no longer needs its data (a sealed email may hold an address); the column is not null.
                message.Payload = JsonDocument.Parse("{}");
                LogDone(logger, message.Kind, message.Id);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                Fail(message, exception, time.GetUtcNow().UtcDateTime);
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    // Its own scope: the handler's DbContext and transaction are its own, while the row lock above is held until it ends.
    private async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IOutboxHandler>().SingleOrDefault(h => h.Kind == message.Kind)
            ?? throw new InvalidOperationException($"No outbox handler for kind '{message.Kind}'.");
        await handler.HandleAsync(new OutboxItem(message.Id, message.IdempotencyKey, message.Payload.RootElement, message.Attempts + 1), cancellationToken);
    }

    private void Fail(OutboxMessage message, Exception exception, DateTime now)
    {
        message.Attempts++;
        var error = $"{exception.GetType().Name}: {exception.Message}";
        message.LastError = error.Length <= 1000 ? error : error[..1000];
        if (message.Attempts > RetryDelays.Length)
        {
            message.FailedAt = now;
            LogFailedPermanently(logger, exception, message.Id, message.Kind, message.Attempts);
            return;
        }
        message.AvailableAt = now + RetryDelays[message.Attempts - 1];
        LogRetry(logger, exception, message.Kind, message.Id, message.Attempts, message.AvailableAt);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox {kind} message {outboxId} done")]
    private static partial void LogDone(ILogger logger, string kind, Guid outboxId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox {kind} message {outboxId} failed (attempt {attempt}); next try at {availableAt}")]
    private static partial void LogRetry(ILogger logger, Exception exception, string kind, Guid outboxId, int attempt, DateTime availableAt);

    // Task 8's alarm matches this template text exactly.
    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox message {outboxId} of kind {kind} failed permanently after {attempt} attempts")]
    private static partial void LogFailedPermanently(ILogger logger, Exception exception, Guid outboxId, string kind, int attempt);
}
