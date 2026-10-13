using System.Collections.Concurrent;
using System.Xml.Linq;
using Amazon.Runtime;
using Amazon.SimpleSystemsManagement;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ExtraSpace.Api.Tests.TestSupport;

/// <summary>An in-memory key ring for test hosts outside Development/Testing, which must have an SSM path to start.</summary>
public sealed class TestKeyRing : IXmlRepository
{
    private readonly ConcurrentQueue<XElement> _elements = new();

    public IReadOnlyCollection<XElement> GetAllElements() => _elements.ToArray();

    public void StoreElement(XElement element, string friendlyName) => _elements.Enqueue(element);
}

public static class TestKeyRingExtensions
{
    public static IWebHostBuilder UseTestKeyRing(this IWebHostBuilder builder) => builder
        .UseSetting("DataProtection:SsmPath", "/es/test/data-protection/")
        .ConfigureTestServices(services =>
        {
            // Never reaches AWS: no credential lookup, and a stray call fails fast on a closed local port.
            services.RemoveAll<IAmazonSimpleSystemsManagement>();
            services.AddSingleton<IAmazonSimpleSystemsManagement>(new AmazonSimpleSystemsManagementClient(
                new AnonymousAWSCredentials(), new AmazonSimpleSystemsManagementConfig { ServiceURL = "http://127.0.0.1:9" }));
            services.AddSingleton<TestKeyRing>();
            // The SSM key manager wraps XmlKeyManager, which reads the repository from these options.
            services.AddOptions<KeyManagementOptions>().PostConfigure<TestKeyRing>((o, ring) => o.XmlRepository = ring);
        });
}
