using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExtraSpace.Api.Features.Admin;
using ExtraSpace.Api.Tests.TestSupport;

namespace ExtraSpace.Api.Tests.Features.Admin;

/// <summary>S0 spec §11 items 4–5, with the test sign-in route in place of an emailed code.</summary>
[Collection("Owners")]
public sealed class AdminFlowTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string CookieValue(HttpResponseMessage response, string name) =>
        response.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]).Single(c => c.StartsWith(name + "=", StringComparison.Ordinal))[(name.Length + 1)..];

    [Fact]
    public async Task An_owner_adds_a_limited_staff_member_who_cannot_manage_staff()
    {
        var owner = await api.CreateStaffAsync(Role.OwnerId);
        var ownerClient = await api.SignedInAdminAsync(owner);

        var roleResponse = await ownerClient.PostAsJsonAsync("/admin/api/roles", new { name = $"auditor-{Guid.NewGuid():N}", permissions = new[] { AdminPermissions.AuditRead } }, Ct);
        roleResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var roleId = (await roleResponse.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        var staffResponse = await ownerClient.PostAsJsonAsync("/admin/api/staff", new { email = IdentityTestData.NewEmail("limited"), roleId }, Ct);
        staffResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var limitedId = (await staffResponse.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        var limited = await api.SignedInAdminAsync(new StaffUser { Id = limitedId, Email = "unused@example.test" });
        await Problem.ReadAsync(await limited.GetAsync("/admin/api/staff", Ct), 403, "FORBIDDEN");
        await Problem.ReadAsync(await limited.PostAsJsonAsync("/admin/api/staff", new { email = IdentityTestData.NewEmail("refused"), roleId }, Ct), 403, "FORBIDDEN");
        await Problem.ReadAsync(await limited.GetAsync("/admin/jobs", Ct), 403, "FORBIDDEN");

        var audit = await limited.GetAsync($"/admin/api/audit?staffId={owner.Id}", Ct);
        audit.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = (await audit.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("staffId").GetGuid()).Should().AllBeEquivalentTo(owner.Id);
        items.Select(i => (i.GetProperty("action").GetString(), i.GetProperty("entityId").GetGuid())).Should().BeEquivalentTo(
            [("POST /admin/api/staff", limitedId), ("POST /admin/api/roles", roleId)], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task An_admin_flow_logs_no_cookie_or_csrf_token()
    {
        var staff = await api.CreateStaffAsync((await api.CreateRoleAsync(AdminPermissions.RolesManage)).Id);
        var client = api.AdminClient();

        var signIn = await client.PostAsync($"/test/admin/sign-in/{staff.Id}", null, Ct);
        signIn.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var csrf = await client.GetAsync("/admin/api/auth/csrf", Ct);
        csrf.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = (await csrf.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Add(StaffAuth.CsrfHeader, token);
        var write = await client.PostAsJsonAsync("/admin/api/roles", new { name = $"logged-{Guid.NewGuid():N}", permissions = Array.Empty<string>() }, Ct);
        write.StatusCode.Should().Be(HttpStatusCode.Created);

        string[] secrets = [CookieValue(signIn, StaffAuth.CookieName), CookieValue(csrf, StaffAuth.CsrfCookieName), token];
        api.Logs.LinesFor(Problem.TraceIdOf(write)).Should().NotBeEmpty("the write's own lines are captured before the search");
        var lines = api.Logs.All().Select(l => l.GetRawText()).ToList();
        foreach (var secret in secrets)
        {
            secret.Length.Should().BeGreaterThan(20);
            lines.Should().NotContain(l => l.Contains(secret, StringComparison.Ordinal));
        }
    }
}
