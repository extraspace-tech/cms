using ExtraSpace.Api.Platform.Data;

namespace ExtraSpace.Api.Features.Identity;

/// <summary>
/// One row per email (plan decision 2): the active code plus the per-email send limits. A new request overwrites the code.
/// </summary>
internal sealed class CustomerLoginCode : IHasTimestamps
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string Email { get; init; }
    public string CodeHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public string? RequestIp { get; set; }
    public DateTime LastSentAt { get; set; }
    public DateTime WindowStartedAt { get; set; }
    public int SentInWindow { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
