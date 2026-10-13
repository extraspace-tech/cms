using Amazon.CDK;
using ExtraSpace.Infra.Stacks;

namespace ExtraSpace.Infra;

public static class InfraComposer
{
    public const string Region = "ap-southeast-1";

    // Fixed zones: no context lookup, so no cdk.context.json holding the account id.
    public static readonly string[] AvailabilityZones = ["ap-southeast-1a", "ap-southeast-1b"];

    public static IReadOnlyDictionary<string, Stack> Compose(App app, string? account, Func<string, DeployInputs?>? inputs = null)
    {
        Tags.Of(app).Add("Project", "extraspace-platform");
        Tags.Of(app).Add("ManagedBy", "cdk");

        var stacks = new Dictionary<string, Stack>();
        var env = new Amazon.CDK.Environment { Account = account, Region = Region };

        var ci = new CiStack(app, "EsCi", new StackProps
        {
            Env = env,
            TerminationProtection = true,
            Description = "ExtraSpace platform CI: GitHub OIDC, image repository, deploy roles",
        });
        stacks[ci.StackName] = ci;

        var deploys = EnvConfig.All.ToDictionary(config => config.Name, config => inputs?.Invoke(config.Name)?.Validated(config));
        RefuseSharedNames(deploys);

        foreach (var config in EnvConfig.All)
        {
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

            var deploy = deploys[config.Name];
            if (deploy is null) continue;

            var edge = new EdgeStack(app, $"EsEdge-{config.Name}", new EdgeStackProps
            {
                Env = env,
                Config = config,
                Inputs = deploy,
                TerminationProtection = config.IsProduction,
                Description = $"ExtraSpace platform certificate and email domain ({config.Name})",
            });
            Tags.Of(edge).Add("Environment", config.Name);
            stacks[edge.StackName] = edge;

            var appStack = new AppStack(app, $"EsApp-{config.Name}", new AppStackProps
            {
                Env = env,
                Config = config,
                Inputs = deploy,
                Vpc = network.Vpc,
                Data = data,
                Certificate = edge.Certificate,
                TerminationProtection = config.IsProduction,
                Description = $"ExtraSpace platform app: load balancer, firewall, ECS, logs, alarms ({config.Name})",
            });
            Tags.Of(appStack).Add("Environment", config.Name);
            stacks[appStack.StackName] = appStack;
        }

        return stacks;
    }

    // Each environment writes DNS records on its host, its email domain and mail.<email domain>, all in one shared zone.
    private static void RefuseSharedNames(Dictionary<string, DeployInputs?> deploys)
    {
        var present = deploys.Where(pair => pair.Value is not null).Select(pair => (Env: pair.Key, Inputs: pair.Value!)).ToArray();
        for (var i = 0; i < present.Length; i++)
        {
            for (var j = i + 1; j < present.Length; j++)
            {
                var (a, b) = (present[i], present[j]);
                if (Same(a.Inputs.Host, b.Inputs.Host)) Clash(a.Env, b.Env, "host", a.Inputs.Host);
                if (Same(a.Inputs.EmailDomain, b.Inputs.EmailDomain)) Clash(a.Env, b.Env, "emailFrom", a.Inputs.EmailDomain);
                RefuseMailFromName(a, b);
                RefuseMailFromName(b, a);
            }
        }
    }

    private static void RefuseMailFromName((string Env, DeployInputs Inputs) one, (string Env, DeployInputs Inputs) other)
    {
        var mailFrom = $"mail.{other.Inputs.EmailDomain}";
        if (Same(one.Inputs.Host, mailFrom)) Clash(one.Env, other.Env, "host", mailFrom);
        if (Same(one.Inputs.EmailDomain, mailFrom)) Clash(one.Env, other.Env, "emailFrom", mailFrom);
    }

    private static bool Same(string a, string b) => a.Trim().Equals(b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static void Clash(string env, string other, string key, string value) =>
        throw new InvalidOperationException($"Deploy inputs for {env} and {other}: {key} must not share the name {value}.");
}
