using Amazon.CDK;
using Amazon.CDK.AWS.ECR;
using Amazon.CDK.AWS.IAM;
using Constructs;

namespace ExtraSpace.Infra.Stacks;

/// <summary>What GitHub Actions needs, once for the account: the OIDC provider, the image repository and one deploy role per environment.</summary>
public sealed class CiStack : Stack
{
    // GitHub puts the numeric ids into the token's subject. A move to another account or repository changes all four.
    private const string GitHubOwner = "extraspace-tech";
    private const string GitHubOwnerId = "119834034";
    private const string GitHubRepoName = "cms";
    private const string GitHubRepoId = "1410108364";
    public const string GitHubRepo = $"{GitHubOwner}/{GitHubRepoName}";
    private const string GitHubSubjectRepo = $"{GitHubOwner}@{GitHubOwnerId}/{GitHubRepoName}@{GitHubRepoId}";
    public const string RepositoryName = "extraspace/api";
    private const string GitHubIssuer = "token.actions.githubusercontent.com";

    public static string ImageTagParameter(string env) => $"/es/{env}/app/image-tag";
    public static string ClusterName(string env) => $"es-{env}";
    public static string TaskRoleName(string env) => $"es-{env}-task";
    public static string ExecutionRoleName(string env) => $"es-{env}-task-execution";
    public static string DeployRoleName(string env) => $"es-ci-{env}";

    public CiStack(Construct scope, string id, IStackProps props) : base(scope, id, props)
    {
        // The L1 resource: the L2 construct deploys a custom-resource Lambda.
        var provider = new CfnOIDCProvider(this, "GitHub", new CfnOIDCProviderProps
        {
            Url = $"https://{GitHubIssuer}",
            ClientIdList = new[] { "sts.amazonaws.com" },
        });

        var repository = new Repository(this, "Images", new RepositoryProps
        {
            RepositoryName = RepositoryName,
            ImageTagMutability = TagMutability.IMMUTABLE,
            ImageScanOnPush = true,
            RemovalPolicy = RemovalPolicy.RETAIN,
            LifecycleRules = new ILifecycleRule[]
            {
                // Rule 1 claims production images first, so the count rule below can never expire one that is running.
                new LifecycleRule { RulePriority = 1, Description = "keep 20 production images", TagPrefixList = new[] { "prod-" }, MaxImageCount = 20 },
                new LifecycleRule { RulePriority = 2, Description = "keep 30 other images", TagStatus = TagStatus.ANY, MaxImageCount = 30 },
            },
        });

        foreach (var config in EnvConfig.All) DeployRole(provider, repository, config);
    }

    private void DeployRole(CfnOIDCProvider provider, Repository repository, EnvConfig config)
    {
        var env = config.Name;
        var role = new Role(this, $"DeployRole{env}", new RoleProps
        {
            RoleName = DeployRoleName(env),
            Description = $"GitHub Actions deploys of {GitHubRepo}, GitHub environment {env}",
            MaxSessionDuration = Duration.Hours(1),
            AssumedBy = new WebIdentityPrincipal(provider.AttrArn, new Dictionary<string, object>
            {
                ["StringEquals"] = new Dictionary<string, object>
                {
                    [$"{GitHubIssuer}:aud"] = "sts.amazonaws.com",
                    [$"{GitHubIssuer}:sub"] = $"repo:{GitHubSubjectRepo}:environment:{env}",
                },
            }),
        });

        if (config.IsProduction)
        {
            // Production never builds: it promotes an image staging already ran, and adds the prod-<sha> tag to it.
            repository.GrantPull(role);
            Allow(role, new[] { "ecr:PutImage", "ecr:DescribeImages" }, repository.RepositoryArn);
            Allow(role, new[] { "ssm:GetParameter" }, Parameter(EnvConfig.Staging.Name));
        }
        else
        {
            repository.GrantPullPush(role);
            Allow(role, new[] { "ecr:DescribeImages" }, repository.RepositoryArn);
        }

        Allow(role, new[] { "ssm:GetParameter", "ssm:PutParameter" }, Parameter(env));
        Allow(role, new[] { "ecs:DescribeServices", "ecs:UpdateService" },
            FormatArn(new ArnComponents { Service = "ecs", Resource = "service", ResourceName = $"{ClusterName(env)}/*" }));
        // These three have no resource-level permissions. What limits a new task definition is iam:PassRole below.
        Allow(role, new[] { "ecs:ListServices", "ecs:DescribeTaskDefinition", "ecs:RegisterTaskDefinition" }, "*");
        role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = new[] { "iam:PassRole" },
            Resources = new[]
            {
                FormatArn(new ArnComponents { Service = "iam", Region = "", Resource = "role", ResourceName = TaskRoleName(env) }),
                FormatArn(new ArnComponents { Service = "iam", Region = "", Resource = "role", ResourceName = ExecutionRoleName(env) }),
            },
            Conditions = new Dictionary<string, object>
            {
                ["StringEquals"] = new Dictionary<string, object> { ["iam:PassedToService"] = "ecs-tasks.amazonaws.com" },
            },
        }));
    }

    private string Parameter(string env) =>
        FormatArn(new ArnComponents { Service = "ssm", Resource = "parameter", ResourceName = ImageTagParameter(env).TrimStart('/') });

    private static void Allow(Role role, string[] actions, string resource) =>
        role.AddToPolicy(new PolicyStatement(new PolicyStatementProps { Actions = actions, Resources = new[] { resource } }));
}
