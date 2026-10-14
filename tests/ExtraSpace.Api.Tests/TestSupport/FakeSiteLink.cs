using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Xml.Linq;

namespace ExtraSpace.Api.Tests.TestSupport;

public sealed record SiteLinkRequest(string Operation, string Location, string SoapAction, IReadOnlyList<string> Names,
    IReadOnlyDictionary<string, string> Fields, string? ApiKey);

/// <summary>
/// Stands in for SiteLink for the whole test run. Replies are keyed by (operation, sLocationCode), so parallel tests that each use
/// their own location code never see each other. An unknown pair answers HTTP 500.
/// </summary>
public sealed class FakeSiteLink : HttpMessageHandler
{
    private readonly ConcurrentDictionary<(string Operation, string Location), Func<SiteLinkRequest, HttpResponseMessage>> _replies = new();
    private readonly ConcurrentQueue<SiteLinkRequest> _requests = new();

    public void Reply(string operation, string location, Func<SiteLinkRequest, HttpResponseMessage> reply) => _replies[(operation, location)] = reply;

    public void Reply(string operation, string location, string xml) => Reply(operation, location, _ => Ok(xml));

    public IReadOnlyList<SiteLinkRequest> RequestsFor(string location, string? operation = null) =>
        _requests.Where(r => r.Location == location && (operation is null || r.Operation == operation)).ToList();

    public static HttpResponseMessage Ok(string xml) => new(HttpStatusCode.OK) { Content = new StringContent(xml, Encoding.UTF8, "text/xml") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = XDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        var operation = body.Root!.Elements().Single().Elements().Single();   // Envelope / Body / <Operation>
        var names = operation.Elements().Select(e => e.Name.LocalName).ToList();
        var fields = operation.Elements().ToDictionary(e => e.Name.LocalName, e => e.Value);
        var parsed = new SiteLinkRequest(operation.Name.LocalName, fields.GetValueOrDefault("sLocationCode") ?? "",
            request.Headers.GetValues("SOAPAction").Single(), names, fields,
            request.Headers.TryGetValues("sApiKey", out var key) ? key.Single() : null);
        _requests.Enqueue(parsed);
        return _replies.TryGetValue((parsed.Operation, parsed.Location), out var reply)
            ? reply(parsed) : new HttpResponseMessage(HttpStatusCode.InternalServerError);
    }

    // Shared by the whole run: the client factory must never dispose it when a handler expires.
    protected override void Dispose(bool disposing) { }
}
