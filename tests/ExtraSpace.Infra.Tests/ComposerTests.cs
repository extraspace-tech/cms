using Amazon.CDK;
using Amazon.CDK.Assertions;
using Xunit;

namespace ExtraSpace.Infra.Tests;

public class ComposerTests
{
    [Fact]
    public void Compose_creates_exactly_the_four_named_stacks()
    {
        var names = InfraComposer.Compose(new App(), "123456789012").Keys.Order().ToArray();

        Assert.Equal(
            new[] { "EsData-production", "EsData-staging", "EsNetwork-production", "EsNetwork-staging" },
            names);
    }

    [Theory]
    [InlineData("EsNetwork-production", true)]
    [InlineData("EsData-production", true)]
    [InlineData("EsNetwork-staging", false)]
    [InlineData("EsData-staging", false)]
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
        InfraComposer.Compose(app, "123456789012");

        Assert.Empty(app.Synth().Manifest.Missing ?? []);
    }
}
