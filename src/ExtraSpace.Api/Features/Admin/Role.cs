using ExtraSpace.Api.Platform.Data;

namespace ExtraSpace.Api.Features.Admin;

internal sealed class Role : IHasTimestamps, IHasStaffStamps, IAudited
{
    /// <summary>The system role Owner: every permission, cannot be changed or deleted (seeded by the AdminTables migration).</summary>
    public static readonly Guid OwnerId = Guid.Parse("0199dd00-0000-7000-8000-000000000001");

    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public string[] Permissions { get; set; } = [];
    public bool IsSystem { get; set; }
    public Guid? CreatedByStaffId { get; set; }
    public Guid? UpdatedByStaffId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public uint Version { get; set; }
}
