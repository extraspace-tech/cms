using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExtraSpace.Api.Features.Sites;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace ExtraSpace.Api.Tests.Features.Sites;

public sealed class UnitsAdminTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    [Fact]
    public async Task Units_filter_by_site_and_availability_and_show_the_site_currency()
    {
        var (client, _) = await api.StaffClientAsync();
        var site = await api.CreateSiteAsync("MY");
        await api.CreateUnitAsync(site.Id, u => { u.UnitName = "A1"; u.StandardRate = 120m; });
        await api.CreateUnitAsync(site.Id, u => { u.UnitName = "A2"; u.Rented = true; });

        var all = await JsonAsync(await client.GetAsync($"/admin/api/units?siteId={site.Id}", Ct));
        var available = await JsonAsync(await client.GetAsync($"/admin/api/units?siteId={site.Id}&available=true", Ct));

        all.GetProperty("total").GetInt32().Should().Be(2);
        all.GetProperty("items")[0].GetProperty("unitName").GetString().Should().Be("A1");   // ordered by unit name
        all.GetProperty("items")[0].GetProperty("currency").GetString().Should().Be("MYR");
        all.GetProperty("items")[0].GetProperty("standardRate").GetDecimal().Should().Be(120m);
        available.GetProperty("total").GetInt32().Should().Be(1);
        (await client.GetAsync($"/admin/api/units/{Guid.CreateVersion7()}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sync_now_queues_a_refresh_that_updates_the_status()
    {
        var (client, actor) = await api.StaffClientAsync(SitesPermissions.Manage);
        var site = await api.CreateSiteAsync();
        api.SiteLink.Reply("UnitsInformation_v3", site.LocationCode, SiteLinkXml.Reply("UnitsInformation_v3",
            ("Table", SiteLinkXml.Row(("UnitID", "1"), ("sUnitName", "A1"))),
            ("Table1", SiteLinkXml.Row(("lngLastTimePolled", "638900000000000002")))));

        (await client.PostAsJsonAsync($"/admin/api/sites/{site.Id}/unit-sync", new { full = false }, Ct)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        JsonElement status = default;
        for (var i = 0; i < 100; i++)
        {
            var list = await JsonAsync(await client.GetAsync("/admin/api/unit-sync", Ct));
            status = list.EnumerateArray().Single(s => s.GetProperty("siteId").GetGuid() == site.Id);
            if (status.GetProperty("lastSyncedAt").ValueKind != JsonValueKind.Null) break;
            await Task.Delay(100, Ct);
        }
        status.GetProperty("units").GetInt32().Should().Be(1);
        status.GetProperty("lastError").ValueKind.Should().Be(JsonValueKind.Null);

        // Who asked: the request's staffId is on the line (StaffAccess adds it to every line of a staff request).
        var requested = api.Logs.All().Single(l => Str(l, "messageTemplate") == "Unit refresh of {siteCode} ({mode}) requested by staff"
            && Str(l, "siteCode") == site.LocationCode);
        Str(requested, "staffId").Should().Be(actor.Id.ToString());
        Str(requested, "mode").Should().Be("Incremental");
    }

    private static string? Str(JsonElement line, string name) => line.TryGetProperty(name, out var value) ? value.ToString() : null;

    [Fact]
    public async Task Starting_a_sync_needs_sites_manage()
    {
        var (client, _) = await api.StaffClientAsync();
        var site = await api.CreateSiteAsync();
        (await client.PostAsJsonAsync($"/admin/api/sites/{site.Id}/unit-sync", new { full = true }, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await (await api.StaffClientAsync(SitesPermissions.Manage)).Client
            .PostAsJsonAsync($"/admin/api/sites/{Guid.CreateVersion7()}/unit-sync", new { full = true }, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
