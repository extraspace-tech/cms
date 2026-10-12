using System.Diagnostics;
using ExtraSpace.Api.Platform.Logging;
using Microsoft.Extensions.Primitives;
using Serilog.Context;
using Serilog.Core;
using Serilog.Core.Enrichers;

namespace ExtraSpace.Api.Platform.Tracing;

/// <summary>Step 2 of the pipeline (S0 spec §4.2): sets X-Trace-Id and the request's log context.</summary>
public sealed class RequestTraceMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http)
    {
        var traceId = Activity.Current?.TraceId.ToHexString() ?? ActivityTraceId.CreateRandom().ToHexString();
        RequestTrace.Set(http, traceId);
        http.Response.OnStarting(() =>
        {
            http.Response.Headers[RequestTrace.HeaderName] = traceId;
            return Task.CompletedTask;
        });

        var properties = new List<ILogEventEnricher>
        {
            new PropertyEnricher("method", http.Request.Method),
            new RequestContextEnricher(http.Items),
        };
        AddHeader(properties, "albTraceId", http.Request.Headers["X-Amzn-Trace-Id"], 128);
        AddHeader(properties, "clientPlatform", http.Request.Headers["X-App-Platform"], 16);
        AddHeader(properties, "clientVersion", http.Request.Headers["X-App-Version"], 32);

        using (Correlation.Begin(traceId))
        using (LogContext.Push([.. properties]))
        {
            await next(http);
        }
    }

    // Client-supplied values: clipped so a header cannot bloat every log line.
    private static void AddHeader(List<ILogEventEnricher> properties, string name, StringValues value, int maxLength)
    {
        var text = value.ToString();
        if (text.Length > 0) properties.Add(new PropertyEnricher(name, text.Length <= maxLength ? text : text[..maxLength]));
    }
}
