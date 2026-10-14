using System.Globalization;

namespace ExtraSpace.Api.Integrations.SiteLink;

internal sealed partial class SiteLinkClient
{
    /// <summary>
    /// <c>UnitsInformation_v3</c>: the units changed since <paramref name="sinceTicks"/> (null = from the beginning, sent as "0"), and
    /// SiteLink's next cursor (null when the reply has none). The ticks are round-tripped, never computed.
    /// </summary>
    public async Task<SiteLinkUnitsPage> UnitsInformationAsync(string locationCode, long? sinceTicks, bool includeExcludedFromWebsite,
        TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        const string operation = "UnitsInformation_v3";
        var reply = (await CallAsync(operation, locationCode, SiteLinkCallKind.Read, new SiteLinkFields()
                .Add("lngLastTimePolled", (sinceTicks ?? 0).ToString(CultureInfo.InvariantCulture))
                .Add("bReturnExcludedFromWebsiteUnits", includeExcludedFromWebsite), cancellationToken))
            .ThrowIfRefused(operation);
        var next = reply.Table("Table1").FirstOrDefault()?.Long("lngLastTimePolled");
        return new SiteLinkUnitsPage(reply.Table("Table").Select(row => SiteLinkUnit.From(row, zone)).ToList(), next is > 0 ? next : null);
    }

    /// <summary><c>UnitTypePriceList_v2</c>, read only for the site's currency decimals in S1-1 (spec §6.5); S1-3 reads the fees.</summary>
    public async Task<int?> CurrencyDecimalsAsync(string locationCode, CancellationToken cancellationToken)
    {
        const string operation = "UnitTypePriceList_v2";
        var reply = (await CallAsync(operation, locationCode, SiteLinkCallKind.Read, new SiteLinkFields(), cancellationToken))
            .ThrowIfRefused(operation);
        return reply.Table("Table").FirstOrDefault()?.Int("iCurrencyDecimals");
    }
}
