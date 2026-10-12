using ExtraSpace.Api.Tests.TestSupport;

namespace ExtraSpace.Api.Tests.Platform;

public sealed class TraceTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_response_has_a_new_32_hex_trace_id()
    {
        var client = api.CreateClient();
        var first = Problem.TraceIdOf(await client.GetAsync("/test/ok", Ct));
        var second = Problem.TraceIdOf(await client.GetAsync("/health/live", Ct));
        first.Should().MatchRegex("^[0-9a-f]{32}$");
        second.Should().MatchRegex("^[0-9a-f]{32}$").And.NotBe(first);
    }

    [Fact]
    public async Task Incoming_w3c_traceparent_is_honoured()
    {
        var client = api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/ok");
        request.Headers.Add("traceparent", "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01");
        Problem.TraceIdOf(await client.SendAsync(request, Ct)).Should().Be("0af7651916cd43dd8448eb211c80319c");
    }
}
