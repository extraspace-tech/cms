using ExtraSpace.Api.Platform.Data;

namespace ExtraSpace.Api.Features.Admin;

/// <summary>A staff member (S0 spec §5.1). Must exist and be active to sign in; never deleted, only deactivated.</summary>
internal sealed class StaffUser : IHasTimestamps, IHasStaffStamps, IAudited
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string Email { get; set; }
    public string? DisplayName { get; set; }
    public string? EntraObjectId { get; set; }
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public bool Active { get; set; } = true;
    public DateTime? LastSignInAt { get; set; }
    public Guid? CreatedByStaffId { get; set; }
    public Guid? UpdatedByStaffId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public uint Version { get; set; }
}
