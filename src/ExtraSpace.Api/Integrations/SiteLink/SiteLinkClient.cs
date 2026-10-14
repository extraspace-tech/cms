using System.Text;
using System.Xml;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace ExtraSpace.Api.Integrations.SiteLink;

internal enum SiteLinkCallKind { Read, Write }

/// <summary>SiteLink CCWs 3.5 over raw SOAP (S1 spec §6.1). One method per operation lives in the partial files.</summary>
internal sealed partial class SiteLinkClient(
    HttpClient http, IOptionsMonitor<SiteLinkOptions> options, SiteLinkCallCounter counter, TimeProvider time, ILogger<SiteLinkClient> logger)
{
    internal static readonly ResiliencePropertyKey<SiteLinkCallKind> KindKey = new("sitelink.kind");
    internal static readonly ResiliencePropertyKey<int> AttemptsKey = new("sitelink.attempts");

    /// <summary>
    /// One call. The logon block goes first, then <paramref name="fields"/> in their order (the WSDL sequence). A write to a site
    /// outside <c>SiteLink:WriteAllowList</c> throws before anything is sent. Reads are retried; writes never are.
    /// </summary>
    internal async Task<SiteLinkReply> CallAsync(string operation, string locationCode, SiteLinkCallKind kind, SiteLinkFields fields,
        CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        if (kind == SiteLinkCallKind.Write && !settings.MayWriteTo(locationCode))
            throw new InvalidOperationException($"SiteLink write {operation} to {locationCode} is blocked: the site is not in SiteLink:WriteAllowList.");
        if (!settings.IsConfigured)
        {
            LogNotConfigured(logger, operation);
            throw new SiteLinkUnavailableException(operation, "SiteLink is not configured");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.BaseUrl)
        {
            Content = new StringContent(SoapEnvelope.Build(operation, Logon(settings, locationCode).Concat(fields)), Encoding.UTF8, "text/xml"),
        };
        request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{SoapEnvelope.Namespace}/{operation}\"");
        if (!string.IsNullOrEmpty(settings.ApiKey)) request.Headers.TryAddWithoutValidation("sApiKey", settings.ApiKey);
        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        context.Properties.Set(KindKey, kind);
        request.SetResilienceContext(context);

        var started = time.GetTimestamp();
        var outcome = "unavailable";
        int? retCode = null;
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) throw new SiteLinkUnavailableException(operation, $"HTTP {(int)response.StatusCode}");
            var reply = Parse(operation, await response.Content.ReadAsStringAsync(cancellationToken));
            retCode = reply.RetCode;
            outcome = reply.IsRefused ? "refused" : "ok";
            return reply;
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutRejectedException or BrokenCircuitException
                                          || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new SiteLinkUnavailableException(operation, exception.GetType().Name, exception);
        }
        finally
        {
            var attempts = context.Properties.GetValue(AttemptsKey, 0);
            ResilienceContextPool.Shared.Return(context);
            var elapsedMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
            // Lower-case values, as SES logs them: the outside-call alarm counts every outcome that is not "ok".
            if (outcome == "ok") LogCall(logger, "sitelink", operation, locationCode, outcome, elapsedMs, retCode, attempts);
            else LogCallFailed(logger, "sitelink", operation, locationCode, outcome, elapsedMs, retCode, attempts);
            await counter.CountAsync(operation, attempts);
        }
    }

    // No inner exception: an XmlException message can quote the body.
    private static SiteLinkReply Parse(string operation, string body)
    {
        try
        {
            return SiteLinkReply.Parse(body);
        }
        catch (XmlException)
        {
            throw new SiteLinkUnavailableException(operation, "unreadable reply");
        }
    }

    private static KeyValuePair<string, string>[] Logon(SiteLinkOptions settings, string locationCode) =>
    [
        new("sCorpCode", settings.CorpCode!),
        new("sLocationCode", locationCode),
        new("sCorpUserName", settings.CorpUserName!),
        new("sCorpPassword", settings.CorpPassword!),
    ];

    // Same template as every outside call (S0 spec §4.4), plus the site and SiteLink's code. Never a body or the logon block.
    [LoggerMessage(Level = LogLevel.Information, Message = "Outside call {integration} {operation} for {siteCode}: {outcome} in {elapsedMs} ms (retCode {retCode}, attempts {attempt})")]
    private static partial void LogCall(ILogger logger, string integration, string operation, string siteCode, string outcome, long elapsedMs, int? retCode, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outside call {integration} {operation} for {siteCode}: {outcome} in {elapsedMs} ms (retCode {retCode}, attempts {attempt})")]
    private static partial void LogCallFailed(ILogger logger, string integration, string operation, string siteCode, string outcome, long elapsedMs, int? retCode, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SiteLink {operation} not sent: SiteLink:CorpCode, CorpUserName or CorpPassword is not set, or CorpUserName is not <user>:::<licence key>")]
    private static partial void LogNotConfigured(ILogger logger, string operation);
}
