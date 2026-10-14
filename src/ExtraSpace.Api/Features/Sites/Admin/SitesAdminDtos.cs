using System.ComponentModel.DataAnnotations;

namespace ExtraSpace.Api.Features.Sites.Admin;

internal sealed record SiteListItem(Guid Id, string LocationCode, string Name, FacilityType FacilityType, string? City, string Country, bool Active, uint Version);

internal sealed record SiteResponse(
    Guid Id, string LocationCode, int? SiteLinkSiteId, string Name, string? DisplayName, string? Description, string? HeroImageUrl,
    string? Address1, string? City, string? Region, string? PostalCode, string Country, string? Phone, decimal? Latitude, decimal? Longitude,
    string TimeZone, string Currency, decimal TaxPercentage, string TaxLabel, FacilityType FacilityType, bool Active, string? StripeAccountId,
    uint Version);

// Request bodies are public: the .NET 10 validation generator skips non-public types. Country codes are upper case (SiteCountries).
public sealed class CreateSiteBody
{
    [Required, MaxLength(20), RegularExpression("^[A-Za-z0-9_-]+$")] public string? LocationCode { get; init; }
    [Range(1, int.MaxValue)] public int? SiteLinkSiteId { get; init; }
    [Required, MaxLength(200)] public string? Name { get; init; }
    [MaxLength(200)] public string? DisplayName { get; init; }
    [MaxLength(4000)] public string? Description { get; init; }
    [MaxLength(1000)] public string? HeroImageUrl { get; init; }
    [MaxLength(300)] public string? Address1 { get; init; }
    [MaxLength(100)] public string? City { get; init; }
    [MaxLength(100)] public string? Region { get; init; }
    [MaxLength(20)] public string? PostalCode { get; init; }
    [Required, AllowedValues("SG", "MY", "KR")] public string? Country { get; init; }
    [MaxLength(50)] public string? Phone { get; init; }
    [Range(typeof(decimal), "-90", "90")] public decimal? Latitude { get; init; }
    [Range(typeof(decimal), "-180", "180")] public decimal? Longitude { get; init; }
    /// <summary>Default: the country's zone.</summary>
    [MaxLength(64)] public string? TimeZone { get; init; }
    [Required, Range(typeof(decimal), "0", "100")] public decimal? TaxPercentage { get; init; }
    /// <summary>Default: the country's (GST / SST / VAT).</summary>
    [MaxLength(20)] public string? TaxLabel { get; init; }
    public FacilityType? FacilityType { get; init; }
    public bool? Active { get; init; }
    [MaxLength(100), RegularExpression("^acct_[A-Za-z0-9]+$")] public string? StripeAccountId { get; init; }
}

/// <summary>Null = unchanged; "" clears a text; <see cref="Clear"/> names the numbers to empty (siteLinkSiteId, latitude, longitude).</summary>
public sealed class UpdateSiteBody
{
    [MaxLength(20), RegularExpression("^[A-Za-z0-9_-]+$")] public string? LocationCode { get; init; }
    [Range(1, int.MaxValue)] public int? SiteLinkSiteId { get; init; }
    [MaxLength(200)] public string? Name { get; init; }
    [MaxLength(200)] public string? DisplayName { get; init; }
    [MaxLength(4000)] public string? Description { get; init; }
    [MaxLength(1000)] public string? HeroImageUrl { get; init; }
    [MaxLength(300)] public string? Address1 { get; init; }
    [MaxLength(100)] public string? City { get; init; }
    [MaxLength(100)] public string? Region { get; init; }
    [MaxLength(20)] public string? PostalCode { get; init; }
    [AllowedValues("SG", "MY", "KR", null)] public string? Country { get; init; }
    [MaxLength(50)] public string? Phone { get; init; }
    [Range(typeof(decimal), "-90", "90")] public decimal? Latitude { get; init; }
    [Range(typeof(decimal), "-180", "180")] public decimal? Longitude { get; init; }
    [MaxLength(64)] public string? TimeZone { get; init; }
    [Range(typeof(decimal), "0", "100")] public decimal? TaxPercentage { get; init; }
    [MaxLength(20)] public string? TaxLabel { get; init; }
    public FacilityType? FacilityType { get; init; }
    public bool? Active { get; init; }
    [MaxLength(100), RegularExpression("^(acct_[A-Za-z0-9]+)?$")] public string? StripeAccountId { get; init; }
    public string[]? Clear { get; init; }
    [Required] public uint? Version { get; init; }
}

