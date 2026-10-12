using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExtraSpace.Api.Tests.TestSupport;

namespace ExtraSpace.Api.Tests.Platform;

public sealed class HttpPipelineTests(ApiFactory api)
{
    private readonly HttpClient _client = api.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Forwarded_headers_take_only_the_last_hop()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/ip");
        request.Headers.Add("X-Forwarded-For", "198.51.100.7, 203.0.113.9");
        request.Headers.Add("X-Forwarded-Proto", "https");
        var body = await (await _client.SendAsync(request, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("ip").GetString().Should().Be("203.0.113.9");
        body.GetProperty("https").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData("/test/ok")]
    [InlineData("/test/boom")]
    public async Task Security_headers_are_on_success_and_error_responses(string path)
    {
        var response = await _client.GetAsync(path, Ct);
        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
        response.Headers.GetValues("Referrer-Policy").Should().Equal("no-referrer");
        response.Headers.GetValues("Strict-Transport-Security").Should().Equal("max-age=31536000");
    }

    [Fact]
    public async Task Allowed_origin_gets_cors_headers_and_can_read_the_trace_id()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/ok");
        request.Headers.Add("Origin", "https://app.example.test");
        var response = await _client.SendAsync(request, Ct);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal("https://app.example.test");
        var exposed = response.Headers.GetValues("Access-Control-Expose-Headers").Single();
        exposed.Should().Contain("X-Trace-Id");
        exposed.Should().Contain("Retry-After");
    }

    [Fact]
    public async Task Other_origins_get_no_cors_headers()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/ok");
        request.Headers.Add("Origin", "https://evil.example.test");
        var response = await _client.SendAsync(request, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Rate_limited_request_gets_429_problem()
    {
        (await _client.GetAsync("/test/limited", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await _client.GetAsync("/test/limited", Ct);
        await Problem.ReadAsync(response, 429, "RATE_LIMITED");
        response.Headers.Contains("Retry-After").Should().BeTrue();
    }
}
