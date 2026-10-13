using Amazon.CDK;
using Amazon.CDK.Assertions;
using Xunit;

namespace ExtraSpace.Infra.Tests;

public class ComposerTests
{
    [Fact]
    public void Without_inputs_compose_creates_the_network_data_and_ci_stacks()
    {
        var names = InfraComposer.Compose(new App(), "123456789012").Keys.Order().ToArray();

        Assert.Equal(
            new[] { "EsCi", "EsData-production", "EsData-staging", "EsNetwork-production", "EsNetwork-staging" },
            names);
    }

    [Fact]
    public void Inputs_for_one_environment_add_only_its_stacks()
    {
        var names = InfraComposer.Compose(new App(), "123456789012", env => env == "staging" ? TestInputs.For(env) : null).Keys.Order().ToArray();

        Assert.Contains("EsEdge-staging", names);
        Assert.DoesNotContain("EsEdge-production", names);
        Assert.Contains("EsApp-staging", names);
        Assert.DoesNotContain("EsApp-production", names);
    }

    [Fact]
    public void Bad_inputs_stop_the_synth()
    {
        Assert.Throws<InvalidOperationException>(() =>
            InfraComposer.Compose(new App(), "123456789012", env => TestInputs.For(env) with { AdminEmailDomains = [] }));
    }

    [Fact]
    public void The_same_inputs_for_both_environments_stop_the_synth()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            InfraComposer.Compose(new App(), "123456789012", _ => TestInputs.For("staging")));

        Assert.Contains("staging and production", error.Message);
        Assert.Contains("host must not share", error.Message);
    }

    [Fact]
    public void Production_on_stagings_host_stops_the_synth()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            InfraComposer.Compose(new App(), "123456789012", env => env == "production"
                ? TestInputs.For(env) with { Host = TestInputs.For("staging").Host }
                : TestInputs.For(env)));

        Assert.Contains("staging and production", error.Message);
        Assert.Contains("host must not share", error.Message);
    }

    [Fact]
    public void Production_on_stagings_email_domain_stops_the_synth()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            InfraComposer.Compose(new App(), "123456789012", env => env == "production"
                ? TestInputs.For(env) with { EmailFrom = TestInputs.For("staging").EmailFrom }
                : TestInputs.For(env)));

        Assert.Contains("staging and production", error.Message);
        Assert.Contains("emailFrom must not share", error.Message);
    }

    [Fact]
    public void The_normal_inputs_compose_both_environments()
    {
        var names = InfraComposer.Compose(new App(), "123456789012", TestInputs.For).Keys;

        Assert.Contains("EsApp-staging", names);
        Assert.Contains("EsApp-production", names);
    }

    [Theory]
    [InlineData("EsNetwork-production", true)]
    [InlineData("EsData-production", true)]
    [InlineData("EsNetwork-staging", false)]
    [InlineData("EsData-staging", false)]
    [InlineData("EsCi", true)]
    [InlineData("EsEdge-production", true)]
    [InlineData("EsEdge-staging", false)]
    [InlineData("EsApp-production", true)]
    [InlineData("EsApp-staging", false)]
    public void Production_stacks_have_termination_protection(string stackName, bool expected)
    {
        Assert.Equal(expected, TestStacks.Stack(stackName).TerminationProtection);
    }

    [Fact]
    public void Compose_works_without_an_account()
    {
        var stacks = InfraComposer.Compose(new App(), account: null);

        var template = Template.FromStack(stacks["EsNetwork-staging"]);

        template.ResourceCountIs("AWS::EC2::VPC", 1);
    }

    [Fact]
    public void Compose_needs_no_context_lookup()
    {
        // A lookup would make `cdk synth` call AWS and write the account id into cdk.context.json.
        var app = new App();
        InfraComposer.Compose(app, "123456789012", TestInputs.For);

        Assert.Empty(app.Synth().Manifest.Missing ?? []);
    }
}
