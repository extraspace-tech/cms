using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExtraSpace.Api.Features.Admin;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExtraSpace.Api.Tests.Features.Admin;

public sealed class DevLoginTests(ApiFactory api) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> _development = null!;

    public ValueTask InitializeAsync()
    {
        _development = api.WithWebHostBuilder(b => b.UseEnvironment("Development").UseSetting("App:Role", "Api")
            .UseSetting("Seq:Url", "").UseSetting("Email:Transport", "Log")
            // Development loads the developer's user secrets: pin the test database above every other source.
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:Default"] = api.ConnectionString }))
            .ConfigureTestServices(s => s.AddTransient<IStartupFilter, TestRemoteIp>()));
        _development.Services.GetRequiredService<IConfiguration>().GetConnectionString("Default").Should().Be(api.ConnectionString);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _development.DisposeAsync();

    private HttpClient DevClient(string ip, string baseAddress = "https://localhost")
    {
        var client = _development.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(baseAddress), HandleCookies = true });
        client.DefaultRequestHeaders.Add(TestRemoteIp.Header, ip);
        return client;
    }

    /// <summary>Sets the connection's own address before UseForwardedHeaders, as a direct caller would have it (TestServer has none).</summary>
    private sealed class TestRemoteIp : IStartupFilter
    {
        public const string Header = "X-Test-Remote-Ip";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((http, nextMiddleware) =>
            {
                if (http.Request.Headers.TryGetValue(Header, out var ip))
                {
                    http.Connection.RemoteIpAddress = IPAddress.Parse(ip.ToString());
                    http.Request.Headers.Remove(Header);
                }
                return nextMiddleware(http);
            });
            next(app);
        };
    }

    private static Task<HttpResponseMessage> DevLoginAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/admin/api/auth/dev-login", new { email }, Ct);

    [Fact]
    public async Task Dev_login_does_not_exist_outside_development() =>
        (await DevLoginAsync(api.AdminClient(), IdentityTestData.NewEmail("s"))).StatusCode.Should().Be(HttpStatusCode.NotFound);

    [Fact]
    public async Task Dev_login_signs_in_a_staff_member_from_loopback()
    {
        var staff = await api.CreateStaffAsync((await api.CreateRoleAsync()).Id);
        var client = DevClient("127.0.0.1");

        (await DevLoginAsync(client, staff.Email)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync("/admin/api/me", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Dev_login_with_a_leftover_cookie_is_not_an_audited_write()
    {
        var staff = await api.CreateStaffAsync((await api.CreateRoleAsync()).Id);
        var client = DevClient("127.0.0.1");
        (await DevLoginAsync(client, staff.Email)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Signed in, no CSRF header: the anonymous route skips the CSRF check and the audit context.
        (await DevLoginAsync(client, staff.Email)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await api.AuditRowsForAsync(staff.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Development_cookies_work_over_plain_http()
    {
        var staff = await api.CreateStaffAsync((await api.CreateRoleAsync()).Id);
        var client = DevClient("127.0.0.1", "http://localhost");

        var login = await DevLoginAsync(client, staff.Email);
        login.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var csrf = await client.GetAsync("/admin/api/auth/csrf", Ct);
        csrf.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookies = login.Headers.GetValues("Set-Cookie").Concat(csrf.Headers.GetValues("Set-Cookie")).ToList();
        cookies.Should().Contain(c => c.StartsWith(StaffAuth.DevCookieName + "=", StringComparison.Ordinal))
            .And.Contain(c => c.StartsWith(StaffAuth.DevCsrfCookieName + "=", StringComparison.Ordinal))
            .And.NotContain(c => c.StartsWith("__Host-", StringComparison.Ordinal));

        var token = (await csrf.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetString();
        client.DefaultRequestHeaders.Add(StaffAuth.CsrfHeader, token);
        (await client.PostAsync("/admin/api/auth/logout", null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Dev_login_refuses_a_remote_caller()
    {
        var staff = await api.CreateStaffAsync((await api.CreateRoleAsync()).Id);
        (await DevLoginAsync(DevClient("10.9.9.9"), staff.Email)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dev_login_refuses_a_forwarded_loopback_address()
    {
        var staff = await api.CreateStaffAsync((await api.CreateRoleAsync()).Id);
        foreach (var connection in new[] { "127.0.0.1", "10.9.9.9" })
        {
            var client = DevClient(connection);
            client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.1");
            (await DevLoginAsync(client, staff.Email)).StatusCode.Should().Be(HttpStatusCode.NotFound, connection);
        }
    }

    [Fact]
    public async Task Dev_login_refuses_an_unknown_email() =>
        await Problem.ReadAsync(await DevLoginAsync(DevClient("127.0.0.1"), IdentityTestData.NewEmail("s")), 403, "FORBIDDEN");
}
