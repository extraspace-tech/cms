using System.Diagnostics;
using System.Text.Json;
using ExtraSpace.Api.Platform.Data;
using ExtraSpace.Api.Platform.Tracing;

namespace ExtraSpace.Api.Platform.Outbox;

internal sealed class Outbox(AppDbContext db, TimeProvider time) : IOutbox
{
    public void Enqueue<T>(string kind, T payload, string idempotencyKey)
    {
        var now = time.GetUtcNow().UtcDateTime;
        db.Add(new OutboxMessage
        {
            Kind = kind,
            Payload = JsonSerializer.SerializeToDocument(payload, JsonSerializerOptions.Web),
            IdempotencyKey = idempotencyKey,
            CorrelationId = Correlation.CurrentId ?? Activity.Current?.TraceId.ToHexString(),
            CreatedAt = now,
            AvailableAt = now,
        });
    }
}
