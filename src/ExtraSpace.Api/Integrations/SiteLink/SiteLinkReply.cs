using System.Globalization;
using System.Xml.Linq;

namespace ExtraSpace.Api.Integrations.SiteLink;

/// <summary>A DataSet reply: tables of rows of string columns. A NULL column is absent from its row (demo spec 02 §4.4).</summary>
internal sealed class SiteLinkReply(Dictionary<string, List<SiteLinkRow>> tables)
{
    public IReadOnlyList<SiteLinkRow> Table(string name) => tables.TryGetValue(name, out var rows) ? rows : [];

    /// <summary>The first RT row's Ret_Code (some operations spell it Ret_Cod); null without an RT table or when it is not a number.</summary>
    public int? RetCode =>
        Table("RT").FirstOrDefault() is { } rt && (rt.Text("Ret_Code") ?? rt.Text("Ret_Cod")) is { } text
        && int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) ? code : null;

    public string? RetMessage => Table("RT").FirstOrDefault()?.Text("Ret_Msg");

    /// <summary>An RT Ret_Code of 0 or below is a refusal; an operation with codes of its own maps them before calling this.</summary>
    public bool IsRefused => RetCode <= 0;

    /// <summary>A refusal becomes <see cref="SiteLinkException"/>.</summary>
    public SiteLinkReply ThrowIfRefused(string operation) =>
        IsRefused ? throw new SiteLinkException(operation, RetCode!.Value, RetMessage) : this;

    public static SiteLinkReply Parse(string xml)
    {
        var dataSet = XDocument.Parse(xml).Descendants().FirstOrDefault(e => e.Name.LocalName == "NewDataSet");
        var tables = new Dictionary<string, List<SiteLinkRow>>(StringComparer.Ordinal);
        foreach (var row in dataSet?.Elements() ?? [])
        {
            if (!tables.TryGetValue(row.Name.LocalName, out var rows)) tables[row.Name.LocalName] = rows = [];
            rows.Add(new SiteLinkRow(row.Elements()
                .GroupBy(column => column.Name.LocalName, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal)));
        }
        return new SiteLinkReply(tables);
    }
}

internal sealed class SiteLinkRow(IReadOnlyDictionary<string, string> columns)
{
    public bool Has(string column) => columns.ContainsKey(column);

    public string? Text(string column) => columns.GetValueOrDefault(column);

    public int? Int(string column) => Number(column) is { } s ? SiteLinkValues.ParseInt(s, column) : null;

    public long? Long(string column) => Number(column) is { } s ? SiteLinkValues.ParseLong(s, column) : null;

    public decimal? Decimal(string column) => Number(column) is { } s ? SiteLinkValues.ParseDecimal(s, column) : null;

    public bool? Bool(string column) => Number(column) is { } s ? SiteLinkValues.ParseBool(s, column) : null;

    public DateTime? UtcDate(string column, TimeZoneInfo zone) => SiteLinkValues.ParseDate(Text(column), zone, column);

    // A blank number or flag counts as absent.
    private string? Number(string column) => Text(column) is { } s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;
}
