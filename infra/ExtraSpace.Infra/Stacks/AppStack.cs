using Amazon.CDK;
using Amazon.CDK.AWS.CertificateManager;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECR;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.Route53;
using Amazon.CDK.AWS.Route53.Targets;
using Amazon.CDK.AWS.SSM;
using Constructs;
using EcsSecret = Amazon.CDK.AWS.ECS.Secret;
using HealthCheck = Amazon.CDK.AWS.ElasticLoadBalancingV2.HealthCheck;
using Secret = Amazon.CDK.AWS.SecretsManager.Secret;
using SecretProps = Amazon.CDK.AWS.SecretsManager.SecretProps;
using SecretStringGenerator = Amazon.CDK.AWS.SecretsManager.SecretStringGenerator;

namespace ExtraSpace.Infra.Stacks;

public sealed class AppStackProps : StackProps
{
    public required EnvConfig Config { get; init; }
    public required DeployInputs Inputs { get; init; }
    public required IVpc Vpc { get; init; }
    public required DataStack Data { get; init; }
    public required ICertificate Certificate { get; init; }
}

public sealed class AppStack : Stack
{
    private const int ContainerPort = 8080;
    private const int PostgresPort = 5432;

    public ApplicationLoadBalancer LoadBalancer { get; }
    public ApplicationTargetGroup TargetGroup { get; }
    public IReadOnlyList<LogGroup> LogGroups { get; }
    public IReadOnlyList<string> ServiceNames { get; }

