using System.Text.Json;

namespace ExtraSpace.Api.Platform.Outbox;

public sealed record OutboxItem(Guid Id, string IdempotencyKey, JsonElement Payload, int Attempt);

/// <summary>
/// Runs one kind of outbox step. It must be idempotent: a crash after the work but before the row is marked done runs it again.
/// Exception messages are stored in <c>last_error</c> and shown to staff, so they must never hold personal data.
/// Handlers never write <c>outbox_message</c> rows (the dispatcher holds the row lock); they may only enqueue new ones.
/// Handlers must finish within seconds and make no outside calls: the claim transaction stays open while they run.
/// </summary>
public interface IOutboxHandler
{
    string Kind { get; }

    Task HandleAsync(OutboxItem item, CancellationToken cancellationToken);
}

public interface IOutbox
{
    /// <summary>
    /// Adds a step to the caller's unit of work. The caller's SaveChanges saves it in the caller's transaction, and it runs only
    /// if that commits (S1 spec §9.10). A second step with the same key fails the save.
    /// </summary>
    void Enqueue<T>(string kind, T payload, string idempotencyKey);
}
