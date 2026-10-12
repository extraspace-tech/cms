using Serilog.Core;
using Serilog.Events;

namespace ExtraSpace.Api.Platform.Logging;

/// <summary>
/// Adds the route template and every <see cref="RequestLog"/> property to each line of a request, read when the line is written.
/// So lines written after the inner middleware has unwound (the exception handler, the summary) still carry them.
/// </summary>
internal sealed class RequestContextEnricher(IDictionary<object, object?> items) : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (items.TryGetValue(RouteLogContextMiddleware.RouteItemKey, out var route) && route is string text)
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("route", text));
        if (items.TryGetValue(RequestLog.ItemKey, out var bag) && bag is Dictionary<string, object> properties)
            foreach (var (name, value) in properties)
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(name, value));
    }
}
