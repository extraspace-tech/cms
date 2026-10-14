using System.Threading.RateLimiting;
using Polly;

namespace ExtraSpace.Api.Integrations.SiteLink;

/// <summary>The per-instance read budget (S1 spec §6.6): a token bucket of <c>CallsPerSecond</c>.</summary>
internal sealed class SiteLinkRateLimiter(int callsPerSecond) : IDisposable
{
    public RateLimiter Limiter { get; } = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
    {
        TokenLimit = callsPerSecond,
        TokensPerPeriod = callsPerSecond,
        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        QueueLimit = 500,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        AutoReplenishment = true,
    });

    public void Dispose() => Limiter.Dispose();
}

/// <summary>Runs once per HTTP attempt, inside the retry: reads wait for the rate limit, writes never wait, and every attempt is counted.</summary>
internal sealed class SiteLinkAttemptHandler(SiteLinkRateLimiter limiter) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var context = request.GetResilienceContext();
        if (context?.Properties.GetValue(SiteLinkClient.KindKey, SiteLinkCallKind.Read) != SiteLinkCallKind.Write)
        {
            using var lease = await limiter.Limiter.AcquireAsync(1, cancellationToken);
            if (!lease.IsAcquired) throw new SiteLinkUnavailableException("(queued call)", "the rate-limit queue is full");
            return await SendCountedAsync(request, context, cancellationToken);
        }
        return await SendCountedAsync(request, context, cancellationToken);
    }

    private Task<HttpResponseMessage> SendCountedAsync(HttpRequestMessage request, ResilienceContext? context, CancellationToken cancellationToken)
    {
        context?.Properties.Set(SiteLinkClient.AttemptsKey, context.Properties.GetValue(SiteLinkClient.AttemptsKey, 0) + 1);
        return base.SendAsync(request, cancellationToken);
    }
}
