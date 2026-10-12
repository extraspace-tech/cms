using Serilog.Core;
using Serilog.Events;

namespace ExtraSpace.Api.Platform.Logging;

/// <summary>
/// Second line of defence (S0 spec §4.4): any property, or property of a logged object, whose name looks secret is replaced by ***.
/// Registered last, so it also sees properties added by the other enrichers and by log scopes.
/// Only captured values are inspected: <c>{@object}</c>, dictionaries and sequences. A plain <c>{object}</c> or <c>{$object}</c>
/// renders through ToString and is not masked, so never log an object that way.
/// </summary>
internal sealed class SensitiveDataMasker : ILogEventEnricher
{
    internal const string Mask = "***";
    private static readonly string[] Fragments = ["token", "secret", "password", "logincode", "authorization", "cookie", "connectionstring"];

    internal static bool IsSensitive(string name)
    {
        foreach (var fragment in Fragments)
            if (name.Contains(fragment, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var (name, value) in logEvent.Properties.ToArray())
        {
            var masked = IsSensitive(name) ? new ScalarValue(Mask) : MaskNested(value);
            if (!ReferenceEquals(masked, value)) logEvent.AddOrUpdateProperty(new LogEventProperty(name, masked));
        }
    }

    private static LogEventPropertyValue MaskNested(LogEventPropertyValue value)
    {
        switch (value)
        {
            case StructureValue structure:
            {
                var changed = false;
                var properties = new List<LogEventProperty>(structure.Properties.Count);
                foreach (var property in structure.Properties)
                {
                    var masked = IsSensitive(property.Name) ? new ScalarValue(Mask) : MaskNested(property.Value);
                    changed |= !ReferenceEquals(masked, property.Value);
                    properties.Add(new LogEventProperty(property.Name, masked));
                }
                return changed ? new StructureValue(properties, structure.TypeTag) : structure;
            }
            case DictionaryValue dictionary:
            {
                var changed = false;
                var elements = new List<KeyValuePair<ScalarValue, LogEventPropertyValue>>(dictionary.Elements.Count);
                foreach (var (key, element) in dictionary.Elements)
                {
                    var masked = key.Value?.ToString() is { } name && IsSensitive(name) ? new ScalarValue(Mask) : MaskNested(element);
                    changed |= !ReferenceEquals(masked, element);
                    elements.Add(new(key, masked));
                }
                return changed ? new DictionaryValue(elements) : dictionary;
            }
            case SequenceValue sequence:
            {
                var elements = sequence.Elements.Select(MaskNested).ToList();
                return elements.Where((e, i) => !ReferenceEquals(e, sequence.Elements[i])).Any() ? new SequenceValue(elements) : sequence;
            }
            default:
                return value;
        }
    }
}
