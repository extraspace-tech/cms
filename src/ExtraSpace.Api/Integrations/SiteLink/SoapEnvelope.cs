using System.Xml.Linq;

namespace ExtraSpace.Api.Integrations.SiteLink;

/// <summary>SOAP 1.1, document/literal wrapped (demo spec 02 §4.2).</summary>
internal static class SoapEnvelope
{
    public const string Namespace = "http://tempuri.org/CallCenterWs/CallCenterWs";
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";

    public static string Build(string operation, IEnumerable<KeyValuePair<string, string>> elements)
    {
        XNamespace service = Namespace;
        return new XElement(Soap + "Envelope", new XAttribute(XNamespace.Xmlns + "soap", Soap),
                new XElement(Soap + "Body",
                    new XElement(service + operation, elements.Select(e => new XElement(service + e.Key, e.Value)))))
            .ToString(SaveOptions.DisableFormatting);
    }
}
