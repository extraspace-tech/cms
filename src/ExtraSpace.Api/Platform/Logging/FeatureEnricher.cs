using Serilog.Core;
using Serilog.Events;

namespace ExtraSpace.Api.Platform.Logging;

/// <summary>Adds <c>feature</c> (for example <c>Identity</c>) from a logger in <c>ExtraSpace.Api.Features.&lt;Feature&gt;</c>.</summary>
internal sealed class FeatureEnricher : ILogEventEnricher
{
    private const string Prefix = "ExtraSpace.Api.Features.";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (!logEvent.Properties.TryGetValue("SourceContext", out var value) || value is not ScalarValue { Value: string source }
            || !source.StartsWith(Prefix, StringComparison.Ordinal)) return;
        var rest = source.AsSpan(Prefix.Length);
        var dot = rest.IndexOf('.');
        logEvent.AddPropertyIfAbsent(new LogEventProperty("feature", new ScalarValue((dot < 0 ? rest : rest[..dot]).ToString())));
    }
}
