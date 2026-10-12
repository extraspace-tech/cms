using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Constructs;

namespace ExtraSpace.Infra.Stacks;

public sealed class NetworkStackProps : StackProps
{
    public required EnvConfig Config { get; init; }
}

public sealed class NetworkStack : Stack
{
    public IVpc Vpc { get; }

    // The Vpc checks its zones against the stack's; without this override that is an AWS context lookup.
    public override string[] AvailabilityZones => InfraComposer.AvailabilityZones;

    public NetworkStack(Construct scope, string id, NetworkStackProps props) : base(scope, id, props)
    {
        Vpc = new Amazon.CDK.AWS.EC2.Vpc(this, "Vpc", new VpcProps
        {
            VpcName = $"es-{props.Config.Name}",
            IpAddresses = IpAddresses.Cidr(props.Config.VpcCidr),
            AvailabilityZones = InfraComposer.AvailabilityZones,
            NatGateways = props.Config.NatGateways,
            // Nothing uses the default security group; leaving it alone avoids a custom-resource Lambda.
            RestrictDefaultSecurityGroup = false,
            SubnetConfiguration = new ISubnetConfiguration[]
            {
                new SubnetConfiguration { Name = "public", SubnetType = SubnetType.PUBLIC, CidrMask = 24 },
                new SubnetConfiguration { Name = "app", SubnetType = SubnetType.PRIVATE_WITH_EGRESS, CidrMask = 22 },
                new SubnetConfiguration { Name = "data", SubnetType = SubnetType.PRIVATE_ISOLATED, CidrMask = 24 },
            },
            GatewayEndpoints = new Dictionary<string, IGatewayVpcEndpointOptions>
            {
                ["S3"] = new GatewayVpcEndpointOptions { Service = GatewayVpcEndpointAwsService.S3 },
            },
        });
    }
}