internal sealed record StorageTypeResponse(Guid Id, string Key, string Name, string? DisplayName, string? IconPath, string? ImagePath,
    int SortOrder, bool Active, uint Version);

public sealed class CreateStorageTypeBody
{
    [Required, MaxLength(50), RegularExpression("^[a-z0-9-]+$")] public string? Key { get; init; }
    [Required, MaxLength(100)] public string? Name { get; init; }
    [MaxLength(100)] public string? DisplayName { get; init; }
    [MaxLength(500)] public string? IconPath { get; init; }
    [MaxLength(500)] public string? ImagePath { get; init; }
    public int? SortOrder { get; init; }
    public bool? Active { get; init; }
}

/// <summary>No key: it cannot change after creation (S1-1 decision 13).</summary>
public sealed class UpdateStorageTypeBody
{
    [MaxLength(100)] public string? Name { get; init; }
    [MaxLength(100)] public string? DisplayName { get; init; }
    [MaxLength(500)] public string? IconPath { get; init; }
    [MaxLength(500)] public string? ImagePath { get; init; }
    public int? SortOrder { get; init; }
    public bool? Active { get; init; }
    [Required] public uint? Version { get; init; }
}

internal sealed record UnitSizeResponse(Guid Id, string Key, string Label, string? DisplayName, string? Description, string? UnitGroup,
    string? ImagePath, decimal? MinAreaSqft, decimal? MaxAreaSqft, int SortOrder, Guid? StorageTypeId, string? StorageTypeName, uint Version);

public sealed class CreateUnitSizeBody
{
    [Required, MaxLength(50), RegularExpression("^[a-z0-9-]+$")] public string? Key { get; init; }
    [Required, MaxLength(100)] public string? Label { get; init; }
    [MaxLength(100)] public string? DisplayName { get; init; }
    [MaxLength(1000)] public string? Description { get; init; }
    [MaxLength(100)] public string? UnitGroup { get; init; }
    [MaxLength(500)] public string? ImagePath { get; init; }
    [Range(typeof(decimal), "0", "99999999")] public decimal? MinAreaSqft { get; init; }
    [Range(typeof(decimal), "0", "99999999")] public decimal? MaxAreaSqft { get; init; }
    public int? SortOrder { get; init; }
    public Guid? StorageTypeId { get; init; }
}

/// <summary><see cref="Clear"/> may name minAreaSqft, maxAreaSqft, storageTypeId.</summary>
public sealed class UpdateUnitSizeBody
{
    [MaxLength(50), RegularExpression("^[a-z0-9-]+$")] public string? Key { get; init; }
    [MaxLength(100)] public string? Label { get; init; }
    [MaxLength(100)] public string? DisplayName { get; init; }
    [MaxLength(1000)] public string? Description { get; init; }
    [MaxLength(100)] public string? UnitGroup { get; init; }
    [MaxLength(500)] public string? ImagePath { get; init; }
    [Range(typeof(decimal), "0", "99999999")] public decimal? MinAreaSqft { get; init; }
    [Range(typeof(decimal), "0", "99999999")] public decimal? MaxAreaSqft { get; init; }
    public int? SortOrder { get; init; }
    public Guid? StorageTypeId { get; init; }
    public string[]? Clear { get; init; }
    [Required] public uint? Version { get; init; }
}

internal sealed record UnitListItem(Guid Id, string UnitName, Guid SiteId, string SiteName, string Currency, string? StorageTypeName,
    string? UnitSizeLabel, decimal? StandardRate, bool Available, bool Rented);

internal sealed record UnitResponse(Guid Id, string UnitName, Guid SiteId, string SiteName, string Currency, string? StorageTypeName,
    string? UnitSizeLabel, decimal Width, decimal Length, decimal AreaSqft, int? Floor, bool Climate, decimal? StandardRate, decimal? WebRate,
    bool Rented, bool Available, DateTime? DeletedAt, DateTime? LastSyncedAt);

internal sealed record UnitSyncStatus(Guid SiteId, string LocationCode, string SiteName, bool Active, int Units,
    DateTime? LastSyncedAt, DateTime? LastAttemptAt, string? LastError);

public sealed class UnitSyncBody
{
    /// <summary>True = "Full resync": every unit again from the beginning, then a new cursor.</summary>
    public bool Full { get; init; }
}
