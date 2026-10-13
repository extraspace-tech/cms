using System.Net;
using ExtraSpace.Api.Platform.Http;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace ExtraSpace.Api.Tests.Platform;

public sealed class AdminSpaTests : IDisposable
{
    private const string Page = "<!doctype html><title>admin-ui-test</title>";
    private readonly ApiFactory _api;
    private readonly string _root = Directory.CreateTempSubdirectory("es-admin-ui-").FullName;
    private readonly PhysicalFileProvider _files;
    private readonly WebApplicationFactory<Program> _host;
    private readonly HttpClient _client;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public AdminSpaTests(ApiFactory api)
    {
        _api = api;
        Directory.CreateDirectory(Path.Combine(_root, "admin", "assets"));
        File.WriteAllText(Path.Combine(_root, "admin", "index.html"), Page);
        File.WriteAllText(Path.Combine(_root, "admin", "assets", "app-abc123.js"), "console.log('admin-ui-test')");
        _files = new PhysicalFileProvider(_root);
        // The real web root is git-ignored build output, so the tests bring their own.
        _host = api.WithWebHostBuilder(b => b.UseSetting("App:Role", "Api").ConfigureTestServices(services =>
            services.Configure<StaticFileOptions>(o => o.FileProvider = _files)));
        _client = _host.CreateClient();
    }

    public void Dispose()
    {
        _host.Dispose();
        _files.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/")]
    [InlineData("/admin/staff")]
    [InlineData("/admin/roles/0199dd00-0000-7000-8000-000000000001")]
    [InlineData("/admin/index.html")]
    public async Task Page_addresses_get_the_page(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Accept", "text/html");
        var response = await _client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await response.Content.ReadAsStringAsync(Ct)).Should().Be(Page);
        response.Headers.CacheControl!.ToString().Should().Be("no-cache");
        response.Headers.GetValues("Content-Security-Policy").Should().Equal(AdminSpa.Csp);
        response.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
    }

    [Fact]
    public async Task Head_of_a_page_address_has_no_body()
    {
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/admin/staff"), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentLength.Should().Be(Page.Length);
        (await response.Content.ReadAsStringAsync(Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Hashed_assets_are_cached_for_a_year()
    {
        var response = await _client.GetAsync("/admin/assets/app-abc123.js", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/javascript");
        response.Headers.CacheControl!.ToString().Should().Be("public, max-age=31536000, immutable");
        response.Headers.GetValues("Content-Security-Policy").Should().Equal(AdminSpa.Csp);
    }

    [Fact]
    public async Task A_missing_asset_is_404_not_the_page()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/assets/app-old999.js");
        request.Headers.Add("Accept", "*/*");
        await Problem.ReadAsync(await _client.SendAsync(request, Ct), 404, "NOT_FOUND");
    }

    [Theory]
    [InlineData("GET", "/admin/api/nope", 404, "NOT_FOUND")]
    [InlineData("GET", "/admin/api/staff/extra/segments", 404, "NOT_FOUND")]
    [InlineData("GET", "/admin/api/me", 401, "UNAUTHENTICATED")]
    [InlineData("GET", "/admin/jobs", 401, "UNAUTHENTICATED")]
    [InlineData("GET", "/admin/jobs/recurring", 401, "UNAUTHENTICATED")]
    [InlineData("POST", "/admin/staff", 405, "HTTP_405")]
    [InlineData("DELETE", "/admin", 405, "HTTP_405")]
    [InlineData("PUT", "/admin/api/me", 405, "HTTP_405")]
    [InlineData("GET", "/administrator", 404, "NOT_FOUND")]
    public async Task Api_and_jobs_paths_never_get_the_page(string method, string path, int status, string code)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("Accept", "text/html");
        await Problem.ReadAsync(await _host.AdminClient().SendAsync(request, Ct), status, code);
    }

    [Theory]
    [InlineData("/admin/api/me")]
    [InlineData("/admin/jobs")]
    [InlineData("/test/ok")]
    [InlineData("/health/live")]
    public async Task Only_admin_pages_get_the_csp(string path)
    {
        var response = await _client.GetAsync(path, Ct);
        response.Headers.Contains("Content-Security-Policy").Should().BeFalse();
    }

    [Fact]
    public async Task Without_a_built_ui_the_page_address_is_404()
    {
        // The shared host has no wwwroot/admin: a fresh clone before `npm run build`.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/staff");
        request.Headers.Add("Accept", "text/html");
        await Problem.ReadAsync(await _api.CreateClient().SendAsync(request, Ct), 404, "NOT_FOUND");
    }

    [Fact]
    public async Task Production_serves_the_page_too()
    {
        await using var production = _api.WithWebHostBuilder(b => b.UseEnvironment("Production").UseSetting("App:Role", "Api")
            .UseSetting("Email:Transport", "Ses").UseSetting("Email:From", "ExtraSpace <no-reply@example.test>").UseTestKeyRing()
            .ConfigureTestServices(services => services.Configure<StaticFileOptions>(o => o.FileProvider = _files)));
        var client = production.CreateClient();
        var response = await client.GetAsync("/admin/staff", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Be(Page);
        // The asset comes from static files, not the fallback: this fails if UseStaticFiles is non-production only.
        (await client.GetAsync("/admin/assets/app-abc123.js", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
