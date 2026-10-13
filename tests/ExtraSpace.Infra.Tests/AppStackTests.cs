using System.Text.Json;
using System.Text.Json.Nodes;
using Amazon.CDK.Assertions;
using Xunit;

namespace ExtraSpace.Infra.Tests;

public class AppStackTests
{
    private static Template App(string env) => TestStacks.For($"EsApp-{env}");

    private static Dictionary<string, object> Container(Dictionary<string, object> properties) => new()
    {
        ["ContainerDefinitions"] = new object[] { Match.ObjectLike(properties) },
    };

    private static object Env(string name, object value) => new Dictionary<string, object> { ["Name"] = name, ["Value"] = value };

    [Theory]
    [InlineData("staging", "Staging")]
    [InlineData("production", "Production")]
    public void Tasks_run_as_Staging_or_Production_only(string env, string expected)
    {
        var template = App(env);
        var definitions = template.FindResources("AWS::ECS::TaskDefinition");

        Assert.NotEmpty(definitions);
        template.ResourcePropertiesCountIs("AWS::ECS::TaskDefinition", Container(new Dictionary<string, object>
        {
            ["Environment"] = Match.ArrayWith(new object[] { Env("ASPNETCORE_ENVIRONMENT", expected) }),
        }), definitions.Count);
        var text = System.Text.Json.JsonSerializer.Serialize(definitions);
        Assert.DoesNotContain("\"Development\"", text);
        Assert.DoesNotContain("\"Testing\"", text);
    }

    [Fact]
    public void Staging_runs_one_task_with_both_roles()
    {
        var template = App("staging");

        template.ResourceCountIs("AWS::ECS::Service", 1);
        template.HasResourceProperties("AWS::ECS::Service", new Dictionary<string, object>
        {
            ["ServiceName"] = "es-staging-api",
            ["DesiredCount"] = 1,
            ["LaunchType"] = "FARGATE",
        });
        template.HasResourceProperties("AWS::ECS::TaskDefinition", new Dictionary<string, object>
        {
            ["Family"] = "es-staging-api",
            ["Cpu"] = "1024",
            ["Memory"] = "2048",
            ["RuntimePlatform"] = new Dictionary<string, object> { ["CpuArchitecture"] = "ARM64", ["OperatingSystemFamily"] = "LINUX" },
            ["ContainerDefinitions"] = new object[]
            {
                Match.ObjectLike(new Dictionary<string, object>
                {
                    ["Name"] = "app",
                    ["Environment"] = Match.ArrayWith(new object[] { Env("App__Role", "All") }),
                }),
            },
        });
        template.ResourceCountIs("AWS::ApplicationAutoScaling::ScalableTarget", 0);
    }

