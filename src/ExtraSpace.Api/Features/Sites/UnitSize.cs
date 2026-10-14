using ExtraSpace.Api.Platform.Data;

namespace ExtraSpace.Api.Features.Sites;

internal sealed class UnitSize : IHasTimestamps, IHasStaffStamps, IAudited
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string Key { get; set; }
    public required string Label { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? UnitGroup { get; set; }
    public string? ImagePath { get; set; }
    public decimal? MinAreaSqft { get; set; }
    public decimal? MaxAreaSqft { get; set; }
    public int SortOrder { get; set; }
    public Guid? StorageTypeId { get; set; }
    public Guid? CreatedByStaffId { get; set; }
    public Guid? UpdatedByStaffId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public uint Version { get; set; }
}
