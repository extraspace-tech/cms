using ExtraSpace.Api.Platform.Data;

namespace ExtraSpace.Api.Features.Sites;

internal sealed class StorageType : IHasTimestamps, IHasStaffStamps, IAudited
{
    public static readonly Guid SelfStorageId = Guid.Parse("0199e000-0000-7000-8000-000000000001");
    public static readonly Guid WorkspaceId = Guid.Parse("0199e000-0000-7000-8000-000000000002");
    public const string SelfStorageKey = "self-storage";
    public const string WorkspaceKey = "workspace";

    public Guid Id { get; init; } = Guid.CreateVersion7();
    /// <summary>Cannot change after creation: the unit sync finds the two seeded types by key.</summary>
    public required string Key { get; init; }
    public required string Name { get; set; }
    public string? DisplayName { get; set; }
    public string? IconPath { get; set; }
    public string? ImagePath { get; set; }
    public int SortOrder { get; set; }
    public bool Active { get; set; } = true;
    public Guid? CreatedByStaffId { get; set; }
    public Guid? UpdatedByStaffId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public uint Version { get; set; }

    /// <summary>SiteLink type name "BizPlus" (any case, trimmed) is a workspace unit; everything else self-storage (demo 03 §4.1.3).</summary>
    public static string KeyFor(string siteLinkTypeName) =>
        siteLinkTypeName.Trim().Equals("bizplus", StringComparison.OrdinalIgnoreCase) ? WorkspaceKey : SelfStorageKey;
}
