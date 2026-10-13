using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace ExtraSpace.Api.Tests.Platform;

public sealed class HealthTests(ApiFactory api)
{
    [Fact]
    public async Task Live_and_ready_are_200_when_the_database_answers()
    {
        var client = api.CreateClient();
        (await client.GetAsync("/health/live", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        var ready = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        ready.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ready.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("Healthy");
    }

    [Fact]
    public async Task Ready_is_503_and_live_stays_200_when_the_database_is_down()
    {
        await using var broken = api.WithWebHostBuilder(b =>
            b.UseSetting("ConnectionStrings:Default", "Host=127.0.0.1;Port=1;Database=x;Username=x;Timeout=2").UseSetting("App:Role", "Api"));
        var client = broken.CreateClient();
        (await client.GetAsync("/health/live", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
