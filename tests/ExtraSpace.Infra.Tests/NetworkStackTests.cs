using Amazon.CDK.Assertions;
using Xunit;

namespace ExtraSpace.Infra.Tests;

public class NetworkStackTests
{
    [Theory]
    [InlineData("staging", 1, "10.10.0.0/16")]
    [InlineData("production", 2, "10.20.0.0/16")]
    public void Vpc_has_the_environment_cidr_and_nat_count(string env, int natGateways, string cidr)
    {
        var template = TestStacks.For($"EsNetwork-{env}");

        template.HasResourceProperties("AWS::EC2::VPC", new Dictionary<string, object> { ["CidrBlock"] = cidr });
        template.ResourceCountIs("AWS::EC2::NatGateway", natGateways);
        template.ResourceCountIs("AWS::EC2::EIP", natGateways);
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void Vpc_spans_two_zones_with_public_app_and_data_subnets(string env)
    {
        var template = TestStacks.For($"EsNetwork-{env}");

        template.ResourceCountIs("AWS::EC2::Subnet", 6);
        foreach (var zone in InfraComposer.AvailabilityZones)
        {
            template.ResourcePropertiesCountIs(
                "AWS::EC2::Subnet",
                new Dictionary<string, object> { ["AvailabilityZone"] = zone },
                3);
        }
        template.ResourcePropertiesCountIs(
            "AWS::EC2::Subnet",
            new Dictionary<string, object> { ["MapPublicIpOnLaunch"] = true },
            2);
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void Vpc_has_a_free_s3_gateway_endpoint(string env)
    {
        TestStacks.For($"EsNetwork-{env}").HasResourceProperties(
            "AWS::EC2::VPCEndpoint",
            new Dictionary<string, object> { ["VpcEndpointType"] = "Gateway" });
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void Vpc_carries_the_project_tags(string env)
    {
        var template = TestStacks.For($"EsNetwork-{env}");

        foreach (var (key, value) in new[]
                 {
                     ("Environment", env),
                     ("ManagedBy", "cdk"),
                     ("Project", "extraspace-platform"),
                 })
        {
            template.HasResourceProperties("AWS::EC2::VPC", new Dictionary<string, object>
            {
                ["Tags"] = Match.ArrayWith(new object[]
                {
                    new Dictionary<string, object> { ["Key"] = key, ["Value"] = value },
                }),
            });
        }
    }
}
