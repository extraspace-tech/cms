using System.Collections.Concurrent;
using System.Text.Json;
using ExtraSpace.Api.Platform.Logging;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;

namespace ExtraSpace.Api.Tests.TestSupport;

/// <summary>Receives every log event and renders it with the production JSON formatter, so tests see the real lines.</summary>
public sealed class LogCapture : ILogEventSink
{
    private readonly ConcurrentQueue<string> _lines = new();
    private readonly ITextFormatter _formatter = LoggingSetup.CreateJsonFormatter();

    public void Emit(LogEvent logEvent)
    {
        using var writer = new StringWriter();
        _formatter.Format(logEvent, writer);
        _lines.Enqueue(writer.ToString());
    }

    public IReadOnlyList<string> RawLinesFor(string traceId) =>
        _lines.Where(line => line.Contains(traceId, StringComparison.Ordinal)).ToList();

    public IReadOnlyList<JsonElement> LinesFor(string traceId) =>
        RawLinesFor(traceId)
            .Select(line => JsonDocument.Parse(line).RootElement)
            .Where(e => e.TryGetProperty("traceId", out var t) && t.GetString() == traceId)
            .ToList();

    public IReadOnlyList<JsonElement> All() => _lines.Select(line => JsonDocument.Parse(line).RootElement).ToList();

    /// <summary>Waits until <paramref name="count"/> lines match (jobs log on a worker thread, after the request returned).</summary>
    public async Task<IReadOnlyList<JsonElement>> WaitForAsync(Func<JsonElement, bool> match, int count = 1, int timeoutSeconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (true)
        {
            var found = All().Where(match).ToList();
            if (found.Count >= count) return found;
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Expected {count} matching log lines, found {found.Count}.");
            await Task.Delay(100);
        }
    }
}
