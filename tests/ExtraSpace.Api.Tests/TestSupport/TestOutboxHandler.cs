using System.Collections.Concurrent;
using ExtraSpace.Api.Platform.Outbox;

namespace ExtraSpace.Api.Tests.TestSupport;

/// <summary>Kind "Test": counts runs per idempotency key; fails the first <c>failTimes</c> runs when the payload says so.</summary>
public sealed class TestOutboxHandler : IOutboxHandler
{
    public const string KindName = "Test";
    public static readonly ConcurrentDictionary<string, int> Runs = new();

    public string Kind => KindName;

    public Task HandleAsync(OutboxItem item, CancellationToken cancellationToken)
    {
        var runs = Runs.AddOrUpdate(item.IdempotencyKey, 1, (_, n) => n + 1);
        if (item.Payload.TryGetProperty("failTimes", out var failTimes) && runs <= failTimes.GetInt32())
            throw new InvalidOperationException("test-outbox-failure");
        return Task.CompletedTask;
    }
}
