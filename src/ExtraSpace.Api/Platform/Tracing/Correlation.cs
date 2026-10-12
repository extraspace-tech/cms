using Serilog.Context;

namespace ExtraSpace.Api.Platform.Tracing;

/// <summary>
/// The traceId of the request that started a chain (S0 spec §4.4). A request's own traceId is its correlationId;
/// S0-3's Hangfire filters carry it into every job the request enqueues.
/// </summary>
public static class Correlation
{
    private static readonly AsyncLocal<string?> Current = new();

    public static string? CurrentId => Current.Value;

    public static IDisposable Begin(string correlationId)
    {
        var previous = Current.Value;
        Current.Value = correlationId;
        return new Scope(previous, LogContext.PushProperty("correlationId", correlationId));
    }

    private sealed class Scope(string? previous, IDisposable logScope) : IDisposable
    {
        public void Dispose()
        {
            logScope.Dispose();
            Current.Value = previous;
        }
    }
}
