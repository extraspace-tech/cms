using ExtraSpace.Api.Features.Sites;
using ExtraSpace.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace ExtraSpace.Api.Tests.Features.Sites;

public sealed class UnitSizeMapTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_unit_gets_the_first_size_whose_range_and_type_fit()
    {
        // A database of its own: unit sizes are global and other tests create them.
        var connection = await api.CreateDatabaseAsync("sizes");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).UseSnakeCaseNamingConvention().Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync(Ct);

        // The bare context has no TimestampsInterceptor: the stamps are set by hand.
        var created = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var site = new Site
        {
            LocationCode = "S1", Name = "s", Country = "SG", Currency = "SGD", TimeZone = "Asia/Singapore", TaxPercentage = 9, TaxLabel = "GST",
            CreatedAt = created, UpdatedAt = created,
        };
        UnitSize NewSize(string key, int sortOrder, decimal? min, decimal? max, Guid? type) => new()
        {
            Key = key, Label = key, MinAreaSqft = min, MaxAreaSqft = max, SortOrder = sortOrder, StorageTypeId = type,
            CreatedAt = created, UpdatedAt = created,
        };
        var small = NewSize("small", 1, 0, 50, StorageType.SelfStorageId);
        var medium = NewSize("medium", 2, 50, 100, null);
        var large = NewSize("large", 3, 100, null, null);
        var desk = NewSize("desk", 0, null, 30, StorageType.WorkspaceId);
        db.AddRange(site, small, medium, large, desk);
        Unit NewUnit(int id, decimal area, Guid type) => new()
        {
            SiteId = site.Id, SiteLinkUnitId = id, UnitName = "U" + id, AreaSqft = area, StorageTypeId = type, Rentable = true,
            CreatedAt = created, UpdatedAt = created,
        };
        var units = new[]
        {
            NewUnit(1, 25, StorageType.SelfStorageId), NewUnit(2, 50, StorageType.SelfStorageId),
            NewUnit(3, 150, StorageType.SelfStorageId), NewUnit(4, 10, StorageType.WorkspaceId),
            NewUnit(5, 0, StorageType.SelfStorageId),   // no dimensions in SiteLink: no size, and a stale one is cleared
        };
        units[4].UnitSizeId = small.Id;
        db.AddRange(units);
        await db.SaveChangesAsync(Ct);
        var now = new DateTimeOffset(2026, 10, 14, 1, 2, 3, TimeSpan.Zero);

        (await UnitSizeMap.RemapAsync(db, null, new FakeTimeProvider(now), Ct)).Should().Be(5);

        var rows = await db.Set<Unit>().AsNoTracking().OrderBy(u => u.SiteLinkUnitId).ToListAsync(Ct);
        rows.Select(u => u.UnitSizeId).Should().Equal(small.Id, medium.Id, large.Id, desk.Id, null);   // min inclusive, max exclusive; the desk size is workspace-only
        rows.Should().AllSatisfy(u => u.UpdatedAt.Should().Be(now.UtcDateTime));
        (await UnitSizeMap.RemapAsync(db, site.Id, new FakeTimeProvider(now.AddHours(1)), Ct)).Should().Be(0);   // nothing changed: nothing written
    }
}