    [Fact]
    public void Production_splits_api_and_jobs_and_scales_the_api()
    {
        var template = App("production");

        template.ResourceCountIs("AWS::ECS::Service", 2);
        template.HasResourceProperties("AWS::ECS::TaskDefinition", new Dictionary<string, object>
        {
            ["Family"] = "es-production-api",
            ["Cpu"] = "2048",
            ["Memory"] = "4096",
            ["ContainerDefinitions"] = new object[]
            {
                Match.ObjectLike(new Dictionary<string, object> { ["Environment"] = Match.ArrayWith(new object[] { Env("App__Role", "Api") }) }),
            },
        });
        template.HasResourceProperties("AWS::ECS::TaskDefinition", new Dictionary<string, object>
        {
            ["Family"] = "es-production-jobs",
            ["Cpu"] = "1024",
            ["Memory"] = "2048",
            ["ContainerDefinitions"] = new object[]
            {
                Match.ObjectLike(new Dictionary<string, object>
                {
                    ["Environment"] = Match.ArrayWith(new object[] { Env("App__Role", "Jobs") }),
                    ["PortMappings"] = Match.Absent(),
                }),
            },
        });
        // The api service scales between 2 and 4: a fixed count here would cut it back on every deploy.
        template.HasResourceProperties("AWS::ECS::Service", new Dictionary<string, object>
        {
            ["ServiceName"] = "es-production-api",
            ["DesiredCount"] = Match.Absent(),
        });
        template.HasResourceProperties("AWS::ECS::Service", new Dictionary<string, object>
        {
            ["ServiceName"] = "es-production-jobs",
            ["DesiredCount"] = 1,
            ["LoadBalancers"] = Match.Absent(),
        });
        template.HasResourceProperties("AWS::ApplicationAutoScaling::ScalableTarget", new Dictionary<string, object>
        {
            ["MinCapacity"] = 2,
            ["MaxCapacity"] = 4,
        });
        template.ResourceCountIs("AWS::ApplicationAutoScaling::ScalingPolicy", 2);
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void Every_service_rolls_back_a_failed_deploy_and_stays_private(string env)
    {
        var template = App(env);
        var count = template.FindResources("AWS::ECS::Service").Count;

        template.ResourcePropertiesCountIs("AWS::ECS::Service", new Dictionary<string, object>
        {
            ["DeploymentConfiguration"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["DeploymentCircuitBreaker"] = new Dictionary<string, object> { ["Enable"] = true, ["Rollback"] = true },
                ["MinimumHealthyPercent"] = 100,
                ["MaximumPercent"] = 200,
            }),
            ["NetworkConfiguration"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["AwsvpcConfiguration"] = Match.ObjectLike(new Dictionary<string, object> { ["AssignPublicIp"] = "DISABLED" }),
            }),
            ["EnableExecuteCommand"] = Match.Absent(),
        }, count);
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void The_task_gets_its_settings_and_only_references_to_secrets(string env)
    {
        var template = App(env);
        var count = template.FindResources("AWS::ECS::TaskDefinition").Count;
        var inputs = TestInputs.For(env);

        template.ResourcePropertiesCountIs("AWS::ECS::TaskDefinition", Container(new Dictionary<string, object>
        {
            ["Environment"] = Match.ArrayWith(new object[]
            {
                Env("Admin__AllowedEmailDomains__0", "example.test"),
                Env("Admin__BootstrapEmails__0", "qa@example.test"),
                Env("App__MinVersion__android", "0.0.0"),
                Env("App__MinVersion__ios", "0.0.0"),
                Env("App__MinVersion__web", "0.0.0"),
                Env("App__Version", Match.AnyValue()),
                Env("Cors__AllowedOrigins__0", "https://web.example.test"),
                Env("DataProtection__SsmPath", $"/es/{env}/data-protection/"),
                Env("Database__Host", Match.AnyValue()),
                Env("Email__From", inputs.EmailFrom),
            }),
            ["Secrets"] = Match.ArrayWith(new object[]
            {
                Match.ObjectLike(new Dictionary<string, object> { ["Name"] = "CustomerAuth__SigningKey" }),
                Match.ObjectLike(new Dictionary<string, object> { ["Name"] = "Database__Password" }),
                Match.ObjectLike(new Dictionary<string, object> { ["Name"] = "Database__Username" }),
            }),
        }), count);

        var text = System.Text.Json.JsonSerializer.Serialize(template.FindResources("AWS::ECS::TaskDefinition"));
        Assert.DoesNotContain("ConnectionStrings", text);
        Assert.DoesNotContain("SigningKey\",\"Value\"", text);
    }

    private static readonly string[] SettingNames =
    [
        "ASPNETCORE_ENVIRONMENT",
        "Admin__AllowedEmailDomains__0",
        "Admin__BootstrapEmails__0",
        "App__MinVersion__android",
        "App__MinVersion__ios",
        "App__MinVersion__web",
        "App__Role",
        "App__Version",
        "Cors__AllowedOrigins__0",
        "DataProtection__SsmPath",
        "Database__Host",
        "Database__Name",
        "Email__From",
    ];

