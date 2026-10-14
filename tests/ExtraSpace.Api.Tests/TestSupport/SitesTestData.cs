using ExtraSpace.Api.Features.Sites;

namespace ExtraSpace.Api.Tests.TestSupport;

internal static class SitesTestData
{
    /// <summary>A location code no other test uses (tests share one database and run in parallel).</summary>
    public static string NewCode() => "T" + Guid.NewGuid().ToString("N")[..9].ToUpperInvariant();

    public static async Task<Site> CreateSiteAsync(this ApiFactory api, string country = "SG", string? code = null)
    {
        var defaults = SiteCountries.Get(country);
        var site = new Site
        {
            LocationCode = code ?? NewCode(), Name = "Test site", Country = defaults.Code, Currency = defaults.Currency,
            TimeZone = defaults.TimeZone, TaxPercentage = 9m, TaxLabel = defaults.TaxLabel, Active = true,
        };
        await api.WithDbAsync(async db =>
        {
            db.Add(site);
            await db.SaveChangesAsync();
        });
        return site;
    }

    public static async Task<Unit> CreateUnitAsync(this ApiFactory api, Guid siteId, Action<Unit>? change = null)
    {
        var unit = new Unit
        {
            SiteId = siteId, SiteLinkUnitId = Random.Shared.Next(1, int.MaxValue), UnitName = "U" + Random.Shared.Next(100000, 999999),
            Width = 5, Length = 10, AreaSqft = 50, Rentable = true, StorageTypeId = StorageType.SelfStorageId,
        };
        change?.Invoke(unit);
        await api.WithDbAsync(async db =>
        {
            db.Add(unit);
            await db.SaveChangesAsync();
        });
        return unit;
    }
}
