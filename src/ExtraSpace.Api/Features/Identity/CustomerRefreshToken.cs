using ExtraSpace.Api.Platform.Data;

namespace ExtraSpace.Api.Features.Identity;

/// <summary>A rotated refresh token (S0 spec §6.1). Only its SHA-256 hash is stored; a family is one sign-in session.</summary>
internal sealed class CustomerRefreshToken : IHasTimestamps
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid CustomerId { get; init; }
    public Guid FamilyId { get; init; }
    public required string TokenHash { get; init; }
    public DateTime ExpiresAt { get; init; }
    public Guid? ReplacedById { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? UserAgent { get; init; }
    public string? Ip { get; init; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