    // The whole list, written out: one extra variable (Database__RequireMigrated, ConnectionStrings__Default) is a mismatch.
    [Theory]
    [InlineData("staging", "api", true)]
    [InlineData("production", "api", false)]
    [InlineData("production", "jobs", false)]
    public void The_container_gets_exactly_these_names(string env, string name, bool allowList)
    {
        var container = AppContainer(env, name);
        var expected = allowList
            ? SettingNames.Concat(["Email__AllowedRecipients__0", "Email__AllowedRecipients__1"]).Order(StringComparer.Ordinal).ToArray()
            : SettingNames.Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, container["Environment"]!.AsArray().Select(e => e!["Name"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            new[] { "CustomerAuth__SigningKey", "Database__Password", "Database__Username" },
            container["Secrets"]!.AsArray().Select(e => e!["Name"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("extraspace", Setting(container, "Database__Name")!.GetValue<string>());
    }

    [Theory]
    [InlineData("staging", "api")]
    [InlineData("production", "api")]
    [InlineData("production", "jobs")]
    public void The_reported_version_is_the_tag_of_the_image_that_runs(string env, string name)
    {
        var container = AppContainer(env, name);
        var image = container["Image"]!["Fn::Join"]![1]!.AsArray();

        Assert.EndsWith("/extraspace/api:", image[^2]!.GetValue<string>());
        var parameter = image[^1]!["Ref"]!.GetValue<string>();
        Assert.Equal(parameter, Setting(container, "App__Version")!["Ref"]!.GetValue<string>());
        var declared = Json($"EsApp-{env}")["Parameters"]![parameter]!;
        Assert.Equal("AWS::SSM::Parameter::Value<String>", declared["Type"]!.GetValue<string>());
        Assert.Equal($"/es/{env}/app/image-tag", declared["Default"]!.GetValue<string>());
    }

    // Without an init process a failed start-up can leave the task running, and the jobs service has no health check to notice.
    [Theory]
    [InlineData("staging", 1)]
    [InlineData("production", 2)]
    public void Every_container_runs_under_an_init_process(string env, int definitions)
    {
        var template = App(env);

        template.ResourceCountIs("AWS::ECS::TaskDefinition", definitions);
        template.ResourcePropertiesCountIs("AWS::ECS::TaskDefinition", Container(new Dictionary<string, object>
        {
            ["LinuxParameters"] = Match.ObjectLike(new Dictionary<string, object> { ["InitProcessEnabled"] = true }),
        }), definitions);
    }

    [Fact]
    public void An_unmapped_log_retention_stops_the_synth()
    {
        var config = EnvConfig.Staging with { LogRetentionDays = 30 };
        var app = new Amazon.CDK.App();
        var env = new Amazon.CDK.Environment { Account = "123456789012", Region = InfraComposer.Region };
        var network = new Stacks.NetworkStack(app, "Network", new Stacks.NetworkStackProps { Env = env, Config = config });
        var data = new Stacks.DataStack(app, "Data", new Stacks.DataStackProps { Env = env, Config = config, Vpc = network.Vpc });
        var edge = new Stacks.EdgeStack(app, "Edge", new Stacks.EdgeStackProps { Env = env, Config = config, Inputs = TestInputs.For("staging") });

        var error = Assert.Throws<InvalidOperationException>(() => new Stacks.AppStack(app, "App", new Stacks.AppStackProps
        {
            Env = env,
            Config = config,
            Inputs = TestInputs.For("staging"),
            Vpc = network.Vpc,
            Data = data,
            Certificate = edge.Certificate,
        }));
        Assert.Contains("30", error.Message);
    }

    [Fact]
    public void Only_staging_carries_the_email_allow_list()
    {
        App("staging").HasResourceProperties("AWS::ECS::TaskDefinition", Container(new Dictionary<string, object>
        {
            ["Environment"] = Match.ArrayWith(new object[]
            {
                Env("Email__AllowedRecipients__0", "qa@example.test"),
                Env("Email__AllowedRecipients__1", "@team.example.test"),
            }),
        }));
        Assert.DoesNotContain("Email__AllowedRecipients",
            System.Text.Json.JsonSerializer.Serialize(App("production").FindResources("AWS::ECS::TaskDefinition")));
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void The_image_comes_from_the_tag_parameter(string env)
    {
        var template = App(env);

        // An SSM-typed stack parameter, resolved at deploy time: CI writes the tag, CDK only reads it.
        template.HasParameter("*", new Dictionary<string, object>
        {
            ["Type"] = "AWS::SSM::Parameter::Value<String>",
            ["Default"] = $"/es/{env}/app/image-tag",
        });
        template.ResourceCountIs("AWS::SSM::Parameter", 0);
        Assert.Contains("extraspace/api:", System.Text.Json.JsonSerializer.Serialize(template.FindResources("AWS::ECS::TaskDefinition")));
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void Only_the_load_balancer_reaches_the_tasks(string env)
    {
        var template = App(env);

        var tasks = GroupId(SecurityGroup($"EsApp-{env}", $"ExtraSpace {env} app tasks"));
        var loadBalancer = GroupId(SecurityGroup($"EsApp-{env}", $"ExtraSpace {env} load balancer"));
        // Where the app connects: the RDS Proxy's group in production, the database's in staging.
        var entry = SecurityGroup($"EsData-{env}", env == "production" ? $"ExtraSpace {env} RDS Proxy" : $"ExtraSpace {env} PostgreSQL");
        var entryExport = Json($"EsData-{env}")["Outputs"]!.AsObject().Select(o => o.Value!)
            .Single(o => o["Value"] is JsonObject value && value["Fn::GetAtt"] is JsonArray getAtt && getAtt[0]!.GetValue<string>() == entry && getAtt[1]!.GetValue<string>() == "GroupId")
            ["Export"]!["Name"]!.GetValue<string>();

        // Two ingress rules exist as separate resources: tasks from the ALB, and the database (or proxy) from the tasks.
        template.ResourcePropertiesCountIs("AWS::EC2::SecurityGroupIngress", new Dictionary<string, object>
        {
            ["IpProtocol"] = "tcp",
            ["FromPort"] = 8080,
            ["ToPort"] = 8080,
            ["GroupId"] = tasks,
            ["SourceSecurityGroupId"] = loadBalancer,
        }, 1);
        template.ResourcePropertiesCountIs("AWS::EC2::SecurityGroupIngress", new Dictionary<string, object>
        {
            ["IpProtocol"] = "tcp",
            ["FromPort"] = 5432,
            ["ToPort"] = 5432,
            ["GroupId"] = new Dictionary<string, object> { ["Fn::ImportValue"] = entryExport },
            ["SourceSecurityGroupId"] = tasks,
        }, 1);
        template.ResourceCountIs("AWS::EC2::SecurityGroupIngress", 2);
        template.HasResourceProperties("AWS::EC2::SecurityGroup", new Dictionary<string, object>
        {
            ["GroupDescription"] = $"ExtraSpace {env} app tasks",
            ["SecurityGroupIngress"] = Match.Absent(),
        });
        template.HasResourceProperties("AWS::EC2::SecurityGroup", new Dictionary<string, object>
        {
            ["GroupDescription"] = $"ExtraSpace {env} load balancer",
            ["SecurityGroupIngress"] = new object[]
            {
                Match.ObjectLike(new Dictionary<string, object> { ["CidrIp"] = "0.0.0.0/0", ["FromPort"] = 443, ["ToPort"] = 443 }),
                Match.ObjectLike(new Dictionary<string, object> { ["CidrIp"] = "0.0.0.0/0", ["FromPort"] = 80, ["ToPort"] = 80 }),
            },
        });
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void Alb_appends_the_client_ip(string env)
    {
        App(env).HasResourceProperties("AWS::ElasticLoadBalancingV2::LoadBalancer", new Dictionary<string, object>
        {
            ["Name"] = $"es-{env}",
            ["Scheme"] = "internet-facing",
            ["LoadBalancerAttributes"] = Match.ArrayWith(new object[]
            {
                new Dictionary<string, object> { ["Key"] = "routing.http.xff_header_processing.mode", ["Value"] = "append" },
                new Dictionary<string, object> { ["Key"] = "routing.http.drop_invalid_header_fields.enabled", ["Value"] = "true" },
            }),
        });
    }

    [Theory]
    [InlineData("staging", "false")]
    [InlineData("production", "true")]
    public void Only_the_production_load_balancer_is_protected_from_deletion(string env, string expected)
    {
        App(env).HasResourceProperties("AWS::ElasticLoadBalancingV2::LoadBalancer", new Dictionary<string, object>
        {
            ["LoadBalancerAttributes"] = Match.ArrayWith(new object[]
            {
                new Dictionary<string, object> { ["Key"] = "deletion_protection.enabled", ["Value"] = expected },
            }),
        });
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void Https_only_with_a_redirect_and_the_live_health_check(string env)
    {
        var template = App(env);

        template.ResourceCountIs("AWS::ElasticLoadBalancingV2::Listener", 2);

        template.HasResourceProperties("AWS::ElasticLoadBalancingV2::Listener", new Dictionary<string, object>
        {
            ["Port"] = 443,
            ["Protocol"] = "HTTPS",
            ["SslPolicy"] = Match.StringLikeRegexp("TLS13"),
            ["Certificates"] = Match.AnyValue(),
        });
        template.HasResourceProperties("AWS::ElasticLoadBalancingV2::Listener", new Dictionary<string, object>
        {
            ["Port"] = 80,
            ["DefaultActions"] = new object[]
            {
                Match.ObjectLike(new Dictionary<string, object>
                {
                    ["Type"] = "redirect",
                    ["RedirectConfig"] = Match.ObjectLike(new Dictionary<string, object> { ["Protocol"] = "HTTPS", ["Port"] = "443", ["StatusCode"] = "HTTP_301" }),
                }),
            },
        });
        template.HasResourceProperties("AWS::ElasticLoadBalancingV2::TargetGroup", new Dictionary<string, object>
        {
            ["Port"] = 8080,
            ["Protocol"] = "HTTP",
            ["TargetType"] = "ip",
            ["HealthCheckPath"] = "/health/live",
            ["Matcher"] = new Dictionary<string, object> { ["HttpCode"] = "200" },
        });
        template.HasResourceProperties("AWS::Route53::RecordSet", new Dictionary<string, object>
        {
            ["Name"] = TestInputs.For(env).Host + ".",
            ["Type"] = "A",
            ["AliasTarget"] = Match.AnyValue(),
        });
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void Task_role_reaches_only_its_key_ring_and_its_sender(string env)
    {
        var template = App(env);
        template.HasResourceProperties("AWS::IAM::Role", new Dictionary<string, object> { ["RoleName"] = $"es-{env}-task" });
        template.HasResourceProperties("AWS::IAM::Role", new Dictionary<string, object> { ["RoleName"] = $"es-{env}-task-execution" });

        var policies = template.FindResources("AWS::IAM::Policy", new Dictionary<string, object>
        {
            ["Properties"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["Roles"] = new object[] { Match.ObjectLike(new Dictionary<string, object> { ["Ref"] = Match.StringLikeRegexp("^TaskRole") }) },
            }),
        });
        var text = System.Text.Json.JsonSerializer.Serialize(policies);

        Assert.Contains($"parameter/es/{env}/data-protection", text);
        Assert.Contains("ssm:GetParametersByPath", text);
        Assert.Contains("ssm:PutParameter", text);
        Assert.Contains("ses:SendEmail", text);
        Assert.Contains("ses:FromAddress", text);
        Assert.Contains(TestInputs.For(env).EmailFromAddress, text);
        Assert.DoesNotContain("secretsmanager:", text);   // secrets reach the task through the execution role only
        Assert.DoesNotContain("\"Action\":\"*\"", text);
    }

    // The whole permission set, written out: one extra pair or one wider resource is a mismatch.
    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void Task_role_grants_are_exactly_the_expected_set(string env)
    {
        var keyRing = $"arn:PARTITION:ssm:REGION:ACCOUNT:parameter/es/{env}/data-protection";
        var statements = Statements(env, "TaskRole");

        Assert.Equal(
            new[]
            {
                "ses:SendEmail -> arn:PARTITION:ses:REGION:ACCOUNT:identity/*",
                $"ssm:GetParametersByPath -> {keyRing}",
                $"ssm:GetParametersByPath -> {keyRing}/*",
                $"ssm:PutParameter -> {keyRing}",
                $"ssm:PutParameter -> {keyRing}/*",
            },
            Grants(statements));
        var send = Assert.Single(statements, s => s.Actions.Contains("ses:SendEmail"));
        Assert.Equal("""{"StringEquals":{"ses:FromAddress":"ADDRESS"}}""".Replace("ADDRESS", TestInputs.For(env).EmailFromAddress), send.Condition);
        AssertOnlyTheOneAllowPolicy(env, "TaskRole", statements);
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void Execution_role_reads_only_this_environments_two_secrets(string env)
    {
        var statements = Statements(env, "ExecutionRole");
        var secrets = statements.Where(s => s.Actions.Any(a => a.StartsWith("secretsmanager:", StringComparison.OrdinalIgnoreCase))).ToArray();

        Assert.Equal(
            new[]
            {
                $"secretsmanager:DescribeSecret -> SECRET es/{env}/customer-signing-key",
                $"secretsmanager:DescribeSecret -> SECRET es/{env}/db-app",
                $"secretsmanager:GetSecretValue -> SECRET es/{env}/customer-signing-key",
                $"secretsmanager:GetSecretValue -> SECRET es/{env}/db-app",
            },
            Grants(secrets));

        var logs = env == "production" ? new[] { "api", "jobs" } : new[] { "api" };
        var expected = new List<string>
        {
            "ecr:BatchCheckLayerAvailability -> arn:PARTITION:ecr:REGION:ACCOUNT:repository/extraspace/api",
            "ecr:BatchGetImage -> arn:PARTITION:ecr:REGION:ACCOUNT:repository/extraspace/api",
            "ecr:GetAuthorizationToken -> *",
            "ecr:GetDownloadUrlForLayer -> arn:PARTITION:ecr:REGION:ACCOUNT:repository/extraspace/api",
        };
        foreach (var name in logs)
        {
            expected.Add($"logs:CreateLogStream -> LOGS /extraspace/{env}/{name}");
            expected.Add($"logs:PutLogEvents -> LOGS /extraspace/{env}/{name}");
        }
        Assert.Equal(expected.Order(StringComparer.Ordinal).ToArray(), Grants(statements.Except(secrets)));
        // The registry sign-in has no resource-level permission; nothing else may be open.
        Assert.Equal(new[] { "ecr:GetAuthorizationToken" }, statements.Where(s => s.Resources.Any(r => r.Contains('*'))).SelectMany(s => s.Actions).ToArray());
        AssertOnlyTheOneAllowPolicy(env, "ExecutionRole", statements);
    }

    [Theory]
    [InlineData("staging", 14, 1)]
    [InlineData("production", 90, 2)]
    public void Log_groups_are_named_per_role_and_expire(string env, int days, int groups)
    {
        var template = App(env);

        template.HasResourceProperties("AWS::Logs::LogGroup", new Dictionary<string, object>
        {
            ["LogGroupName"] = $"/extraspace/{env}/api",
            ["RetentionInDays"] = days,
        });
        template.ResourceCountIs("AWS::Logs::LogGroup", groups);
        if (groups == 2)
        {
            template.HasResourceProperties("AWS::Logs::LogGroup", new Dictionary<string, object>
            {
                ["LogGroupName"] = $"/extraspace/{env}/jobs",
                ["RetentionInDays"] = days,
            });
        }
        template.HasResourceProperties("AWS::ECS::Cluster", new Dictionary<string, object>
        {
            ["ClusterName"] = $"es-{env}",
            ["ClusterSettings"] = new object[] { Match.ObjectLike(new Dictionary<string, object> { ["Name"] = "containerInsights" }) },
        });
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void The_signing_key_is_generated_and_no_lambda_is_needed(string env)
    {
        var template = App(env);

        template.HasResourceProperties("AWS::SecretsManager::Secret", new Dictionary<string, object>
        {
            ["Name"] = $"es/{env}/customer-signing-key",
            // 64 letters and digits are valid base64 for 48 bytes, which is what CustomerAuth:SigningKey must be.
            ["GenerateSecretString"] = new Dictionary<string, object> { ["PasswordLength"] = 64, ["ExcludePunctuation"] = true },
        });
        template.ResourceCountIs("AWS::Lambda::Function", 0);
    }

    private sealed record Statement(string Effect, string[] Actions, string[] Resources, string? Condition, bool HasNotActionOrNotResource);

    private static string[] Grants(IEnumerable<Statement> statements) =>
        statements.SelectMany(s => s.Actions.SelectMany(a => s.Resources.Select(r => $"{a} -> {r}"))).Distinct().Order(StringComparer.Ordinal).ToArray();

    private static void AssertOnlyTheOneAllowPolicy(string env, string roleId, Statement[] statements)
    {
        var role = Resources($"EsApp-{env}", "AWS::IAM::Role").Single(r => r.Key.StartsWith(roleId)).Value["Properties"]!;

        Assert.Null(role["Policies"]);
        Assert.Null(role["ManagedPolicyArns"]);
        Assert.Null(role["PermissionsBoundary"]);
        Assert.Single(PoliciesOf(env, roleId));
        Assert.NotEmpty(statements);
        Assert.All(statements, s => Assert.Equal("Allow", s.Effect));
        Assert.All(statements, s => Assert.False(s.HasNotActionOrNotResource));
        Assert.DoesNotContain(statements.SelectMany(s => s.Actions), a => a.Contains('*') || a.Contains('?'));
    }

    private static JsonNode AppContainer(string env, string name) =>
        Resources($"EsApp-{env}", "AWS::ECS::TaskDefinition").Values
            .Single(d => d["Properties"]!["Family"]!.GetValue<string>() == $"es-{env}-{name}")
            ["Properties"]!["ContainerDefinitions"]!.AsArray().Single()!;

    private static JsonNode? Setting(JsonNode container, string name) =>
        container["Environment"]!.AsArray().Single(e => e!["Name"]!.GetValue<string>() == name)!["Value"];

    private static string SecurityGroup(string stackName, string description) =>
        Resources(stackName, "AWS::EC2::SecurityGroup").Single(g => g.Value["Properties"]!["GroupDescription"]!.GetValue<string>() == description).Key;

    private static Dictionary<string, object> GroupId(string logicalId) => new() { ["Fn::GetAtt"] = new object[] { logicalId, "GroupId" } };

    private static JsonObject Json(string stackName) => JsonNode.Parse(JsonSerializer.Serialize(TestStacks.For(stackName).ToJSON()))!.AsObject();

    private static Dictionary<string, JsonNode> Resources(string stackName, string type) =>
        Json(stackName)["Resources"]!.AsObject()
            .Where(r => r.Value!["Type"]!.GetValue<string>() == type)
            .ToDictionary(r => r.Key, r => r.Value!);

    // Matched on the whole role id (TaskRole + CDK's 8-character hash), so "TaskRole" never picks up another role.
    private static JsonNode[] PoliciesOf(string env, string roleId)
    {
        var role = Resources($"EsApp-{env}", "AWS::IAM::Role").Keys.Single(k => k.StartsWith(roleId));
        return Resources($"EsApp-{env}", "AWS::IAM::Policy").Values
            .Where(p => p["Properties"]!["Roles"]!.AsArray().Any(r => r!["Ref"]!.GetValue<string>() == role))
            .ToArray();
    }

    private static Statement[] Statements(string env, string roleId) =>
        PoliciesOf(env, roleId)
            .SelectMany(p => p["Properties"]!["PolicyDocument"]!["Statement"]!.AsArray())
            .Select(s => new Statement(
                s!["Effect"]!.GetValue<string>(),
                OneOrMany(s["Action"]).Select(a => a.GetValue<string>()).ToArray(),
                OneOrMany(s["Resource"]).Select(r => Readable(env, r)).ToArray(),
                s["Condition"]?.ToJsonString(),
                s["NotAction"] is not null || s["NotResource"] is not null))
            .ToArray();

    private static IEnumerable<JsonNode> OneOrMany(JsonNode? node) => node switch
    {
        null => [],
        JsonArray array => array.Select(n => n!),
        _ => [node],
    };

    // Turns a CloudFormation reference into text that reads the same for any account; an unknown form stays raw and fails the comparison.
    private static string Readable(string env, JsonNode resource)
    {
        var app = Json($"EsApp-{env}");
        if (resource is JsonValue value) return Markers(value.GetValue<string>());
        if (resource["Ref"] is JsonValue reference && Named(app, reference.GetValue<string>(), "AWS::SecretsManager::Secret", "Name") is { } secret)
            return $"SECRET {secret}";
        if (resource["Fn::ImportValue"] is JsonValue import)
        {
            // The secret lives in the data stack: follow the export back to the resource it names.
            var data = Json($"EsData-{env}");
            var exported = data["Outputs"]!.AsObject().Select(o => o.Value!)
                .SingleOrDefault(o => o["Export"]?["Name"]?.GetValue<string>() == import.GetValue<string>());
            if (exported?["Value"]?["Ref"] is JsonValue target && Named(data, target.GetValue<string>(), "AWS::SecretsManager::Secret", "Name") is { } remote)
                return $"SECRET {remote}";
        }
        if (resource["Fn::GetAtt"] is JsonArray getAtt && getAtt[1]!.GetValue<string>() == "Arn"
            && Named(app, getAtt[0]!.GetValue<string>(), "AWS::Logs::LogGroup", "LogGroupName") is { } logGroup)
            return $"LOGS {logGroup}";
        if (resource["Fn::Join"] is JsonArray join && join[0]!.GetValue<string>() == "")
        {
            return Markers(string.Concat(join[1]!.AsArray().Select(part => part switch
            {
                JsonValue text => text.GetValue<string>(),
                JsonObject partition when partition["Ref"]?.GetValue<string>() == "AWS::Partition" => "PARTITION",
                _ => part!.ToJsonString(),
            })));
        }
        return resource.ToJsonString();
    }

    private static string? Named(JsonObject template, string logicalId, string type, string property) =>
        template["Resources"]![logicalId] is JsonObject found && found["Type"]!.GetValue<string>() == type
            ? found["Properties"]![property]!.GetValue<string>()
            : null;

    private static string Markers(string arn) => arn.Replace("123456789012", "ACCOUNT").Replace("ap-southeast-1", "REGION");
}
