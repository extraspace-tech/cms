using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.RDS;
using Amazon.CDK.AWS.SecretsManager;
using Constructs;
using InstanceType = Amazon.CDK.AWS.EC2.InstanceType;

namespace ExtraSpace.Infra.Stacks;

public sealed class DataStackProps : StackProps
{
    public required EnvConfig Config { get; init; }
    public required IVpc Vpc { get; init; }
}

public sealed class DataStack : Stack
{
    public const string DatabaseName = "extraspace";
    public const string MasterUser = "postgres";
    private const string PostgresFullVersion = "18.6";
    private const string PostgresMajorVersion = "18";
    private const int PostgresPort = 5432;

    public DatabaseInstance Database { get; }
    public ISecurityGroup DatabaseSecurityGroup { get; }
    public ISecret AppUserSecret { get; }
    public ISecret ReadOnlyUserSecret { get; }
    public DatabaseProxy? Proxy { get; }
    public BastionHostLinux Bastion { get; }

    public DataStack(Construct scope, string id, DataStackProps props) : base(scope, id, props)
    {
        var config = props.Config;
        var vpc = props.Vpc;
        var identifier = $"es-{config.Name}-postgres";
        var removal = config.IsProduction ? RemovalPolicy.RETAIN : RemovalPolicy.DESTROY;

        DatabaseSecurityGroup = new SecurityGroup(this, "DbSg", new SecurityGroupProps
        {
            Vpc = vpc,
            Description = $"ExtraSpace {config.Name} PostgreSQL",
            AllowAllOutbound = false,
        });

        var bastionSecurityGroup = new SecurityGroup(this, "BastionSg", new SecurityGroupProps
        {
            Vpc = vpc,
            Description = $"ExtraSpace {config.Name} bastion, SSM only, no inbound",
            AllowAllOutbound = true,
        });

        // Reached only through SSM Session Manager port forwarding (aws-setup-guide B5); no SSH key, no inbound port.
        Bastion = new BastionHostLinux(this, "Bastion", new BastionHostLinuxProps
        {
            Vpc = vpc,
            InstanceName = $"es-{config.Name}-bastion",
            InstanceType = new InstanceType("t4g.nano"),
            MachineImage = MachineImage.LatestAmazonLinux2023(new AmazonLinux2023ImageSsmParameterProps
            {
                CpuType = AmazonLinuxCpuType.ARM_64,
            }),
            SecurityGroup = bastionSecurityGroup,
            SubnetSelection = new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS },
            RequireImdsv2 = true,
            BlockDevices = new IBlockDevice[]
            {
                new BlockDevice
                {
                    DeviceName = "/dev/xvda",
                    Volume = BlockDeviceVolume.Ebs(8, new EbsDeviceOptions
                    {
                        Encrypted = true,
                        VolumeType = EbsDeviceVolumeType.GP3,
                    }),
                },
            },
        });

        var engine = DatabaseInstanceEngine.Postgres(new PostgresInstanceEngineProps
        {
            Version = PostgresEngineVersion.Of(PostgresFullVersion, PostgresMajorVersion),
        });

        var parameters = new ParameterGroup(this, "DbParams", new ParameterGroupProps
        {
            Engine = engine,
            Description = $"ExtraSpace {config.Name} PostgreSQL parameters",
            Parameters = new Dictionary<string, string>
            {
                ["rds.force_ssl"] = "1",
                ["log_min_duration_statement"] = "500",
                ["idle_in_transaction_session_timeout"] = "60000",
            },
        });

        // Created here, with retention, so RDS does not create an unmanaged group and CDK needs no log-retention Lambda.
        var logGroup = new LogGroup(this, "DbLogs", new LogGroupProps
        {
            LogGroupName = $"/aws/rds/instance/{identifier}/postgresql",
            Retention = config.IsProduction ? RetentionDays.THREE_MONTHS : RetentionDays.TWO_WEEKS,
            RemovalPolicy = RemovalPolicy.DESTROY,
        });

