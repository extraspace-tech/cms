using ExtraSpace.Api.Platform.Data;

namespace ExtraSpace.Api.Features.Sites;

public enum FacilityType { SelfStorage, Workspace }

/// <summary>A SiteLink facility (S1 spec §5.1). Staff-edited; the unit sync never writes it.</summary>
internal sealed class Site : IHasTimestamps, IHasStaffStamps, IAudited
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string LocationCode { get; set; }
    public int? SiteLinkSiteId { get; set; }
    public required string Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? HeroImageUrl { get; set; }
    public string? Address1 { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public required string Country { get; set; }
    public string? Phone { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public required string TimeZone { get; set; }
    public required string Currency { get; set; }
    public decimal TaxPercentage { get; set; }
    public required string TaxLabel { get; set; }
    public FacilityType FacilityType { get; set; } = FacilityType.SelfStorage;
    public bool Active { get; set; } = true;
    /// <summary>The reused Stripe connected account of this facility; null = no in-app payments here.</summary>
    public string? StripeAccountId { get; set; }
    public Guid? CreatedByStaffId { get; set; }
    public Guid? UpdatedByStaffId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public uint Version { get; set; }
}
