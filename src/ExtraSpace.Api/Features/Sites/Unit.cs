using ExtraSpace.Api.Platform.Data;

namespace ExtraSpace.Api.Features.Sites;

/// <summary>Our copy of a SiteLink unit (S1 spec §5.1). Written only by the unit sync; read-only in the admin.</summary>
internal sealed class Unit : IHasTimestamps
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid SiteId { get; init; }
    public Site Site { get; private set; } = null!;
    public int SiteLinkUnitId { get; set; }
    public int SiteLinkUnitTypeId { get; set; }
    public required string UnitName { get; set; }
    public string? Description { get; set; }
    public decimal Width { get; set; }
    public decimal Length { get; set; }
    public decimal AreaSqft { get; set; }
    public int? Floor { get; set; }
    public bool Climate { get; set; }
    public bool Inside { get; set; }
    public bool Power { get; set; }
    public bool Alarm { get; set; }
    public bool Ada { get; set; }
    public bool Mobile { get; set; }
    public decimal? StandardRate { get; set; }
    public decimal? WebRate { get; set; }
    public decimal? BoardRate { get; set; }
    public decimal? PushRate { get; set; }
    public decimal? PreferredRate { get; set; }
    public decimal? StandardSecurityDeposit { get; set; }
    public bool Rented { get; set; }
    public bool Rentable { get; set; } = true;
    public bool WaitingListReserved { get; set; }
    public DateTime? DeletedAt { get; set; }
    /// <summary>Stored generated column: rentable AND NOT rented AND NOT waiting_list_reserved AND deleted_at IS NULL.</summary>
    public bool Available { get; private set; }
    public Guid? StorageTypeId { get; set; }
    public StorageType? StorageType { get; private set; }
    public Guid? UnitSizeId { get; set; }
    public UnitSize? UnitSize { get; private set; }
    public DateTime? LastSyncedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