        Database = new DatabaseInstance(this, "Postgres", new DatabaseInstanceProps
        {
            InstanceIdentifier = identifier,
            Engine = engine,
            InstanceType = new InstanceType(config.DbInstanceType),
            Port = PostgresPort,
            Vpc = vpc,
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_ISOLATED },
            SecurityGroups = new[] { DatabaseSecurityGroup },
            Credentials = Credentials.FromGeneratedSecret(MasterUser, new CredentialsBaseOptions
            {
                SecretName = $"es/{config.Name}/db-master",
            }),
            DatabaseName = DatabaseName,
            MultiAz = config.DbMultiAz,
            AllocatedStorage = config.DbAllocatedStorageGb,
            MaxAllocatedStorage = config.DbMaxAllocatedStorageGb,
            StorageType = StorageType.GP3,
            StorageEncrypted = true,
            PubliclyAccessible = false,
            BackupRetention = Duration.Days(config.DbBackupRetentionDays),
            PreferredBackupWindow = "18:00-19:00",               // 02:00–03:00 Singapore time
            PreferredMaintenanceWindow = "sun:19:30-sun:20:30",  // Monday 03:30–04:30 Singapore time
            DeletionProtection = config.IsProduction,
            RemovalPolicy = config.IsProduction ? RemovalPolicy.RETAIN : RemovalPolicy.SNAPSHOT,
            ParameterGroup = parameters,
            AutoMinorVersionUpgrade = true,
            EnablePerformanceInsights = true,
            PerformanceInsightRetention = config.DbAdvancedInsights
                ? PerformanceInsightRetention.MONTHS_15
                : PerformanceInsightRetention.DEFAULT,
            CloudwatchLogsExports = new[] { "postgresql" },
            CopyTagsToSnapshot = true,
        });
        Database.Node.AddDependency(logGroup);

        if (config.DbAdvancedInsights)
        {
            // shortcut: set through the CloudFormation property because the L2 construct may not expose it; verify at first synth.
            ((CfnDBInstance)Database.Node.DefaultChild!).AddPropertyOverride("DatabaseInsightsMode", "advanced");
        }

        AppUserSecret = UserSecret("DbAppUser", config, "es_app", "app", removal);
        ReadOnlyUserSecret = UserSecret("DbReadOnlyUser", config, "es_readonly", "readonly", removal);

        Database.Connections.AllowDefaultPortFrom(Bastion, "bastion psql tunnel");

        if (config.DbProxy)
        {
            var proxySecurityGroup = new SecurityGroup(this, "ProxySg", new SecurityGroupProps
            {
                Vpc = vpc,
                Description = $"ExtraSpace {config.Name} RDS Proxy",
                AllowAllOutbound = false,
            });

            Proxy = Database.AddProxy("Proxy", new DatabaseProxyOptions
            {
                DbProxyName = $"es-{config.Name}-proxy",
                Secrets = new[] { AppUserSecret },
                Vpc = vpc,
                VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_ISOLATED },
                SecurityGroups = new ISecurityGroup[] { proxySecurityGroup },
                RequireTLS = true,
                IamAuth = false,
            });

            Database.Connections.AllowDefaultPortFrom(proxySecurityGroup, "RDS Proxy to PostgreSQL");
            _ = new CfnOutput(this, "ProxyEndpoint", new CfnOutputProps { Value = Proxy.Endpoint });
        }

        // CDK writes these rules with the Endpoint.Port token; pin them to the fixed port so the template states 5432.
        foreach (var rule in DatabaseSecurityGroup.Node.Children.OfType<CfnSecurityGroupIngress>())
        {
            rule.FromPort = PostgresPort;
            rule.ToPort = PostgresPort;
        }

        _ = new CfnOutput(this, "DbEndpoint", new CfnOutputProps { Value = Database.DbInstanceEndpointAddress });
        _ = new CfnOutput(this, "DbMasterSecretName", new CfnOutputProps { Value = $"es/{config.Name}/db-master" });
        _ = new CfnOutput(this, "BastionInstanceId", new CfnOutputProps { Value = Bastion.InstanceId });
    }

    // The PostgreSQL role itself is created by hand with this password (aws-setup-guide B5).
    private Secret UserSecret(string id, EnvConfig config, string username, string suffix, RemovalPolicy removal) =>
        new(this, id, new SecretProps
        {
            SecretName = $"es/{config.Name}/db-{suffix}",
            Description = $"PostgreSQL user {username} ({config.Name})",
            GenerateSecretString = new SecretStringGenerator
            {
                SecretStringTemplate = $$"""{"username":"{{username}}"}""",
                GenerateStringKey = "password",
                ExcludePunctuation = true,
                PasswordLength = 32,
            },
            RemovalPolicy = removal,
        });
}
