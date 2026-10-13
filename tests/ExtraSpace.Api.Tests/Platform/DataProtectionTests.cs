using Amazon;
using Amazon.Runtime;
using Amazon.SimpleSystemsManagement;
using ExtraSpace.Api.Platform.Hosting;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;

namespace ExtraSpace.Api.Tests.Platform;

public sealed class DataProtectionTests
{
    private static ServiceProvider Build(string environment, string? ssmPath)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=localhost;Database=test-not-used",
            ["DataProtection:SsmPath"] = ssmPath,
        }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        // Building the real client walks the AWS credential chain (instance metadata included); a pre-registered one wins over both TryAdds.
        services.AddSingleton<IAmazonSimpleSystemsManagement>(
            new AmazonSimpleSystemsManagementClient(new AnonymousAWSCredentials(), RegionEndpoint.APSoutheast1));
        services.AddPlatform(configuration, new HostingEnvironment { EnvironmentName = environment });
        return services.BuildServiceProvider();
    }

    private static string RepositoryType(ServiceProvider provider) =>
        provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository!.GetType().Name;

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void Outside_development_and_testing_the_app_needs_an_ssm_path(string environment)
    {
        var build = () => Build(environment, null);
        build.Should().Throw<InvalidOperationException>().WithMessage("*DataProtection:SsmPath*");
    }

    [Fact]
    public void With_an_ssm_path_the_keys_go_to_parameter_store()
    {
        using var provider = Build("Staging", "/es/test/data-protection/");
        RepositoryType(provider).Should().Be("SSMXmlRepository");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void In_development_and_testing_the_keys_stay_in_the_database(string environment)
    {
        using var provider = Build(environment, null);
        RepositoryType(provider).Should().StartWith("EntityFrameworkCoreXmlRepository");
    }
}
