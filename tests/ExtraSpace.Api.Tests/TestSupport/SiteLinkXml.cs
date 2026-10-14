using System.Xml.Linq;

namespace ExtraSpace.Api.Tests.TestSupport;

/// <summary>Synthetic SiteLink replies in the DataSet/diffgram shape (demo spec 02 §4.4). Never real data: the repo is public.</summary>
public static class SiteLinkXml
{
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Service = "http://tempuri.org/CallCenterWs/CallCenterWs";
    private static readonly XNamespace Diffgram = "urn:schemas-microsoft-com:xml-diffgram-v1";
    private static readonly XNamespace MsData = "urn:schemas-microsoft-com:xml-msdata";

    /// <summary>A row; a null value leaves the column out, as a DataSet does for NULL.</summary>
    public static Dictionary<string, string?> Row(params (string Column, string? Value)[] columns) =>
        columns.ToDictionary(c => c.Column, c => c.Value);

    public static string Reply(string operation, params (string Table, Dictionary<string, string?> Row)[] rows)
    {
        var dataSet = new XElement("NewDataSet", rows.Select((row, i) => new XElement(row.Table,
            new XAttribute(Diffgram + "id", row.Table + (i + 1)), new XAttribute(MsData + "rowOrder", i),
            row.Row.Where(c => c.Value is not null).Select(c => new XElement(c.Key, c.Value)))));
        return new XDocument(new XElement(Soap + "Envelope", new XAttribute(XNamespace.Xmlns + "soap", Soap),
            new XElement(Soap + "Body",
                new XElement(Service + (operation + "Response"),
                    new XElement(Service + (operation + "Result"),
                        new XElement(XNamespace.Get("http://www.w3.org/2001/XMLSchema") + "schema", new XAttribute("id", "NewDataSet")),
                        new XElement(Diffgram + "diffgram",
                            new XAttribute(XNamespace.Xmlns + "diffgr", Diffgram), new XAttribute(XNamespace.Xmlns + "msdata", MsData),
                            dataSet)))))).ToString();
    }
}
