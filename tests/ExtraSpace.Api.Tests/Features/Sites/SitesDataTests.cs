using ExtraSpace.Api.Features.Sites;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExtraSpace.Api.Tests.Features.Sites;

public sealed class SitesDataTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Unit> ReloadAsync(Guid id)
    {
        Unit? unit = null;
        await api.WithDbAsync(async db => unit = await db.Set<Unit>().AsNoTracking().SingleAsync(u => u.Id == id, Ct));
        return unit!;
    }

    [Fact]
    public async Task Available_is_computed_by_the_database()
    {
        var site = await api.CreateSiteAsync();
        var unit = await api.CreateUnitAsync(site.Id);
        (await ReloadAsync(unit.Id)).Available.Should().BeTrue();

        await api.WithDbAsync(async db =>
        {
            var row = await db.Set<Unit>().SingleAsync(u => u.Id == unit.Id, Ct);
            row.Rented = true;
            await db.SaveChangesAsync(Ct);
        });
        (await ReloadAsync(unit.Id)).Available.Should().BeFalse();

        var deleted = await api.CreateUnitAsync(site.Id, u => u.DeletedAt = DateTime.UtcNow);
        (await ReloadAsync(deleted.Id)).Available.Should().BeFalse();
        var waiting = await api.CreateUnitAsync(site.Id, u => u.WaitingListReserved = true);
        (await ReloadAsync(waiting.Id)).Available.Should().BeFalse();
        var unrentable = await api.CreateUnitAsync(site.Id, u => u.Rentable = false);
        (await ReloadAsync(unrentable.Id)).Available.Should().BeFalse();
    }

    [Fact]
    public async Task A_SiteLink_unit_id_is_unique_within_a_site()
    {
        var site = await api.CreateSiteAsync();
        var unit = await api.CreateUnitAsync(site.Id);
        var duplicate = () => api.CreateUnitAsync(site.Id, u => u.SiteLinkUnitId = unit.SiteLinkUnitId);
        (await duplicate.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);

        var other = await api.CreateSiteAsync();
        await api.CreateUnitAsync(other.Id, u => u.SiteLinkUnitId = unit.SiteLinkUnitId);   // another site may reuse it
    }

    [Theory]
    [InlineData("US", "SGD")]
    [InlineData("SG", "USD")]
    [InlineData("SG", "MYR")]
    public async Task Country_and_currency_are_checked(string country, string currency)
    {
        var save = () => api.WithDbAsync(async db =>
        {
            db.Add(new Site
            {
                LocationCode = SitesTestData.NewCode(), Name = "x", Country = country, Currency = currency,
                TimeZone = "Asia/Singapore", TaxPercentage = 9m, TaxLabel = "GST", Active = true,
            });
            await db.SaveChangesAsync(Ct);
        });
        (await save.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task The_two_storage_types_are_seeded()
    {
        List<StorageType> types = [];
        await api.WithDbAsync(async db => types = await db.Set<StorageType>().AsNoTracking()
            .Where(t => t.Id == StorageType.SelfStorageId || t.Id == StorageType.WorkspaceId).ToListAsync(Ct));
        types.Should().HaveCount(2);
        types.Single(t => t.Key == "self-storage").Active.Should().BeTrue();
        types.Single(t => t.Key == "workspace").Active.Should().BeFalse();
    }

    [Theory]
    [InlineData(" BizPlus ", "workspace")]
    [InlineData("bizplus", "workspace")]
    [InlineData("Walk In", "self-storage")]
    [InlineData("", "self-storage")]
    public void C7_BizPlus_units_are_workspace(string typeName, string key) =>
        StorageType.KeyFor(typeName).Should().Be(key);
}
