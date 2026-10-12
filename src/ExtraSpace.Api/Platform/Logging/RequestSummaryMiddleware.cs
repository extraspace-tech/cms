namespace ExtraSpace.Api.Platform.Logging;

/// <summary>One summary line per request (S0 spec §4.4). Health checks are logged only when they fail.</summary>
internal sealed partial class RequestSummaryMiddleware(RequestDelegate next, ILogger<RequestSummaryMiddleware> logger, TimeProvider time)
{
    public async Task InvokeAsync(HttpContext http)
    {
        var start = time.GetTimestamp();
        try
        {
            await next(http);
        }
        finally
        {
            var status = http.Response.StatusCode;
            if (status >= 500 || !http.Request.Path.StartsWithSegments("/health"))
            {
                // The exception handler clears the endpoint before it writes a 500, so the route saved after routing comes first.
                var route = http.Items[RouteLogContextMiddleware.RouteItemKey] as string
                    ?? (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
                    ?? "(unmatched)";
                var elapsedMs = (long)time.GetElapsedTime(start).TotalMilliseconds;
                Summary(logger, LevelFor(status), http.Request.Method, route, status, elapsedMs);
            }
        }
    }

    internal static LogLevel LevelFor(int status) => status switch
    {
        >= 500 => LogLevel.Error,
        403 or 429 => LogLevel.Warning,
        _ => LogLevel.Information,
    };

    [LoggerMessage(Message = "HTTP {method} {route} responded {status} in {elapsedMs} ms")]
    private static partial void Summary(ILogger logger, LogLevel level, string method, string route, int status, long elapsedMs);
}
