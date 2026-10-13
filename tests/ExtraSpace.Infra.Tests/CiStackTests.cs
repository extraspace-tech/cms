using System.Text.Json;
using System.Text.Json.Nodes;
using Amazon.CDK.Assertions;
using Xunit;

namespace ExtraSpace.Infra.Tests;

public class CiStackTests
{
    private static Template Ci => TestStacks.For("EsCi");

    [Fact]
    public void GitHub_is_the_only_identity_provider_and_no_lambda_is_needed()
    {
        Ci.HasResourceProperties("AWS::IAM::OIDCProvider", new Dictionary<string, object>
        {
            ["Url"] = "https://token.actions.githubusercontent.com",
            ["ClientIdList"] = new object[] { "sts.amazonaws.com" },
        });
        Ci.ResourceCountIs("AWS::IAM::OIDCProvider", 1);
        Ci.ResourceCountIs("AWS::Lambda::Function", 0);
    }

    [Fact]
    public void Repository_has_immutable_tags_is_scanned_and_is_kept()
    {
        Ci.HasResource("AWS::ECR::Repository", new Dictionary<string, object>
        {
            ["DeletionPolicy"] = "Retain",
            ["Properties"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["RepositoryName"] = "extraspace/api",
                ["ImageTagMutability"] = "IMMUTABLE",
                ["ImageScanningConfiguration"] = new Dictionary<string, object> { ["ScanOnPush"] = true },
            }),
        });
    }

    [Fact]
    public void Production_images_are_kept_apart()
    {
        // Rule 1 claims every prod-* image first, so rule 2 (30 of the rest) can never expire a production image.
        var policy = Ci.FindResources("AWS::ECR::Repository").Values.Single();
        // The relaxed encoder writes the quotes inside LifecyclePolicyText as \" (the default writes \u0022).
        var text = System.Text.Json.JsonSerializer.Serialize(policy, new System.Text.Json.JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

        Assert.Contains(@"\""rulePriority\"":1", text);
        Assert.Contains(@"\""tagPrefixList\"":[\""prod-\""]", text);
        Assert.Contains(@"\""countNumber\"":20", text);
        Assert.Contains(@"\""rulePriority\"":2", text);
        Assert.Contains(@"\""tagStatus\"":\""any\""", text);
        Assert.Contains(@"\""countNumber\"":30", text);
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void Deploy_roles_trust_only_their_github_environment(string env)
    {
        Ci.HasResourceProperties("AWS::IAM::Role", new Dictionary<string, object>
        {
            ["RoleName"] = $"es-ci-{env}",
            ["AssumeRolePolicyDocument"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["Statement"] = new object[]
                {
                    Match.ObjectLike(new Dictionary<string, object>
                    {
                        ["Action"] = "sts:AssumeRoleWithWebIdentity",
                        ["Effect"] = "Allow",
                        ["Principal"] = new Dictionary<string, object>
                        {
                            ["Federated"] = new Dictionary<string, object> { ["Fn::GetAtt"] = new object[] { OidcProviderId, "Arn" } },
                        },
                        ["Condition"] = new Dictionary<string, object>
                        {
                            ["StringEquals"] = new Dictionary<string, object>
                            {
                                ["token.actions.githubusercontent.com:aud"] = "sts.amazonaws.com",
                                ["token.actions.githubusercontent.com:sub"] = $"repo:extraspace-tech/cms:environment:{env}",
                            },
                        },
                    }),
                },
            }),
        });
    }

    [Theory]
    [InlineData("staging", "production")]
    [InlineData("production", "staging")]
    public void Deploy_roles_are_scoped_to_their_environment(string env, string other)
    {
        var policies = Ci.FindResources("AWS::IAM::Policy", new Dictionary<string, object>
        {
            ["Properties"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["Roles"] = new object[] { Match.ObjectLike(new Dictionary<string, object> { ["Ref"] = Match.StringLikeRegexp($"^DeployRole{env}") }) },
            }),
        });
        var text = System.Text.Json.JsonSerializer.Serialize(policies);

        Assert.Contains($"service/es-{env}/*", text);
        Assert.Contains($"role/es-{env}-task", text);
        Assert.Contains($"role/es-{env}-task-execution", text);
        Assert.Contains($"parameter/es/{env}/app/image-tag", text);
        Assert.Contains("ecs-tasks.amazonaws.com", text);            // iam:PassRole only to ECS tasks
        Assert.DoesNotContain($"service/es-{other}/", text);
        Assert.DoesNotContain($"role/es-{other}-", text);
        Assert.DoesNotContain("iam:*", text);
        Assert.DoesNotContain("cloudformation:", text);
        // Production reads staging's tag (to promote the same image) but never writes it; staging knows nothing of production.
        if (env == "staging") Assert.DoesNotContain("parameter/es/production", text);
    }

    [Fact]
    public void Only_staging_may_push_layers()
    {
        string Policy(string env) => System.Text.Json.JsonSerializer.Serialize(Ci.FindResources("AWS::IAM::Policy", new Dictionary<string, object>
        {
            ["Properties"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["Roles"] = new object[] { Match.ObjectLike(new Dictionary<string, object> { ["Ref"] = Match.StringLikeRegexp($"^DeployRole{env}") }) },
            }),
        }));

        Assert.Contains("ecr:UploadLayerPart", Policy("staging"));
        Assert.DoesNotContain("ecr:UploadLayerPart", Policy("production"));
        Assert.Contains("ecr:PutImage", Policy("production"));   // adds the prod-<sha> tag to an existing image
    }

    [Fact]
    public void Each_lifecycle_fact_belongs_to_its_rule()
    {
        var repository = Resources("AWS::ECR::Repository").Single().Value;
        var rules = JsonNode.Parse(repository["Properties"]!["LifecyclePolicy"]!["LifecyclePolicyText"]!.GetValue<string>())!["rules"]!.AsArray();

        Assert.Equal(2, rules.Count);
        var production = rules.Single(r => r!["rulePriority"]!.GetValue<int>() == 1)!;
        var rest = rules.Single(r => r!["rulePriority"]!.GetValue<int>() == 2)!;

        Assert.Equal(new[] { "prod-" }, production["selection"]!["tagPrefixList"]!.AsArray().Select(p => p!.GetValue<string>()));
        Assert.Equal("tagged", production["selection"]!["tagStatus"]!.GetValue<string>());
        Assert.Equal("imageCountMoreThan", production["selection"]!["countType"]!.GetValue<string>());
        Assert.Equal(20, production["selection"]!["countNumber"]!.GetValue<int>());
        Assert.Equal("any", rest["selection"]!["tagStatus"]!.GetValue<string>());
        Assert.Null(rest["selection"]!["tagPrefixList"]);
        Assert.Equal("imageCountMoreThan", rest["selection"]!["countType"]!.GetValue<string>());
        Assert.Equal(30, rest["selection"]!["countNumber"]!.GetValue<int>());
        Assert.All(rules, r => Assert.Equal("expire", r!["action"]!["type"]!.GetValue<string>()));
    }

    // The whole permission set, written out: one extra pair or one wider resource is a mismatch.
    private static readonly string[] StagingGrants =
    [
        "ecr:BatchCheckLayerAvailability -> REPOSITORY",
        "ecr:BatchGetImage -> REPOSITORY",
        "ecr:CompleteLayerUpload -> REPOSITORY",
        "ecr:DescribeImages -> REPOSITORY",
        "ecr:GetAuthorizationToken -> *",
        "ecr:GetDownloadUrlForLayer -> REPOSITORY",
        "ecr:InitiateLayerUpload -> REPOSITORY",
        "ecr:PutImage -> REPOSITORY",
        "ecr:UploadLayerPart -> REPOSITORY",
        "ecs:DescribeServices -> arn:PARTITION:ecs:REGION:ACCOUNT:service/es-staging/*",
        "ecs:DescribeTaskDefinition -> *",
        "ecs:ListServices -> *",
        "ecs:RegisterTaskDefinition -> *",
        "ecs:UpdateService -> arn:PARTITION:ecs:REGION:ACCOUNT:service/es-staging/*",
        "iam:PassRole -> arn:PARTITION:iam::ACCOUNT:role/es-staging-task",
        "iam:PassRole -> arn:PARTITION:iam::ACCOUNT:role/es-staging-task-execution",
        "ssm:GetParameter -> arn:PARTITION:ssm:REGION:ACCOUNT:parameter/es/staging/app/image-tag",
        "ssm:PutParameter -> arn:PARTITION:ssm:REGION:ACCOUNT:parameter/es/staging/app/image-tag",
    ];

    // No layer upload (production never builds); reads staging's tag to promote the same image.
    private static readonly string[] ProductionGrants =
    [
        "ecr:BatchCheckLayerAvailability -> REPOSITORY",
        "ecr:BatchGetImage -> REPOSITORY",
        "ecr:DescribeImages -> REPOSITORY",
        "ecr:GetAuthorizationToken -> *",
        "ecr:GetDownloadUrlForLayer -> REPOSITORY",
        "ecr:PutImage -> REPOSITORY",
        "ecs:DescribeServices -> arn:PARTITION:ecs:REGION:ACCOUNT:service/es-production/*",
        "ecs:DescribeTaskDefinition -> *",
        "ecs:ListServices -> *",
        "ecs:RegisterTaskDefinition -> *",
        "ecs:UpdateService -> arn:PARTITION:ecs:REGION:ACCOUNT:service/es-production/*",
        "iam:PassRole -> arn:PARTITION:iam::ACCOUNT:role/es-production-task",
        "iam:PassRole -> arn:PARTITION:iam::ACCOUNT:role/es-production-task-execution",
        "ssm:GetParameter -> arn:PARTITION:ssm:REGION:ACCOUNT:parameter/es/production/app/image-tag",
        "ssm:GetParameter -> arn:PARTITION:ssm:REGION:ACCOUNT:parameter/es/staging/app/image-tag",
        "ssm:PutParameter -> arn:PARTITION:ssm:REGION:ACCOUNT:parameter/es/production/app/image-tag",
    ];

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void Deploy_role_grants_are_exactly_the_expected_set(string env)
    {
        var expected = env == "staging" ? StagingGrants : ProductionGrants;

        var actual = Statements(env).SelectMany(s => s.Actions.SelectMany(a => s.Resources.Select(r => $"{a} -> {r}"))).Distinct().Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(expected.Order(StringComparer.Ordinal).ToArray(), actual);
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void Deploy_roles_get_permissions_only_from_their_one_policy(string env)
    {
        var role = Resources("AWS::IAM::Role").Single(r => r.Key.StartsWith($"DeployRole{env}")).Value["Properties"]!;

        Assert.Null(role["Policies"]);
        Assert.Null(role["ManagedPolicyArns"]);
        Assert.Null(role["PermissionsBoundary"]);
        Assert.Single(PoliciesOf(env));
        Assert.All(Statements(env), s => Assert.Equal("Allow", s.Effect));
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void Only_four_actions_apply_to_every_resource(string env)
    {
        var wide = Statements(env).Where(s => s.Resources.Any(r => r == "*")).SelectMany(s => s.Actions).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "ecr:GetAuthorizationToken", "ecs:DescribeTaskDefinition", "ecs:ListServices", "ecs:RegisterTaskDefinition" }, wide);
        // The one other wildcard is the service name inside the role's own cluster.
        var partial = Statements(env).SelectMany(s => s.Resources).Where(r => r != "*" && r.Contains('*')).Distinct().ToArray();
        Assert.Equal(new[] { $"arn:PARTITION:ecs:REGION:ACCOUNT:service/es-{env}/*" }, partial);
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void Pass_role_is_one_statement_for_the_two_task_roles_and_only_to_ecs_tasks(string env)
    {
        var statements = Statements(env);
        var statement = Assert.Single(statements, s => s.Actions.Any(a => a.Contains("PassRole", StringComparison.OrdinalIgnoreCase)));

        Assert.Equal(new[] { "iam:PassRole" }, statement.Actions);
        Assert.Equal(
            new[] { $"arn:PARTITION:iam::ACCOUNT:role/es-{env}-task", $"arn:PARTITION:iam::ACCOUNT:role/es-{env}-task-execution" },
            statement.Resources.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("""{"StringEquals":{"iam:PassedToService":"ecs-tasks.amazonaws.com"}}""", statement.Condition);
        Assert.All(statements.Where(s => !ReferenceEquals(s, statement)), s => Assert.Null(s.Condition));
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void The_image_tag_is_written_only_to_the_roles_own_parameter(string env)
    {
        var statement = Assert.Single(Statements(env), s => s.Actions.Contains("ssm:PutParameter"));

        Assert.Equal(new[] { $"arn:PARTITION:ssm:REGION:ACCOUNT:parameter/es/{env}/app/image-tag" }, statement.Resources);
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void No_action_is_a_wildcard(string env)
    {
        var actions = Statements(env).SelectMany(s => s.Actions).ToArray();

        Assert.NotEmpty(actions);
        Assert.DoesNotContain(actions, a => a.Contains('*') || a.Contains('?'));
        Assert.All(Statements(env), s => Assert.False(s.HasNotActionOrNotResource));
    }

    private sealed record Statement(string Effect, string[] Actions, string[] Resources, string? Condition, bool HasNotActionOrNotResource);

    private static string OidcProviderId => Resources("AWS::IAM::OIDCProvider").Single().Key;

    private static Dictionary<string, JsonNode> Resources(string type) =>
        JsonNode.Parse(JsonSerializer.Serialize(Ci.ToJSON()))!["Resources"]!.AsObject()
            .Where(r => r.Value!["Type"]!.GetValue<string>() == type)
            .ToDictionary(r => r.Key, r => r.Value!);

    private static JsonNode[] PoliciesOf(string env) =>
        Resources("AWS::IAM::Policy").Values
            .Where(p => p["Properties"]!["Roles"]!.AsArray().Any(r => r!["Ref"]!.GetValue<string>().StartsWith($"DeployRole{env}")))
            .ToArray();

    private static Statement[] Statements(string env) =>
        PoliciesOf(env)
            .SelectMany(p => p["Properties"]!["PolicyDocument"]!["Statement"]!.AsArray())
            .Select(s => new Statement(
                s!["Effect"]!.GetValue<string>(),
                OneOrMany(s["Action"]).Select(a => a.GetValue<string>()).ToArray(),
                OneOrMany(s["Resource"]).Select(Readable).ToArray(),
                s["Condition"]?.ToJsonString(),
                s["NotAction"] is not null || s["NotResource"] is not null))
            .ToArray();

    private static IEnumerable<JsonNode> OneOrMany(JsonNode? node) => node switch
    {
        null => [],
        JsonArray array => array.Select(n => n!),
        _ => [node],
    };

    // Turns a CloudFormation ARN into text that reads the same for any account; an unknown form stays raw and fails the comparison.
    private static string Readable(JsonNode resource)
    {
        if (resource is JsonValue value) return Markers(value.GetValue<string>());
        if (resource["Fn::GetAtt"] is JsonArray getAtt
            && getAtt[0]!.GetValue<string>() == Resources("AWS::ECR::Repository").Single().Key
            && getAtt[1]!.GetValue<string>() == "Arn") return "REPOSITORY";
        if (resource["Fn::Join"] is JsonArray join && join[0]!.GetValue<string>() == "")
        {
            return Markers(string.Concat(join[1]!.AsArray().Select(part => part switch
            {
                JsonValue text => text.GetValue<string>(),
                JsonObject reference when reference["Ref"]?.GetValue<string>() == "AWS::Partition" => "PARTITION",
                _ => part!.ToJsonString(),
            })));
        }
        return resource.ToJsonString();
    }

    private static string Markers(string arn) => arn.Replace("123456789012", "ACCOUNT").Replace("ap-southeast-1", "REGION");
}
