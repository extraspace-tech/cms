using ExtraSpace.Api.Platform.Data;
using ExtraSpace.Engines;

namespace ExtraSpace.Api.Tests.Architecture;

public sealed class ArchitectureTests
{
    private const string ApiRoot = "ExtraSpace.Api";
    private const string FixtureRoot = "ExtraSpace.Api.Tests.ArchFixtures";
    private const string Fixtures = FixtureRoot + ".Features.";
    private static readonly System.Reflection.Assembly Api = typeof(AppDbContext).Assembly;
    private static readonly System.Reflection.Assembly Tests = typeof(ArchitectureTests).Assembly;

    [Fact]
    public void The_api_follows_every_rule()
    {
        ArchitectureRules.CrossFeatureViolations(Api, ApiRoot).Should().BeEmpty();
        ArchitectureRules.LayerViolations(Api, ApiRoot).Should().BeEmpty();
        ArchitectureRules.EndpointDataAccess(Api, ApiRoot).Should().BeEmpty();
    }

    [Fact]
    public void Engines_reference_no_ef_aspnet_or_http() =>
        ArchitectureRules.ForbiddenEngineReferences(typeof(Money).Assembly).Should().BeEmpty();

    [Fact]
    public void Cross_feature_rule_catches_use_outside_contracts() =>
        ShouldBe(ArchitectureRules.CrossFeatureViolations(Tests, FixtureRoot), Fixtures + "Alpha.", "__AlphaBadFile",
            Fixtures + "Alpha.AlphaBad", Fixtures + "Alpha.AlphaBadAsync", Fixtures + "Alpha.AlphaBadSet", Fixtures + "Alphabet.AlphabetBad");

    [Fact]
    public void Layer_rule_catches_platform_using_a_feature() =>
        ShouldBe(ArchitectureRules.LayerViolations(Tests, FixtureRoot), FixtureRoot + ".Platform.", "__PlatformBadFile",
            FixtureRoot + ".Platform.PlatformBad", FixtureRoot + ".Platform.PlatformBadAsync", FixtureRoot + ".Integrations.IntegrationsBad");

    [Fact]
    public void Endpoint_rule_catches_a_db_context() =>
        ArchitectureRules.EndpointDataAccess(Tests, FixtureRoot).Should().BeEquivalentTo(
            Fixtures + "Beta.BetaEndpoints", Fixtures + "Beta.BetaAsyncEndpoints", Fixtures + "Beta.BetaHost", Fixtures + "Beta.BetaRoutes");

    [Fact]
    public void Engine_rule_catches_a_forbidden_reference() =>
        ArchitectureRules.ForbiddenEngineReferences(Api).Should().NotBeEmpty();

    // A C# `file` type compiles to a mangled name (`<Fixtures>F<hash>__Name`), so it is matched by namespace and suffix.
    private static void ShouldBe(IReadOnlyList<string> actual, string fileTypeNamespace, string fileTypeSuffix, params string[] stable)
    {
        actual.Where(n => !n.Contains('<')).Should().BeEquivalentTo(stable);
        actual.Should().ContainSingle(n => n.StartsWith(fileTypeNamespace + "<", StringComparison.Ordinal)
            && n.EndsWith(fileTypeSuffix, StringComparison.Ordinal));
        actual.Should().HaveCount(stable.Length + 1);
    }
}
