using System.Diagnostics;

namespace ExtraSpace.Api.Platform.Tracing;

public static class RequestTrace
{
    public const string HeaderName = "X-Trace-Id";
    private const string ItemKey = "es.traceId";

    /// <summary>The 32-hex W3C trace id of this request: the same value in the header, the error body and every log line.</summary>
    public static string TraceId(HttpContext http) =>
        http.Items[ItemKey] as string ?? Activity.Current?.TraceId.ToHexString() ?? http.TraceIdentifier;

    internal static void Set(HttpContext http, string traceId) => http.Items[ItemKey] = traceId;
}
