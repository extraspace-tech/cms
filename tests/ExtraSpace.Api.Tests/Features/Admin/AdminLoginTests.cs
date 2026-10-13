using System.Net;
using ExtraSpace.Api.Features.Admin;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;

namespace ExtraSpace.Api.Tests.Features.Admin;

public sealed class AdminLoginTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Login_redirects_to_entra_with_pkce()
    {
        await using var host = api.WithWebHostBuilder(b => b.UseSetting("App:Role", "Api").UseTestEntra());
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

        var response = await client.GetAsync("/admin/api/auth/login", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();
        location.Should().StartWith("https://login.example.test/authorize");
        location.Should().Contain("client_id=test-client").And.Contain("response_type=code")
            .And.Contain("code_challenge_method=S256").And.Contain("redirect_uri=https%3A%2F%2Flocalhost%2Fadmin%2Fapi%2Fauth%2Fcallback");
    }

    [Fact]
    public async Task Front_channel_sign_out_is_off()
    {
        await using var host = api.WithWebHostBuilder(b => b.UseSetting("App:Role", "Api").UseTestEntra());
        var staff = await api.CreateStaffAsync((await api.CreateRoleAsync()).Id);
        var client = host.AdminClient();
        (await client.PostAsync($"/test/admin/sign-in/{staff.Id}", null, Ct)).EnsureSuccessStatusCode();

        (await client.GetAsync("/signout-oidc", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/admin/api/me", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_failed_callback_logs_our_warning_and_no_error()
    {
        await using var host = api.WithWebHostBuilder(b => b.UseSetting("App:Role", "Api").UseTestEntra());
        var client = host.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
        var login = await client.GetAsync("/admin/api/auth/login", Ct);
        var state = QueryHelpers.ParseQuery(login.Headers.Location!.Query)["state"].ToString();

        // A real state and correlation cookie take the handler to the code exchange, where the test backchannel throws:
        // the path on which the framework logs its own Error (EventId 17) with the whole exception.
        var response = await client.PostAsync(StaffAuth.CallbackPath,
            new FormUrlEncodedContent([new("code", "test-junk-code"), new("state", state)]), Ct);
        var junk = await client.PostAsync(StaffAuth.CallbackPath, new FormUrlEncodedContent([new("state", "junk")]), Ct);

        await Problem.ReadAsync(response, 401, "UNAUTHENTICATED");
        await Problem.ReadAsync(junk, 401, "UNAUTHENTICATED");
        foreach (var traceId in new[] { Problem.TraceIdOf(response), Problem.TraceIdOf(junk) })
        {
            var lines = api.Logs.LinesFor(traceId);
            lines.Should().ContainSingle(l => l.GetProperty("messageTemplate").GetString() == "Staff sign-in did not complete: {failureType}: {failureMessage}")
                .Which.GetProperty("level").GetString().Should().Be("Warning");
            lines.Should().NotContain(l => l.GetProperty("level").GetString() == "Error" || l.GetProperty("level").GetString() == "Fatal");
            lines.Should().NotContain(l => l.GetRawText().Contains("OpenIdConnectHandler"));
        }
    }

    [Fact]
    public void Production_jobs_role_starts_without_entra_settings()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:Role"] = "Jobs" }).Build();
        var services = new ServiceCollection().AddLogging()
            .AddStaffAuth(configuration, new HostingEnvironment { EnvironmentName = "Production" });

        using var provider = services.BuildServiceProvider();
        var schemes = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value.Schemes.Select(s => s.Name);
        schemes.Should().Contain(StaffAuth.CookieScheme).And.NotContain(StaffAuth.EntraScheme);
    }

    [Theory]
    [InlineData("Api")]
    [InlineData("All")]
    public void Production_serving_roles_refuse_to_start_without_entra_settings(string role)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:Role"] = role }).Build();
        var add = () => new ServiceCollection().AddLogging()
            .AddStaffAuth(configuration, new HostingEnvironment { EnvironmentName = "Production" });
        add.Should().Throw<InvalidOperationException>().WithMessage("*Admin:Entra*");
    }

    [Fact]
    public void Production_refuses_to_start_without_entra_settings()
    {
        var configuration = new ConfigurationBuilder().Build();
        var add = () => new ServiceCollection().AddLogging()
            .AddStaffAuth(configuration, new HostingEnvironment { EnvironmentName = "Production" });
        add.Should().Throw<InvalidOperationException>().WithMessage("*Admin:Entra*");
    }
}
