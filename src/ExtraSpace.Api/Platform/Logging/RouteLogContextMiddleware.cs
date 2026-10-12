namespace ExtraSpace.Api.Platform.Logging;

/// <summary>Runs after routing: saves the route template (never the raw path) for <see cref="RequestContextEnricher"/> and the summary line.</summary>
internal sealed class RouteLogContextMiddleware(RequestDelegate next)
{
    internal const string RouteItemKey = "es.route";

    public Task InvokeAsync(HttpContext http)
    {
        if ((http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText is { } route) http.Items[RouteItemKey] = route;
        return next(http);
    }
}
