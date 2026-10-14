using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExtraSpace.Api.Features.Sites;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace ExtraSpace.Api.Tests.Features.Sites;

public sealed class TaxonomyAdminTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private static string NewKey() => "k-" + Guid.NewGuid().ToString("N")[..10];

    [Fact]
    public async Task A_storage_type_key_cannot_change_and_a_type_in_use_cannot_be_deleted()
    {
        var (client, _) = await api.StaffClientAsync(SitesPermissions.Manage);
        var created = await JsonAsync(await client.PostAsJsonAsync("/admin/api/storage-types", new { key = NewKey(), name = "Lockers", sortOrder = 5 }, Ct));
        var id = created.GetProperty("id").GetGuid();

        var patched = await JsonAsync(await client.PatchAsJsonAsync($"/admin/api/storage-types/{id}",
            new { key = "other", name = "Big lockers", version = created.GetProperty("version").GetUInt32() }, Ct));
        patched.GetProperty("key").GetString().Should().Be(created.GetProperty("key").GetString());   // UpdateStorageTypeBody has no key
        patched.GetProperty("name").GetString().Should().Be("Big lockers");

        var site = await api.CreateSiteAsync();
        await api.CreateUnitAsync(site.Id, u => u.StorageTypeId = id);
        var response = await client.DeleteAsync($"/admin/api/storage-types/{id}?version={patched.GetProperty("version").GetUInt32()}", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(response)).GetProperty("code").GetString().Should().Be("STORAGE_TYPE_IN_USE");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_seeded_storage_types_cannot_be_deleted(bool workspace)
    {
        var id = workspace ? StorageType.WorkspaceId : StorageType.SelfStorageId;
        var (client, _) = await api.StaffClientAsync(SitesPermissions.Manage);
        var type = await JsonAsync(await client.GetAsync($"/admin/api/storage-types/{id}", Ct));

        var response = await client.DeleteAsync($"/admin/api/storage-types/{id}?version={type.GetProperty("version").GetUInt32()}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(response);
        problem.GetProperty("code").GetString().Should().Be("STORAGE_TYPE_IN_USE");
        problem.GetProperty("title").GetString().Should().Contain("unit sync");
    }

    [Fact]
    public async Task A_unit_size_key_cannot_be_patched_to_blank()
    {
        var (client, _) = await api.StaffClientAsync(SitesPermissions.Manage);
        var size = await JsonAsync(await client.PostAsJsonAsync("/admin/api/unit-sizes",
            new { key = NewKey(), label = "Blank key", minAreaSqft = 99990m, maxAreaSqft = 99991m }, Ct));

        var response = await client.PatchAsJsonAsync($"/admin/api/unit-sizes/{size.GetProperty("id").GetGuid()}",
            new { key = "", version = size.GetProperty("version").GetUInt32() }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await JsonAsync(await client.GetAsync($"/admin/api/unit-sizes/{size.GetProperty("id").GetGuid()}", Ct)))
            .GetProperty("key").GetString().Should().Be(size.GetProperty("key").GetString());
    }

    [Fact]
    public async Task A_unit_size_needs_min_not_above_max()
    {
        var (client, _) = await api.StaffClientAsync(SitesPermissions.Manage);
        (await client.PostAsJsonAsync("/admin/api/unit-sizes", new { key = NewKey(), label = "Odd", minAreaSqft = 50m, maxAreaSqft = 10m }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Saving_a_unit_size_maps_the_units_it_covers_at_once()
    {
        var (client, _) = await api.StaffClientAsync(SitesPermissions.Manage);
        var site = await api.CreateSiteAsync();
        var unit = await api.CreateUnitAsync(site.Id, u => u.AreaSqft = 98765.43m);   // an area no other test uses

        var size = await JsonAsync(await client.PostAsJsonAsync("/admin/api/unit-sizes",
            new { key = NewKey(), label = "Huge", minAreaSqft = 98765m, maxAreaSqft = 98766m, sortOrder = -2_000_000_000 }, Ct));

        Guid? mapped = null;
        await api.WithDbAsync(async db => mapped = await db.Set<Unit>().Where(u => u.Id == unit.Id).Select(u => u.UnitSizeId).SingleAsync(Ct));
        mapped.Should().Be(size.GetProperty("id").GetGuid());

        var response = await client.DeleteAsync($"/admin/api/unit-sizes/{size.GetProperty("id").GetGuid()}?version={size.GetProperty("version").GetUInt32()}", Ct);
        (await JsonAsync(response)).GetProperty("code").GetString().Should().Be("UNIT_SIZE_IN_USE");
    }
}
