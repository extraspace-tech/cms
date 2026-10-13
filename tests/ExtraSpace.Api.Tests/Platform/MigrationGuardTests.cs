using System.Net;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Hosting;

namespace ExtraSpace.Api.Tests.Platform;

public sealed class MigrationGuardTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IWebHostBuilder Staging(IWebHostBuilder b, string connectionString) => b
        .UseEnvironment("Staging").UseTestKeyRing().UseSetting("App:Role", "Api")
        .UseSetting("Email:Transport", "Ses").UseSetting("Email:From", "ExtraSpace <no-reply@example.test>")
        .UseSetting("ConnectionStrings:Default", connectionString);

    [Fact]
    public async Task A_build_ahead_of_the_database_refuses_to_start()
    {
        var empty = await api.CreateDatabaseAsync("guard");
        await using var host = api.WithWebHostBuilder(b => Staging(b, empty));

        var start = () => host.CreateClient();

        start.Should().Throw<Exception>().Where(e => e.ToString().Contains("missing") && e.ToString().Contains("migration"));
    }

    [Fact]
    public async Task A_migrated_database_starts()
    {
        await using var host = api.WithWebHostBuilder(b => Staging(b, api.ConnectionString));

        (await host.CreateClient().GetAsync("/health/ready", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_switch_turns_the_check_off()
    {
        var empty = await api.CreateDatabaseAsync("guard_off");
        await using var host = api.WithWebHostBuilder(b => Staging(b, empty).UseSetting("Database:RequireMigrated", "false"));

        (await host.CreateClient().GetAsync("/health/live", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Testing_does_not_check()
    {
        var empty = await api.CreateDatabaseAsync("guard_testing");
        await using var host = api.WithWebHostBuilder(b => b.UseSetting("App:Role", "Api").UseSetting("ConnectionStrings:Default", empty));

        (await host.CreateClient().GetAsync("/health/live", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
