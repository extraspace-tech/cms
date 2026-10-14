using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Telemetry;

namespace ExtraSpace.Api.Integrations.SiteLink;

internal static class SiteLinkSetup
{
    public static IServiceCollection AddSiteLink(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<SiteLinkOptions>().Bind(configuration.GetSection(SiteLinkOptions.Section))
            .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps,
                "SiteLink:BaseUrl must be an https URL.")
            .Validate(o => o.CallsPerSecond is > 0 and <= 1000, "SiteLink:CallsPerSecond must be between 1 and 1000.")
            .Validate(o => environment.IsProduction() || !o.AllowWritesToAllSites,
                "SiteLink:AllowWritesToAllSites is for production only; other environments write only to SiteLink:WriteAllowList.")
            .ValidateOnStart();

        services.AddSingleton<SiteLinkCallCounter>();
        services.AddSingleton(sp => new SiteLinkRateLimiter(sp.GetRequiredService<IOptions<SiteLinkOptions>>().Value.CallsPerSecond));
        services.AddTransient<SiteLinkAttemptHandler>();
        // The pipeline owns every timeout (S1 spec §6.3): 60 s total, 30 s per attempt, 2 jittered retries for reads, one breaker.
        var client = services.AddHttpClient<SiteLinkClient>(http => http.Timeout = Timeout.InfiniteTimeSpan);
        client.AddResilienceHandler("sitelink", (builder, context) =>
        {
            var settings = context.ServiceProvider.GetRequiredService<IOptions<SiteLinkOptions>>().Value;
            // Our call line is the one line per call (S0 spec §4.4); Polly's own events stay below Information, except an open breaker.
            builder.ConfigureTelemetry(new TelemetryOptions(context.ServiceProvider.GetRequiredService<IOptions<TelemetryOptions>>().Value)
            {
                SeverityProvider = args => args.Event.EventName switch
                {
                    "OnTimeout" or "OnRetry" or "ExecutionAttempt" => ResilienceEventSeverity.Debug,
                    "OnCircuitOpened" => ResilienceEventSeverity.Warning,
                    _ => args.Event.Severity,
                },
            });
            builder
                .AddTimeout(TimeSpan.FromSeconds(60))
                .AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 2,
                    Delay = settings.RetryDelay,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    // A write that timed out may have happened in SiteLink: never send it again (S1 spec §6.3).
                    ShouldHandle = args => ValueTask.FromResult(
                        args.Context.Properties.GetValue(SiteLinkClient.KindKey, SiteLinkCallKind.Write) == SiteLinkCallKind.Read
                        && HttpClientResiliencePredicates.IsTransient(args.Outcome)),
                })
                .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    MinimumThroughput = settings.BreakerMinimumThroughput,
                    SamplingDuration = TimeSpan.FromSeconds(60),
                    BreakDuration = TimeSpan.FromSeconds(30),
                })
                .AddTimeout(TimeSpan.FromSeconds(30));
        });
        client.AddHttpMessageHandler<SiteLinkAttemptHandler>();   // after the resilience handler = inside it = once per attempt
        return services;
    }
}
