using System.Diagnostics;
using System.Net;
using ExtraSpace.Api.Integrations.SiteLink;
using Polly;

namespace ExtraSpace.Api.Tests.Integrations;

public sealed class SiteLinkAttemptHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Ok : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    private static (HttpRequestMessage Request, ResilienceContext Context) Request(SiteLinkCallKind kind)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://sitelink.example.test/");
        var context = ResilienceContextPool.Shared.Get();
        context.Properties.Set(SiteLinkClient.KindKey, kind);
        request.SetResilienceContext(context);
        return (request, context);
    }

    [Fact]
    public async Task Reads_wait_for_the_limit_and_writes_do_not()
    {
        using var limiter = new SiteLinkRateLimiter(2);
        using var invoker = new HttpMessageInvoker(new SiteLinkAttemptHandler(limiter) { InnerHandler = new Ok() });

        var clock = Stopwatch.StartNew();
        for (var i = 0; i < 5; i++) await invoker.SendAsync(Request(SiteLinkCallKind.Write).Request, Ct);
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500));

        clock.Restart();
        for (var i = 0; i < 5; i++) await invoker.SendAsync(Request(SiteLinkCallKind.Read).Request, Ct);
        clock.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(900));   // 2 at once, 2 after 1 s, 1 after 2 s
    }

    [Fact]
    public async Task Each_attempt_is_counted_on_the_context()
    {
        using var limiter = new SiteLinkRateLimiter(100);
        using var invoker = new HttpMessageInvoker(new SiteLinkAttemptHandler(limiter) { InnerHandler = new Ok() });
        var (request, context) = Request(SiteLinkCallKind.Read);

        await invoker.SendAsync(request, Ct);
        await invoker.SendAsync(request, Ct);

        context.Properties.GetValue(SiteLinkClient.AttemptsKey, 0).Should().Be(2);
    }
}
