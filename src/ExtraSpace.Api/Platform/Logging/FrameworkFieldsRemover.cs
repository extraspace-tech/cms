using Serilog.Core;
using Serilog.Events;

namespace ExtraSpace.Api.Platform.Logging;

/// <summary>Drops ASP.NET Core's scope fields: the raw path is client-controlled, and the route template replaces it (S0 spec §4.4).</summary>
internal sealed class FrameworkFieldsRemover : ILogEventEnricher
{
    private static readonly string[] Fields = ["RequestPath", "RequestId", "ActionId", "ActionName"];

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var field in Fields) logEvent.RemovePropertyIfPresent(field);
    }
}
