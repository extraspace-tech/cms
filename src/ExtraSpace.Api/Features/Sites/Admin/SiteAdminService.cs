using ExtraSpace.Api.Platform.Data;
using ExtraSpace.Api.Platform.Http;
using Microsoft.EntityFrameworkCore;

namespace ExtraSpace.Api.Features.Sites.Admin;

internal sealed class SiteAdminService(AppDbContext db)
{
    private static readonly string[] Clearable = ["siteLinkSiteId", "latitude", "longitude"];

    public Task<Paged<SiteListItem>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var rows = db.Set<Site>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = Paging.Contains(search);
            rows = rows.Where(s => EF.Functions.ILike(s.Name, pattern, Paging.Escape) || EF.Functions.ILike(s.LocationCode, pattern, Paging.Escape)
                || (s.City != null && EF.Functions.ILike(s.City, pattern, Paging.Escape)));
        }
        return rows.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .Select(s => new SiteListItem(s.Id, s.LocationCode, s.Name, s.FacilityType, s.City, s.Country, s.Active, s.Version))
            .ToPageAsync(page, pageSize, cancellationToken);
    }

    public async Task<SiteResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Response(await db.Set<Site>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken) ?? throw SitesErrors.NotFound("Site"));

    public async Task<SiteResponse> CreateAsync(CreateSiteBody body, CancellationToken cancellationToken)
    {
        var country = SiteCountries.Get(body.Country!);
        var site = new Site
        {
            LocationCode = Code(body.LocationCode!),
            SiteLinkSiteId = body.SiteLinkSiteId,
            Name = Patch.Required(body.Name!, "Name"),
            Country = country.Code,
            Currency = country.Currency,
            TimeZone = Zone(Patch.Blank(body.TimeZone) ?? country.TimeZone),
            TaxPercentage = Tax(body.TaxPercentage!.Value),
            TaxLabel = Patch.Blank(body.TaxLabel) ?? country.TaxLabel,
            FacilityType = body.FacilityType ?? FacilityType.SelfStorage,
            Active = body.Active ?? true,
            DisplayName = Patch.Blank(body.DisplayName),
            Description = Patch.Blank(body.Description),
            HeroImageUrl = Https(Patch.Blank(body.HeroImageUrl)),
            Address1 = Patch.Blank(body.Address1),
            City = Patch.Blank(body.City),
            Region = Patch.Blank(body.Region),
            PostalCode = Patch.Blank(body.PostalCode),
            Phone = Patch.Blank(body.Phone),
            Latitude = body.Latitude,
            Longitude = body.Longitude,
            StripeAccountId = Patch.Blank(body.StripeAccountId),
        };
        db.Add(site);
        await db.SaveChangesAsync(cancellationToken);   // a taken location code or SiteLink id → unique violation → 409 CONFLICT
        return Response(site);
    }

    public async Task<SiteResponse> UpdateAsync(Guid id, UpdateSiteBody body, CancellationToken cancellationToken)
    {
        var site = await db.Set<Site>().SingleOrDefaultAsync(s => s.Id == id, cancellationToken) ?? throw SitesErrors.NotFound("Site");
        db.Entry(site).Property(s => s.Version).OriginalValue = body.Version!.Value;   // stale → 409
        var clear = Patch.Clears(body.Clear, Clearable);
        var code = body.LocationCode is null ? site.LocationCode : Code(body.LocationCode);
        var country = body.Country is null ? null : SiteCountries.Get(body.Country);
        // The refresh cursor belongs to the SiteLink location, and stored prices to the currency: neither may move under units.
        if ((code != site.LocationCode || (country is not null && country.Code != site.Country))
            && await db.Set<Unit>().AnyAsync(u => u.SiteId == id, cancellationToken))
            throw SitesErrors.SiteInUse("The location code and country cannot change while the site has units.");
        site.LocationCode = code;
        if (body.Name is not null) site.Name = Patch.Required(body.Name, "Name");
        if (country is not null)
        {
            // The currency follows the country; the zone and tax label were only defaults (S1-1 decision 10).
            site.Country = country.Code;
            site.Currency = country.Currency;
        }
        if (body.TimeZone is not null) site.TimeZone = Zone(body.TimeZone);
        if (body.TaxPercentage is { } tax) site.TaxPercentage = Tax(tax);
        if (body.TaxLabel is not null) site.TaxLabel = Patch.Required(body.TaxLabel, "Tax label");
        if (body.FacilityType is { } facility) site.FacilityType = facility;
        if (body.Active is { } active) site.Active = active;
        Patch.Value(body.SiteLinkSiteId, "siteLinkSiteId", clear, v => site.SiteLinkSiteId = v);
        Patch.Value(body.Latitude, "latitude", clear, v => site.Latitude = v);
        Patch.Value(body.Longitude, "longitude", clear, v => site.Longitude = v);
        Patch.Text(body.DisplayName, v => site.DisplayName = v);
        Patch.Text(body.Description, v => site.Description = v);
        Patch.Text(body.HeroImageUrl, v => site.HeroImageUrl = Https(v));
        Patch.Text(body.Address1, v => site.Address1 = v);
        Patch.Text(body.City, v => site.City = v);
        Patch.Text(body.Region, v => site.Region = v);
        Patch.Text(body.PostalCode, v => site.PostalCode = v);
        Patch.Text(body.Phone, v => site.Phone = v);
        Patch.Text(body.StripeAccountId, v => site.StripeAccountId = v);
        await db.SaveChangesAsync(cancellationToken);
        return Response(site);
    }

    public async Task DeleteAsync(Guid id, uint version, CancellationToken cancellationToken)
    {
        var site = await db.Set<Site>().SingleOrDefaultAsync(s => s.Id == id, cancellationToken) ?? throw SitesErrors.NotFound("Site");
        db.Entry(site).Property(s => s.Version).OriginalValue = version;
        if (await db.Set<Unit>().AnyAsync(u => u.SiteId == id, cancellationToken)) throw SitesErrors.SiteInUse();
        db.Remove(site);
        await db.SaveChangesAsync(cancellationToken);   // a unit synced since the check → foreign-key violation → 409 CONFLICT
    }

    private static string Code(string code) =>
        Patch.Blank(code)?.ToUpperInvariant() ?? throw SitesErrors.Invalid("The location code cannot be blank.");

    // IANA ids only (the lookup also maps Windows ids); SiteClock.Zone makes the same lookup later, so a stored zone always resolves.
    private static string Zone(string id) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(id.Trim(), out var zone) && zone.HasIanaId
            ? id.Trim() : throw SitesErrors.Invalid($"Unknown time zone '{id}'. Use an IANA id such as Asia/Singapore.");

    // Validated as stored: numeric(5,2) would round a third decimal away silently.
    private static decimal Tax(decimal percentage) =>
        decimal.Round(percentage, 2) == percentage ? percentage : throw SitesErrors.Invalid("The tax percentage can have at most 2 decimals.");

    private static string? Https(string? url) =>
        url is null || (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            ? url : throw SitesErrors.Invalid("The image address must be an https URL.");

    private static SiteResponse Response(Site s) => new(s.Id, s.LocationCode, s.SiteLinkSiteId, s.Name, s.DisplayName, s.Description,
        s.HeroImageUrl, s.Address1, s.City, s.Region, s.PostalCode, s.Country, s.Phone, s.Latitude, s.Longitude, s.TimeZone, s.Currency,
        s.TaxPercentage, s.TaxLabel, s.FacilityType, s.Active, s.StripeAccountId, s.Version);
}
