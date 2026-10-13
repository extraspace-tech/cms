using ExtraSpace.Api.Platform.Data;

namespace ExtraSpace.Api.Features.Identity;

/// <summary>An app customer (S0 spec §5.1). Created at the first verified sign-in; email stored trimmed and lower-case.</summary>
internal sealed class Customer : IHasTimestamps
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
