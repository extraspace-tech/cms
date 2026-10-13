using System.Net;
using System.Text.RegularExpressions;
using ExtraSpace.Api.Features.Admin;
using ExtraSpace.Api.Tests.TestSupport;

namespace ExtraSpace.Api.Tests.Features.Admin;

public sealed partial class JobsDashboardTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FormUrlEncodedContent Trigger() => new([new("jobs[]", "IdentityHousekeeping")]);

    private static string Meta(string html, string name) =>
        Regex.Match(html, $"<meta name=\"{name}\" content=\"([^\"]*)\"").Groups[1].Value;

    [Fact]
    public async Task The_dashboard_needs_a_signed_in_staff_member()
    {
        var response = await api.AdminClient().GetAsync("/admin/jobs", Ct);

        await Problem.ReadAsync(response, 401, "UNAUTHENTICATED");
        response.Headers.Location.Should().BeNull();
    }

    [Fact]
    public async Task The_dashboard_needs_jobs_manage()
    {
        var (client, _) = await api.StaffClientAsync(AdminPermissions.StaffManage, AdminPermissions.RolesManage, AdminPermissions.AuditRead);

        await Problem.ReadAsync(await client.GetAsync("/admin/jobs", Ct), 403, "FORBIDDEN");
        await Problem.ReadAsync(await client.PostAsync("/admin/jobs/recurring/trigger", Trigger(), Ct), 403, "FORBIDDEN");
    }

    [Fact]
    public async Task Staff_with_jobs_manage_open_the_dashboard()
    {
        var (client, _) = await api.StaffClientAsync(AdminPermissions.JobsManage);

        var response = await client.GetAsync("/admin/jobs", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        var html = await response.Content.ReadAsStringAsync(Ct);
        var assets = AssetLinks().Matches(html).Select(m => m.Groups[1].Value).ToList();
        assets.Should().Contain(a => a.Contains("/admin/jobs/css")).And.Contain(a => a.Contains("/admin/jobs/js"));
        foreach (var asset in assets)
        {
            (await client.GetAsync(asset, Ct)).StatusCode.Should().Be(HttpStatusCode.OK, asset);
            (await api.AdminClient().GetAsync(asset, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, asset);
        }
    }

    [Fact]
    public async Task Dashboard_actions_need_the_antiforgery_token()
    {
        var role = await api.CreateRoleAsync(AdminPermissions.JobsManage);
        var client = await api.SignedInAdminAsync(await api.CreateStaffAsync(role.Id), withCsrf: false);
        // The dashboard page carries the header name and token its own script sends on every POST.
        var html = await client.GetStringAsync("/admin/jobs/recurring", Ct);
        var header = Meta(html, "csrf-header");
        var token = Meta(html, "csrf-token");
        header.Should().Be(StaffAuth.CsrfHeader);
        token.Should().NotBeEmpty();

        var without = await client.PostAsync("/admin/jobs/recurring/trigger", Trigger(), Ct);
        using var withToken = new HttpRequestMessage(HttpMethod.Post, "/admin/jobs/recurring/trigger") { Content = Trigger() };
        withToken.Headers.Add(header, token);
        var with = await client.SendAsync(withToken, Ct);

        without.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        with.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_dashboard_action_logs_the_staff_member()
    {
        const string template = "Jobs dashboard action {path} by staff {staffId}";
        var (client, staff) = await api.StaffClientAsync(AdminPermissions.JobsManage);

        var page = await client.GetAsync("/admin/jobs/recurring", Ct);
        // Without the dashboard's own token Hangfire refuses the POST: no action ran, so no action line.
        using var refusedRequest = new HttpRequestMessage(HttpMethod.Post, "/admin/jobs/recurring/trigger") { Content = Trigger() };
        refusedRequest.Headers.Add(StaffAuth.CsrfHeader, "not-a-token");
        var refused = await client.SendAsync(refusedRequest, Ct);
        var done = await client.PostAsync("/admin/jobs/recurring/trigger?note=never-logged", Trigger(), Ct);

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        done.StatusCode.Should().Be(HttpStatusCode.NoContent);
        bool IsAction(System.Text.Json.JsonElement l) => l.GetProperty("messageTemplate").GetString() == template;
        api.Logs.LinesFor(Problem.TraceIdOf(page)).Where(IsAction).Should().BeEmpty();
        api.Logs.LinesFor(Problem.TraceIdOf(refused)).Where(IsAction).Should().BeEmpty();
        var lines = api.Logs.LinesFor(Problem.TraceIdOf(done));
        var action = lines.Single(IsAction);
        action.GetProperty("level").GetString().Should().Be("Information");
        action.GetProperty("path").GetString().Should().Be("/admin/jobs/recurring/trigger");
        action.GetProperty("staffId").GetString().Should().Be(staff.Id.ToString());
        action.GetRawText().Should().NotContain("never-logged").And.NotContain("IdentityHousekeeping");
        lines.Single(l => l.GetProperty("messageTemplate").GetString() == "HTTP {method} {route} responded {status} in {elapsedMs} ms")
            .GetProperty("staffId").GetString().Should().Be(staff.Id.ToString());
    }

    [GeneratedRegex("(?:href|src)=\"(/admin/jobs/(?:css|js)[^\"]*)\"")]
    private static partial Regex AssetLinks();
}
