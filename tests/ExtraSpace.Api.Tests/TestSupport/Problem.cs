using System.Net.Http.Json;
using System.Text.Json;

namespace ExtraSpace.Api.Tests.TestSupport;

public static class Problem
{
    /// <summary>Asserts the S0 error shape (spec §4.1) and that the body traceId equals the X-Trace-Id header.</summary>
    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response, int status, string code)
    {
        ((int)response.StatusCode).Should().Be(status);
        response.Content.Headers.ContentType.Should().NotBeNull();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetInt32().Should().Be(status);
        body.GetProperty("code").GetString().Should().Be(code);
        body.GetProperty("traceId").GetString().Should().Be(TraceIdOf(response)).And.MatchRegex("^[0-9a-f]{32}$");
        return body;
    }

    public static string TraceIdOf(HttpResponseMessage response) => response.Headers.GetValues("X-Trace-Id").Single();
}
