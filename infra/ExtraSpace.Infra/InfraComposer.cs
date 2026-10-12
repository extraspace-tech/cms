using Amazon.CDK;
using ExtraSpace.Infra.Stacks;

namespace ExtraSpace.Infra;

public static class InfraComposer
{
    public const string Region = "ap-southeast-1";

    // Fixed zones: no context lookup, so no cdk.context.json holding the account id.
    public static readonly string[] AvailabilityZones = ["ap-southeast-1a", "ap-southeast-1b"];

    public static IReadOnlyDictionary<string, Stack> Compose(App app, string? account)
    {
        Tags.Of(app).Add("Project", "extraspace-platform");
        Tags.Of(app).Add("ManagedBy", "cdk");

        var stacks = new Dictionary<string, Stack>();
        foreach (var config in EnvConfig.All)
        {
            var env = new Amazon.CDK.Environment { Account = account, Region = Region };

            var network = new NetworkStack(app, $"EsNetwork-{config.Name}", new NetworkStackProps
            {
                Env = env,
                Config = config,
                TerminationProtection = config.IsProduction,
                Description = $"ExtraSpace platform network ({config.Name})",
            });

            var data = new DataStack(app, $"EsData-{config.Name}", new DataStackProps
            {
                Env = env,
                Config = config,
                Vpc = network.Vpc,
                TerminationProtection = config.IsProduction,
                Description = $"ExtraSpace platform database and bastion ({config.Name})",
            });

            foreach (var stack in new Stack[] { network, data })
            {
                Tags.Of(stack).Add("Environment", config.Name);
                stacks[stack.StackName] = stack;
            }
        }

        return stacks;
    }
}
