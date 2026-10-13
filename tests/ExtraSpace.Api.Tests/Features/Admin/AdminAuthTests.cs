using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExtraSpace.Api.Features.Admin;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;

namespace ExtraSpace.Api.Tests.Features.Admin;

public sealed class AdminAuthTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<HttpResponseMessage> LogoutAsync(HttpClient client) => client.PostAsync("/admin/api/auth/logout", null, Ct);

    [Fact]
    public async Task Admin_routes_without_a_cookie_get_401_not_a_redirect()
    {
        var response = await api.AdminClient().GetAsync("/admin/api/me", Ct);
        await Problem.ReadAsync(response, 401, "UNAUTHENTICATED");
        response.Headers.Location.Should().BeNull();
    }

    [Fact]
    public async Task Me_returns_the_staff_member_and_permissions()
    {
        // A stored permission that is no longer in the catalogue is ignored.
        var (client, staff) = await api.StaffClientAsync(AdminPermissions.StaffManage, AdminPermissions.AuditRead, "test.retired");

        var body = await client.GetFromJsonAsync<JsonElement>("/admin/api/me", Ct);
        body.GetProperty("id").GetGuid().Should().Be(staff.Id);
        body.GetProperty("email").GetString().Should().Be(staff.Email);
        body.GetProperty("roleId").GetGuid().Should().Be(staff.RoleId);
        body.GetProperty("roleName").GetString().Should().StartWith("role-");
        body.GetProperty("permissions").EnumerateArray().Select(p => p.GetString())
            .Should().Equal(AdminPermissions.AuditRead, AdminPermissions.StaffManage);
    }

    [Fact]
    public async Task A_route_without_the_permission_gets_403()
    {
        var (without, _) = await api.StaffClientAsync(AdminPermissions.StaffManage);
        await Problem.ReadAsync(await without.GetAsync("/test/admin/needs-audit", Ct), 403, "FORBIDDEN");

        var (with, _) = await api.StaffClientAsync(AdminPermissions.AuditRead);
        (await with.GetAsync("/test/admin/needs-audit", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_deactivated_staff_member_is_signed_out_at_the_next_request()
    {
        var (client, staff) = await api.StaffClientAsync();
        (await client.GetAsync("/admin/api/me", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        await api.WithDbAsync(async db =>
        {
            (await db.Set<StaffUser>().SingleAsync(s => s.Id == staff.Id)).Active = false;
            await db.SaveChangesAsync();
        });

        // /csrf has no null check of its own: only the cookie validation can refuse it.
        await Problem.ReadAsync(await client.GetAsync("/admin/api/auth/csrf", Ct), 401, "UNAUTHENTICATED");
    }

    [Fact]
    public async Task A_deactivated_staff_member_loses_permission_routes_at_the_next_request()
    {
        var (client, staff) = await api.StaffClientAsync(AdminPermissions.AuditRead);
        (await client.GetAsync("/test/admin/needs-audit", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        await api.WithDbAsync(async db =>
        {
            (await db.Set<StaffUser>().SingleAsync(s => s.Id == staff.Id)).Active = false;
            await db.SaveChangesAsync();
        });

        await Problem.ReadAsync(await client.GetAsync("/test/admin/needs-audit", Ct), 401, "UNAUTHENTICATED");
    }

    [Fact]
    public async Task A_role_change_applies_at_the_next_request()
    {
        var (client, staff) = await api.StaffClientAsync(AdminPermissions.AuditRead);
        (await client.GetAsync("/test/admin/needs-audit", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        await api.WithDbAsync(async db =>
        {
            (await db.Set<Role>().SingleAsync(r => r.Id == staff.RoleId)).Permissions = [];
            await db.SaveChangesAsync();
        });

        await Problem.ReadAsync(await client.GetAsync("/test/admin/needs-audit", Ct), 403, "FORBIDDEN");
    }

    [Fact]
    public async Task The_cookie_is_host_prefixed_http_only_secure_and_lax()
    {
        var staff = await api.CreateStaffAsync((await api.CreateRoleAsync()).Id);
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false });

        var response = await client.PostAsync($"/test/admin/sign-in/{staff.Id}", null, Ct);

        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-es-admin=", StringComparison.Ordinal));
        var attributes = cookie.Split(';').Skip(1).Select(a => a.Trim().ToLowerInvariant()).ToList();
        attributes.Should().Contain(["path=/", "secure", "httponly", "samesite=lax"]);
        attributes.Should().NotContain(a => a.StartsWith("domain", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_write_without_the_csrf_header_is_rejected()
    {
        var staff = await api.CreateStaffAsync((await api.CreateRoleAsync()).Id);
        var client = await api.SignedInAdminAsync(staff, withCsrf: false);

        await Problem.ReadAsync(await LogoutAsync(client), 400, "CSRF_FAILED");

        await client.AddCsrfAsync();
        (await LogoutAsync(client)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await Problem.ReadAsync(await client.GetAsync("/admin/api/me", Ct), 401, "UNAUTHENTICATED");
    }

    [Fact]
    public async Task A_csrf_token_of_another_staff_member_is_rejected()
    {
        var role = await api.CreateRoleAsync();
        var a = await api.CreateStaffAsync(role.Id);
        var b = await api.CreateStaffAsync(role.Id);
        var aClient = await api.SignedInAdminAsync(a);
        var aToken = aClient.DefaultRequestHeaders.GetValues(StaffAuth.CsrfHeader).Single();

        // B's own cookies with A's token.
        var bClient = await api.SignedInAdminAsync(b);
        bClient.DefaultRequestHeaders.Remove(StaffAuth.CsrfHeader);
        bClient.DefaultRequestHeaders.Add(StaffAuth.CsrfHeader, aToken);
        await Problem.ReadAsync(await LogoutAsync(bClient), 400, "CSRF_FAILED");

        // Same browser (same CSRF cookie), now signed in as B: the token is bound to A's staff id.
        (await aClient.PostAsync($"/test/admin/sign-in/{b.Id}", null, Ct)).EnsureSuccessStatusCode();
        await Problem.ReadAsync(await LogoutAsync(aClient), 400, "CSRF_FAILED");
    }

    [Fact]
    public async Task A_csrf_token_survives_a_permission_change()
    {
        var (client, staff) = await api.StaffClientAsync(AdminPermissions.AuditRead);
        await api.WithDbAsync(async db =>
        {
            (await db.Set<Role>().SingleAsync(r => r.Id == staff.RoleId)).Permissions = [AdminPermissions.StaffManage];
            await db.SaveChangesAsync();
        });

        (await LogoutAsync(client)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Requests_log_the_staff_id()
    {
        var (client, staff) = await api.StaffClientAsync();
        var response = await client.GetAsync("/admin/api/me", Ct);

        var summary = api.Logs.LinesFor(Problem.TraceIdOf(response))
            .Single(l => l.GetProperty("messageTemplate").GetString() == "HTTP {method} {route} responded {status} in {elapsedMs} ms");
        summary.GetProperty("staffId").GetString().Should().Be(staff.Id.ToString());
    }

    [Fact]
    public async Task A_refused_request_logs_the_staff_id()
    {
        var (client, staff) = await api.StaffClientAsync(AdminPermissions.AuditRead);
        var response = await client.GetAsync("/admin/api/staff", Ct);

        await Problem.ReadAsync(response, 403, "FORBIDDEN");
        var summary = api.Logs.LinesFor(Problem.TraceIdOf(response))
            .Single(l => l.GetProperty("messageTemplate").GetString() == "HTTP {method} {route} responded {status} in {elapsedMs} ms");
        summary.GetProperty("status").GetInt32().Should().Be(403);
        summary.GetProperty("staffId").GetString().Should().Be(staff.Id.ToString());
    }

    [Fact]
    public async Task A_customer_token_does_not_open_admin_routes()
    {
        var customer = await api.CreateCustomerAsync();
        foreach (var path in new[] { "/admin/api/me", "/admin/api/auth/csrf" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", api.AccessTokenFor(customer));
            await Problem.ReadAsync(await api.AdminClient().SendAsync(request, Ct), 401, "UNAUTHENTICATED");
        }
    }

    [Fact]
    public async Task The_admin_cookie_does_not_open_customer_routes()
    {
        var (client, _) = await api.StaffClientAsync(AdminPermissions.StaffManage);
        await Problem.ReadAsync(await client.GetAsync("/api/v1/me", Ct), 401, "UNAUTHENTICATED");
    }

    [Fact]
    public async Task Admin_responses_never_allow_credentials_cross_origin()
    {
        var (client, _) = await api.StaffClientAsync();
        using var get = new HttpRequestMessage(HttpMethod.Get, "/admin/api/me");
        get.Headers.Add("Origin", "https://app.example.test");
        var response = await client.SendAsync(get, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();

        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/admin/api/auth/logout");
        preflight.Headers.Add("Origin", "https://app.example.test");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", StaffAuth.CsrfHeader);
        (await client.SendAsync(preflight, Ct)).Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
    }

    [Fact]
    public async Task The_redirect_login_route_is_gone() =>
        await Problem.ReadAsync(await api.AdminClient().GetAsync("/admin/api/auth/login", Ct), 404, "NOT_FOUND");

    [Fact]
    public async Task Development_cookies_work_over_plain_http()
    {
        await using var development = api.WithWebHostBuilder(b => b.UseEnvironment("Development").UseSetting("App:Role", "Api")
            .UseSetting("Seq:Url", "").UseSetting("Email:Transport", "Log")
            // Development loads the developer's user secrets: pin the test database above every other source.
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:Default"] = api.ConnectionString })));
        development.Services.GetRequiredService<IConfiguration>().GetConnectionString("Default").Should().Be(api.ConnectionString);
        var staff = await api.CreateStaffAsync((await api.CreateRoleAsync()).Id);
        var client = development.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost"), HandleCookies = true });

        var signIn = await client.PostAsync($"/test/admin/sign-in/{staff.Id}", null, Ct);
        signIn.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var csrf = await client.GetAsync("/admin/api/auth/csrf", Ct);
        csrf.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookies = signIn.Headers.GetValues("Set-Cookie").Concat(csrf.Headers.GetValues("Set-Cookie")).ToList();
        cookies.Should().Contain(c => c.StartsWith(StaffAuth.DevCookieName + "=", StringComparison.Ordinal))
            .And.Contain(c => c.StartsWith(StaffAuth.DevCsrfCookieName + "=", StringComparison.Ordinal))
            .And.NotContain(c => c.StartsWith("__Host-", StringComparison.Ordinal));

        var token = (await csrf.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetString();
        client.DefaultRequestHeaders.Add(StaffAuth.CsrfHeader, token);
        (await client.PostAsync("/admin/api/auth/logout", null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public void Production_needs_no_sign_in_settings()
    {
        var services = new ServiceCollection().AddLogging().AddStaffAuth(new HostingEnvironment { EnvironmentName = "Production" });

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value.Schemes.Select(s => s.Name).Should().Equal(StaffAuth.CookieScheme);
    }

    [Fact]
    public async Task The_customer_bearer_lock_stays_off_admin_routes()
    {
        var document = await api.CreateClient().GetFromJsonAsync<JsonElement>("/openapi/v1.json", Ct);
        var me = document.GetProperty("paths").GetProperty("/admin/api/me").GetProperty("get");
        me.TryGetProperty("security", out _).Should().BeFalse();
    }
}
