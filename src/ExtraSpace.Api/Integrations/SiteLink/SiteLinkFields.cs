using System.Collections;
using System.Globalization;

namespace ExtraSpace.Api.Integrations.SiteLink;

/// <summary>Request elements in WSDL sequence order (the order they are added). Values are written in the invariant culture.</summary>
internal sealed class SiteLinkFields : IEnumerable<KeyValuePair<string, string>>
{
    private readonly List<KeyValuePair<string, string>> _items = [];

    public SiteLinkFields Add(string name, string? value)
    {
        _items.Add(new(name, value ?? ""));
        return this;
    }

    public SiteLinkFields Add(string name, int value) => Add(name, value.ToString(CultureInfo.InvariantCulture));

    public SiteLinkFields Add(string name, decimal value) => Add(name, value.ToString(CultureInfo.InvariantCulture));

    public SiteLinkFields Add(string name, bool value) => Add(name, value ? "true" : "false");

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
