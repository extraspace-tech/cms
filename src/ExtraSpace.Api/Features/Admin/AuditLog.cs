using System.Net;
using System.Text.Json;

namespace ExtraSpace.Api.Features.Admin;

/// <summary>Append-only (S0 spec §5.1): no update or delete path; written only by <see cref="AuditInterceptor"/>.</summary>
internal sealed class AuditLog
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid StaffUserId { get; init; }
    public required string Action { get; init; }
    public required string EntityType { get; init; }
    public Guid EntityId { get; init; }
    public required JsonDocument Changes { get; init; }
    public IPAddress? Ip { get; init; }
    public DateTime OccurredAt { get; init; }
}
