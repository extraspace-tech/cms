using ExtraSpace.Api.Platform.Errors;

namespace ExtraSpace.Api.Integrations.SiteLink;

/// <summary>SiteLink answered with a refusal. What a code means depends on the operation (API PDF), so callers map their own codes first.</summary>
internal sealed class SiteLinkException(string operation, int returnCode, string? siteLinkMessage)
    : Exception($"SiteLink {operation} refused the call with code {returnCode}."), IProblemException
{
    public string Operation { get; } = operation;
    public int ReturnCode { get; } = returnCode;
    /// <summary>SiteLink's own text: for staff screens and outbox errors, never for the app or the log.</summary>
    public string? SiteLinkMessage { get; } = siteLinkMessage;
    public int Status => 502;
    public string Code => "SITELINK_ERROR";
    public string Title => "The storage system refused the request. Try again later or contact the facility.";
}

/// <summary>SiteLink could not be reached, timed out, failed with HTTP 5xx, the breaker is open, or we have no login.</summary>
internal sealed class SiteLinkUnavailableException(string operation, string reason, Exception? inner = null)
    : Exception($"SiteLink {operation} is unavailable: {reason}.", inner), IProblemException
{
    public int Status => 503;
    public string Code => "SITELINK_UNAVAILABLE";
    public string Title => "The storage system cannot be reached right now. Try again in a few minutes.";
}
