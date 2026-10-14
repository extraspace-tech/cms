using System.Text.Json;

namespace ExtraSpace.Api.Platform.Outbox;

/// <summary>A side effect saved in the same transaction as the change that causes it (S1 spec §5.6, §9.10).</summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string Kind { get; init; }
    public required JsonDocument Payload { get; set; }
    public required string IdempotencyKey { get; init; }
    public string? CorrelationId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime AvailableAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public DateTime? FailedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public Guid? ResolvedByStaffId { get; set; }
    public string? ResolutionNote { get; set; }
}
