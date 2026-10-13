using System.Net;
using ExtraSpace.Api.Platform.Http;
using ExtraSpace.Api.Tests.TestSupport;

namespace ExtraSpace.Api.Tests.Platform;

public sealed class AppVersionTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("1.4.0", "1.4.0")]
    [InlineData("1.4", "1.4.0")]
    [InlineData("1.4.0+12", "1.4.0")]
    [InlineData("1.4.0-beta.2", "1.4.0")]
    [InlineData(" 2.10.3 ", "2.10.3")]
    public void Parse_reads_semver_like_values(string raw, string expected) =>
        AppVersion.Parse(raw).Should().Be(Version.Parse(expected));

    [Theory]
    [InlineData("garbage")]
    [InlineData("1")]
    [InlineData("")]
    [InlineData(null)]
    public void Parse_returns_null_when_it_cannot(string? raw) => AppVersion.Parse(raw).Should().BeNull();

    private async Task<HttpResponseMessage> Send(string? platform, string? version)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/ok");
        if (platform is not null) request.Headers.Add("X-App-Platform", platform);
        if (version is not null) request.Headers.Add("X-App-Version", version);
        return await api.CreateClient().SendAsync(request, Ct);
    }

    [Theory]
    [InlineData("ios", "1.9.9")]
    [InlineData("iOS", "1.0.0")]
    [InlineData("android", "1.9.9+99")]
    public async Task Below_the_minimum_is_426(string platform, string version) =>
        await Problem.ReadAsync(await Send(platform, version), 426, "APP_UPDATE_REQUIRED");

    [Theory]
    [InlineData("ios", "2.0.0")]
    [InlineData("ios", "2.0.0+15")]
    [InlineData("android", "3.1.0")]
    [InlineData("web", "0.0.1")]
    [InlineData("ios", "garbage")]
    [InlineData("ios", null)]
    [InlineData(null, "0.0.1")]
    [InlineData(null, null)]
    public async Task Everything_else_passes(string? platform, string? version) =>
        (await Send(platform, version)).StatusCode.Should().Be(HttpStatusCode.OK);

    [Fact]
    public async Task A_minimum_that_is_not_a_version_stops_the_app_at_start()
    {
        await using var misconfigured = api.WithWebHostBuilder(b => b.UseSetting("App:MinVersion:web", "garbage").UseSetting("App:Role", "Api"));
        var start = () => misconfigured.CreateClient();
        start.Should().Throw<Exception>()
            .Which.ToString().Should().Contain("App:MinVersion values must be versions like 1.4.0");
    }
}
