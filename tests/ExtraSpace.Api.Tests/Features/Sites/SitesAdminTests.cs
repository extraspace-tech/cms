using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExtraSpace.Api.Features.Sites;
using ExtraSpace.Api.Tests.TestSupport;

namespace ExtraSpace.Api.Tests.Features.Sites;

public sealed class SitesAdminTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<(HttpClient Client, global::ExtraSpace.Api.Features.Admin.StaffUser Staff)> ManagerAsync() => api.StaffClientAsync(SitesPermissions.Manage);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private static object NewSite(string code, string country = "MY") =>
        new { locationCode = code, name = "Bukit " + code, country, taxPercentage = 6m, city = "Kuala Lumpur" };

    [Fact]
    public async Task Creating_a_site_fills_currency_zone_and_tax_label_from_the_country_and_is_audited()
    {
        var (client, actor) = await ManagerAsync();
        var code = SitesTestData.NewCode();

        var response = await client.PostAsJsonAsync("/admin/api/sites", NewSite(code), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var site = await JsonAsync(response);
        site.GetProperty("currency").GetString().Should().Be("MYR");
        site.GetProperty("timeZone").GetString().Should().Be("Asia/Kuala_Lumpur");
        site.GetProperty("taxLabel").GetString().Should().Be("SST");
        site.GetProperty("facilityType").GetString().Should().Be("SelfStorage");
        site.GetProperty("active").GetBoolean().Should().BeTrue();
        var audit = (await api.AuditRowsForAsync(site.GetProperty("id").GetGuid())).Should().ContainSingle().Subject;
        audit.EntityType.Should().Be("site");
        audit.StaffUserId.Should().Be(actor.Id);
    }

    [Fact]
    public async Task Any_staff_can_read_sites_but_only_sites_manage_can_write()
    {
        var (reader, _) = await api.StaffClientAsync();
        (await reader.GetAsync("/admin/api/sites", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await reader.PostAsJsonAsync("/admin/api/sites", NewSite(SitesTestData.NewCode()), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_patch_changes_only_what_it_sends_clears_on_request_and_needs_the_version()
    {
        var (client, _) = await ManagerAsync();
        var created = await JsonAsync(await client.PostAsJsonAsync("/admin/api/sites",
            new { locationCode = SitesTestData.NewCode(), name = "N", country = "SG", taxPercentage = 9m, displayName = "Shown", latitude = 1.3m, city = "Singapore" }, Ct));
        var id = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetUInt32();

        var response = await client.PatchAsJsonAsync($"/admin/api/sites/{id}",
            new { displayName = "", clear = new[] { "latitude" }, phone = "+65 6000 0000", version }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var site = await JsonAsync(response);
        site.GetProperty("displayName").ValueKind.Should().Be(JsonValueKind.Null);
        site.GetProperty("latitude").ValueKind.Should().Be(JsonValueKind.Null);
        site.GetProperty("phone").GetString().Should().Be("+65 6000 0000");
        site.GetProperty("city").GetString().Should().Be("Singapore");

        (await client.PatchAsJsonAsync($"/admin/api/sites/{id}", new { name = "Again", version }, Ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PatchAsJsonAsync($"/admin/api/sites/{id}", new { clear = new[] { "name" }, version = site.GetProperty("version").GetUInt32() }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PatchAsJsonAsync($"/admin/api/sites/{id}", new { latitude = 1m, clear = new[] { "latitude" }, version = site.GetProperty("version").GetUInt32() }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);   // set and clear at once
        (await client.PatchAsJsonAsync($"/admin/api/sites/{id}", new { name = "Fresh", version = site.GetProperty("version").GetUInt32() }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);   // the PATCH answer carries the new version
    }

    [Fact]
    public async Task A_stale_delete_is_409()
    {
        var (client, _) = await ManagerAsync();
        var created = await JsonAsync(await client.PostAsJsonAsync("/admin/api/sites", NewSite(SitesTestData.NewCode()), Ct));
        var id = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetUInt32();
        (await client.PatchAsJsonAsync($"/admin/api/sites/{id}", new { name = "Changed", version }, Ct)).EnsureSuccessStatusCode();

        var response = await client.DeleteAsync($"/admin/api/sites/{id}?version={version}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(response)).GetProperty("code").GetString().Should().Be("CONFLICT");
        (await client.GetAsync($"/admin/api/sites/{id}", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Changing_the_country_changes_the_currency()
    {
        var (client, _) = await ManagerAsync();
        var created = await JsonAsync(await client.PostAsJsonAsync("/admin/api/sites", NewSite(SitesTestData.NewCode(), "SG"), Ct));

        var site = await JsonAsync(await client.PatchAsJsonAsync($"/admin/api/sites/{created.GetProperty("id").GetGuid()}",
            new { country = "KR", version = created.GetProperty("version").GetUInt32() }, Ct));

        site.GetProperty("currency").GetString().Should().Be("KRW");
        site.GetProperty("timeZone").GetString().Should().Be("Asia/Singapore");   // only a default on create
    }

    [Theory]
    [InlineData("timeZone", "Mars/Olympus")]
    [InlineData("timeZone", "Singapore Standard Time")]   // a Windows id resolves through ICU, but only IANA ids are stored
    [InlineData("country", "sg")]   // codes are upper case, never folded
    [InlineData("heroImageUrl", "http://example.test/a.png")]
    [InlineData("stripeAccountId", "cus_123")]
    [InlineData("country", "US")]
    public async Task Bad_values_are_400(string field, string value)
    {
        var (client, _) = await ManagerAsync();
        var body = new Dictionary<string, object?>
        {
            ["locationCode"] = SitesTestData.NewCode(), ["name"] = "N", ["country"] = "SG", ["taxPercentage"] = 9m, [field] = value,
        };
        (await client.PostAsJsonAsync("/admin/api/sites", body, Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_taken_location_code_is_409()
    {
        var (client, _) = await ManagerAsync();
        var code = SitesTestData.NewCode();
        await client.PostAsJsonAsync("/admin/api/sites", NewSite(code), Ct);
        (await client.PostAsJsonAsync("/admin/api/sites", NewSite(code), Ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_site_with_units_cannot_be_deleted()
    {
        var (client, _) = await ManagerAsync();
        var site = await api.CreateSiteAsync();
        await api.CreateUnitAsync(site.Id);
        var current = await JsonAsync(await client.GetAsync($"/admin/api/sites/{site.Id}", Ct));

        var response = await client.DeleteAsync($"/admin/api/sites/{site.Id}?version={current.GetProperty("version").GetUInt32()}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(response)).GetProperty("code").GetString().Should().Be("SITE_IN_USE");

        var empty = await JsonAsync(await client.PostAsJsonAsync("/admin/api/sites", NewSite(SitesTestData.NewCode()), Ct));
        (await client.DeleteAsync($"/admin/api/sites/{empty.GetProperty("id").GetGuid()}?version={empty.GetProperty("version").GetUInt32()}", Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData("locationCode", null)]
    [InlineData("country", "MY")]
    public async Task The_location_code_and_country_of_a_site_with_units_cannot_change(string field, string? value)
    {
        var (client, _) = await ManagerAsync();
        var site = await api.CreateSiteAsync();
        await api.CreateUnitAsync(site.Id);
        var version = (await JsonAsync(await client.GetAsync($"/admin/api/sites/{site.Id}", Ct))).GetProperty("version").GetUInt32();

        var response = await client.PatchAsJsonAsync($"/admin/api/sites/{site.Id}",
            new Dictionary<string, object?> { [field] = value ?? SitesTestData.NewCode(), ["version"] = version }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(response)).GetProperty("code").GetString().Should().Be("SITE_IN_USE");
        var unchanged = await JsonAsync(await client.GetAsync($"/admin/api/sites/{site.Id}", Ct));
        unchanged.GetProperty("locationCode").GetString().Should().Be(site.LocationCode);
        unchanged.GetProperty("country").GetString().Should().Be("SG");

        // Sending the stored values again is not a change.
        (await client.PatchAsJsonAsync($"/admin/api/sites/{site.Id}",
            new { locationCode = site.LocationCode.ToLowerInvariant(), country = "SG", name = "Renamed", version }, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_location_code_and_country_of_a_site_without_units_can_change()
    {
        var (client, _) = await ManagerAsync();
        var created = await JsonAsync(await client.PostAsJsonAsync("/admin/api/sites", NewSite(SitesTestData.NewCode(), "SG"), Ct));
        var code = SitesTestData.NewCode();

        var response = await client.PatchAsJsonAsync($"/admin/api/sites/{created.GetProperty("id").GetGuid()}",
            new { locationCode = code, country = "MY", version = created.GetProperty("version").GetUInt32() }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var site = await JsonAsync(response);
        site.GetProperty("locationCode").GetString().Should().Be(code);
        site.GetProperty("currency").GetString().Should().Be("MYR");
    }

    [Fact]
    public async Task The_list_pages_and_searches_name_code_and_city()
    {
        var (client, _) = await ManagerAsync();
        var tag = "Srch" + Guid.NewGuid().ToString("N")[..6];
        for (var i = 0; i < 3; i++)
            await client.PostAsJsonAsync("/admin/api/sites",
                new { locationCode = SitesTestData.NewCode(), name = $"{tag} {i}", country = "SG", taxPercentage = 9m }, Ct);

        var page = await JsonAsync(await client.GetAsync($"/admin/api/sites?search={tag.ToLowerInvariant()}&pageSize=2", Ct));

        page.GetProperty("total").GetInt32().Should().Be(3);
        page.GetProperty("items").GetArrayLength().Should().Be(2);
        page.GetProperty("pageSize").GetInt32().Should().Be(2);
        (await client.GetAsync("/admin/api/sites?search=100%25_off", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);   // % and _ are literal
        (await client.GetAsync("/admin/api/sites?pageSize=101", Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Search_wildcards_match_only_themselves()
    {
        var (client, _) = await ManagerAsync();
        var tag = "Wild" + Guid.NewGuid().ToString("N")[..6];
        foreach (var name in new[] { $"{tag} a_b", $"{tag} axb", $"{tag} 5%", $"{tag} 50" })
            await client.PostAsJsonAsync("/admin/api/sites", new { locationCode = SitesTestData.NewCode(), name, country = "SG", taxPercentage = 9m }, Ct);

        var underscore = await JsonAsync(await client.GetAsync($"/admin/api/sites?search={Uri.EscapeDataString(tag + " a_b")}", Ct));
        var percent = await JsonAsync(await client.GetAsync($"/admin/api/sites?search={Uri.EscapeDataString(tag + " 5%")}", Ct));

        underscore.GetProperty("total").GetInt32().Should().Be(1);
        percent.GetProperty("total").GetInt32().Should().Be(1);
    }
}
