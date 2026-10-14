using System.Data.Common;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ExtraSpace.Api.Platform.Outbox;

/// <summary>
/// Starts a dispatcher run right after a commit that saved outbox rows, so steps run within a second instead of at the minute
/// sweep. A failed nudge only delays the step until the sweep. The job client is resolved only when a nudge fires, so building
/// a DbContext never builds Hangfire's client or storage.
/// </summary>
internal sealed partial class OutboxNudge(IServiceProvider services, ILogger<OutboxNudge> logger) : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private bool _pending;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Note(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Note(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context?.Database.CurrentTransaction is null) Nudge();
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context?.Database.CurrentTransaction is null) Nudge();
        return ValueTask.FromResult(result);
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => Nudge();

    public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Nudge();
        return Task.CompletedTask;
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => _pending = false;

    public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        _pending = false;
        return Task.CompletedTask;
    }

    private void Note(DbContext? context)
    {
        if (context?.ChangeTracker.Entries<OutboxMessage>().Any(e => e.State == EntityState.Added) == true) _pending = true;
    }

    private void Nudge()
    {
        if (!_pending) return;
        _pending = false;
        try
        {
            services.GetRequiredService<IBackgroundJobClient>().Enqueue<OutboxDispatchJob>(job => job.RunAsync(CancellationToken.None));
        }
        catch (Exception exception)
        {
            LogNudgeFailed(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox nudge failed; the minute sweep will run the step")]
    private static partial void LogNudgeFailed(ILogger logger, Exception exception);
}