    public AppStack(Construct scope, string id, AppStackProps props) : base(scope, id, props)
    {
        var config = props.Config;
        var inputs = props.Inputs;
        var env = config.Name;
        var removal = config.IsProduction ? RemovalPolicy.RETAIN : RemovalPolicy.DESTROY;
        var retention = config.LogRetentionDays switch
        {
            14 => RetentionDays.TWO_WEEKS,
            90 => RetentionDays.THREE_MONTHS,
            _ => throw new InvalidOperationException($"Log retention of {config.LogRetentionDays} days ({env}) has no mapping in AppStack."),
        };
        // A service that scales owns its own count: a fixed one would cut it back on every deploy.
        var apiScales = config.ApiMaxCount > config.ApiCount;

        // CI writes the tag after each roll-out; an SSM-typed stack parameter reads it at deploy time (no lookup at synth).
        var imageTag = StringParameter.ValueForStringParameter(this, CiStack.ImageTagParameter(env));
        var repository = Repository.FromRepositoryName(this, "Images", CiStack.RepositoryName);

        var albSecurityGroup = new SecurityGroup(this, "AlbSg", new SecurityGroupProps
        {
            Vpc = props.Vpc,
            Description = $"ExtraSpace {env} load balancer",
            AllowAllOutbound = false,
        });
        albSecurityGroup.AddIngressRule(Peer.AnyIpv4(), Port.Tcp(443), "HTTPS");
        albSecurityGroup.AddIngressRule(Peer.AnyIpv4(), Port.Tcp(80), "HTTP, redirected to HTTPS");

        // No inbound rule here: the only one is added below by the listener, from the load balancer.
        // The app trusts X-Forwarded-For from any caller, so nothing else may ever reach a task.
        var taskSecurityGroup = new SecurityGroup(this, "TaskSg", new SecurityGroupProps
        {
            Vpc = props.Vpc,
            Description = $"ExtraSpace {env} app tasks",
            AllowAllOutbound = true,
        });

        // In this stack, not the data stack: the data stack must not depend on the app.
        _ = new CfnSecurityGroupIngress(this, "DbFromTasks", new CfnSecurityGroupIngressProps
        {
            GroupId = props.Data.AppEntrySecurityGroup.SecurityGroupId,
            SourceSecurityGroupId = taskSecurityGroup.SecurityGroupId,
            IpProtocol = "tcp",
            FromPort = PostgresPort,
            ToPort = PostgresPort,
            Description = "app tasks to PostgreSQL",
        });

        var signingKey = new Secret(this, "CustomerSigningKey", new SecretProps
        {
            SecretName = $"es/{env}/customer-signing-key",
            Description = $"JWT signing key for customer access tokens ({env})",
            // 64 letters and digits are valid base64 for 48 bytes: what CustomerAuth:SigningKey expects.
            GenerateSecretString = new SecretStringGenerator { PasswordLength = 64, ExcludePunctuation = true },
            RemovalPolicy = removal,
        });

        var cluster = new Cluster(this, "Cluster", new ClusterProps
        {
            ClusterName = CiStack.ClusterName(env),
            Vpc = props.Vpc,
            ContainerInsightsV2 = ContainerInsights.ENABLED,
        });

        // Fixed names: the CI deploy role may pass exactly these two roles.
        var executionRole = new Role(this, "ExecutionRole", new RoleProps
        {
            RoleName = CiStack.ExecutionRoleName(env),
            AssumedBy = new ServicePrincipal("ecs-tasks.amazonaws.com"),
        });
        var taskRole = new Role(this, "TaskRole", new RoleProps
        {
            RoleName = CiStack.TaskRoleName(env),
            AssumedBy = new ServicePrincipal("ecs-tasks.amazonaws.com"),
        });
        var keyRing = $"es/{env}/data-protection";
        taskRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = new[] { "ssm:GetParametersByPath", "ssm:PutParameter" },
            Resources = new[]
            {
                FormatArn(new ArnComponents { Service = "ssm", Resource = "parameter", ResourceName = keyRing }),
                FormatArn(new ArnComponents { Service = "ssm", Resource = "parameter", ResourceName = $"{keyRing}/*" }),
            },
        }));
        // identity/*: while SES is in the sandbox it also checks the recipient's identity. The sender is pinned instead.
        taskRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = new[] { "ses:SendEmail" },
            Resources = new[] { FormatArn(new ArnComponents { Service = "ses", Resource = "identity", ResourceName = "*" }) },
            Conditions = new Dictionary<string, object>
            {
                ["StringEquals"] = new Dictionary<string, object> { ["ses:FromAddress"] = inputs.EmailFromAddress },
            },
        }));

        var settings = new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = config.AspNetEnvironment,
            ["App__Version"] = imageTag,
            ["App__MinVersion__ios"] = "0.0.0",
            ["App__MinVersion__android"] = "0.0.0",
            ["App__MinVersion__web"] = "0.0.0",
            ["Database__Host"] = props.Data.AppHost,
            ["Database__Name"] = DataStack.DatabaseName,
            ["DataProtection__SsmPath"] = $"/{keyRing}/",
            ["Email__From"] = inputs.EmailFrom,
        };
        AddList(settings, "Admin__AllowedEmailDomains", inputs.AdminEmailDomains);
        AddList(settings, "Admin__BootstrapEmails", inputs.BootstrapEmails);
        AddList(settings, "Cors__AllowedOrigins", inputs.CorsOrigins);
        if (!config.IsProduction) AddList(settings, "Email__AllowedRecipients", inputs.EmailAllowedRecipients);

        var secrets = new Dictionary<string, EcsSecret>
        {
            ["CustomerAuth__SigningKey"] = EcsSecret.FromSecretsManager(signingKey),
            ["Database__Password"] = EcsSecret.FromSecretsManager(props.Data.AppUserSecret, "password"),
            ["Database__Username"] = EcsSecret.FromSecretsManager(props.Data.AppUserSecret, "username"),
        };

        var logGroups = new List<LogGroup>();
        var serviceNames = new List<string>();

        FargateService Service(string name, string role, int cpu, int memory, int? count, bool web)
        {
            var logGroup = new LogGroup(this, $"{name}Logs", new LogGroupProps
            {
                LogGroupName = $"/extraspace/{env}/{name}",
                Retention = retention,
                RemovalPolicy = removal,
            });
            logGroups.Add(logGroup);

            var definition = new FargateTaskDefinition(this, $"{name}Task", new FargateTaskDefinitionProps
            {
                Family = $"es-{env}-{name}",
                Cpu = cpu,
                MemoryLimitMiB = memory,
                RuntimePlatform = new RuntimePlatform
                {
                    CpuArchitecture = CpuArchitecture.ARM64,
                    OperatingSystemFamily = OperatingSystemFamily.LINUX,
                },
                TaskRole = taskRole,
                ExecutionRole = executionRole,
            });
            definition.AddContainer("app", new ContainerDefinitionOptions
            {
                Image = ContainerImage.FromEcrRepository(repository, imageTag),
                Environment = Sorted(new Dictionary<string, string>(settings) { ["App__Role"] = role }),
                Secrets = Sorted(secrets),
                // dotnet as PID 1 ignores the runtime's abort signal and can hang instead of exiting; an init process passes it on.
                LinuxParameters = new LinuxParameters(this, $"{name}Linux", new LinuxParametersProps { InitProcessEnabled = true }),
                Logging = LogDriver.AwsLogs(new AwsLogDriverProps { LogGroup = logGroup, StreamPrefix = name }),
                PortMappings = web ? new IPortMapping[] { new PortMapping { ContainerPort = ContainerPort } } : null,
            });

            var service = new FargateService(this, $"{name}Service", new FargateServiceProps
            {
                ServiceName = $"es-{env}-{name}",
                Cluster = cluster,
                TaskDefinition = definition,
                DesiredCount = count,
                VpcSubnets = new SubnetSelection { SubnetGroupName = "app" },
                SecurityGroups = new ISecurityGroup[] { taskSecurityGroup },
                AssignPublicIp = false,
                CircuitBreaker = new DeploymentCircuitBreaker { Enable = true, Rollback = true },
                MinHealthyPercent = 100,
                MaxHealthyPercent = 200,
                HealthCheckGracePeriod = web ? Duration.Seconds(60) : null,
                PropagateTags = PropagatedTagSource.SERVICE,
            });
            serviceNames.Add(name);
            return service;
        }

        var api = Service("api", config.SeparateJobs ? "Api" : "All", config.ApiCpu, config.ApiMemoryMiB, apiScales ? null : config.ApiCount, web: true);
        if (config.SeparateJobs)
            Service("jobs", "Jobs", config.JobsCpu, config.JobsMemoryMiB, 1, web: false);

        LoadBalancer = new ApplicationLoadBalancer(this, "Alb", new ApplicationLoadBalancerProps
        {
            LoadBalancerName = $"es-{env}",
            Vpc = props.Vpc,
            InternetFacing = true,
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PUBLIC },
            SecurityGroup = albSecurityGroup,
            IdleTimeout = Duration.Seconds(60),
            DeletionProtection = config.IsProduction,
        });
        // The per-IP limits read the last X-Forwarded-For hop. Stated, so a change of the AWS default cannot move it.
        LoadBalancer.SetAttribute("routing.http.xff_header_processing.mode", "append");
        LoadBalancer.SetAttribute("routing.http.drop_invalid_header_fields.enabled", "true");

        var https = LoadBalancer.AddListener("Https", new BaseApplicationListenerProps
        {
            Port = 443,
            Protocol = ApplicationProtocol.HTTPS,
            Certificates = new IListenerCertificate[] { ListenerCertificate.FromCertificateManager(props.Certificate) },
            SslPolicy = SslPolicy.RECOMMENDED_TLS,
            Open = false,
        });
        TargetGroup = https.AddTargets("Api", new AddApplicationTargetsProps
        {
            Port = ContainerPort,
            Protocol = ApplicationProtocol.HTTP,
            Targets = new IApplicationLoadBalancerTarget[] { api },
            DeregistrationDelay = Duration.Seconds(30),
            HealthCheck = new HealthCheck
            {
                Path = "/health/live",
                HealthyHttpCodes = "200",
                Interval = Duration.Seconds(15),
                Timeout = Duration.Seconds(5),
                HealthyThresholdCount = 2,
                UnhealthyThresholdCount = 3,
            },
        });
        LoadBalancer.AddRedirect(new ApplicationLoadBalancerRedirectConfig { SourcePort = 80, TargetPort = 443, Open = false });

        if (apiScales)
        {
            var scaling = api.AutoScaleTaskCount(new Amazon.CDK.AWS.ApplicationAutoScaling.EnableScalingProps
            {
                MinCapacity = config.ApiCount,
                MaxCapacity = config.ApiMaxCount,
            });
            scaling.ScaleOnCpuUtilization("Cpu", new CpuUtilizationScalingProps { TargetUtilizationPercent = 60 });
            scaling.ScaleOnRequestCount("Requests", new RequestCountScalingProps { RequestsPerTarget = 1000, TargetGroup = TargetGroup });
        }

        var zone = HostedZone.FromHostedZoneAttributes(this, "Zone", new HostedZoneAttributes
        {
            HostedZoneId = inputs.ZoneId,
            ZoneName = inputs.ZoneName,
        });
        _ = new ARecord(this, "HostRecord", new ARecordProps
        {
            Zone = zone,
            RecordName = inputs.Host + ".",
            Target = RecordTarget.FromAlias(new LoadBalancerTarget(LoadBalancer)),
        });

        LogGroups = logGroups;
        ServiceNames = serviceNames;

        _ = new WebFirewall(this, "Waf", env, LoadBalancer.LoadBalancerArn);
        _ = new Observability(this, "Observability", new ObservabilityProps
        {
            Config = config,
            AlarmEmail = inputs.AlarmEmail,
            LoadBalancer = LoadBalancer,
            TargetGroup = TargetGroup,
            LogGroups = LogGroups,
            ServiceNames = ServiceNames,
        });

        _ = new CfnOutput(this, "Url", new CfnOutputProps { Value = $"https://{inputs.Host}" });
        _ = new CfnOutput(this, "ClusterName", new CfnOutputProps { Value = cluster.ClusterName });
    }

    private static void AddList(Dictionary<string, string> settings, string key, string[] values)
    {
        for (var i = 0; i < values.Length; i++) settings[$"{key}__{i}"] = values[i];
    }

    // CDK writes these in insertion order. The tests match them in order, and a stable order keeps template diffs small.
    private static Dictionary<string, T> Sorted<T>(Dictionary<string, T> values) =>
        new(values.OrderBy(pair => pair.Key, StringComparer.Ordinal));
}
