using Amazon.CDK.Assertions;
using Xunit;

namespace ExtraSpace.Infra.Tests;

public class DataStackTests
{
    private static Template Data(string env) => TestStacks.For($"EsData-{env}");

    [Fact]
    public void Production_database_matches_cost_v4()
    {
        Data("production").HasResourceProperties("AWS::RDS::DBInstance", new Dictionary<string, object>
        {
            ["DBInstanceIdentifier"] = "es-production-postgres",
            ["DBInstanceClass"] = "db.m8g.large",
            ["Engine"] = "postgres",
            ["EngineVersion"] = "18.6",
            ["DBName"] = "extraspace",
            ["MultiAZ"] = true,
            ["StorageType"] = "gp3",
            ["AllocatedStorage"] = "50",
            ["MaxAllocatedStorage"] = 200,
            ["StorageEncrypted"] = true,
            ["PubliclyAccessible"] = false,
            ["BackupRetentionPeriod"] = 35,
            ["DeletionProtection"] = true,
            ["EnablePerformanceInsights"] = true,
            ["PerformanceInsightsRetentionPeriod"] = 465,
            ["DatabaseInsightsMode"] = "advanced",
            ["EnableCloudwatchLogsExports"] = new object[] { "postgresql" },
        });
    }

    [Fact]
    public void Staging_database_matches_cost_v4()
    {
        var template = Data("staging");

        template.HasResourceProperties("AWS::RDS::DBInstance", new Dictionary<string, object>
        {
            ["DBInstanceIdentifier"] = "es-staging-postgres",
            ["DBInstanceClass"] = "db.t4g.medium",
            ["EngineVersion"] = "18.6",
            ["MultiAZ"] = false,
            ["AllocatedStorage"] = "20",
            ["MaxAllocatedStorage"] = 50,
            ["StorageEncrypted"] = true,
            ["PubliclyAccessible"] = false,
            ["BackupRetentionPeriod"] = 7,
            ["DeletionProtection"] = false,
            ["EnablePerformanceInsights"] = true,
            ["PerformanceInsightsRetentionPeriod"] = 7,
        });
        template.HasResourceProperties("AWS::RDS::DBInstance", new Dictionary<string, object>
        {
            ["DBInstanceIdentifier"] = "es-staging-postgres",
            ["DatabaseInsightsMode"] = Match.Absent(),
        });
    }

    [Fact]
    public void Production_database_is_retained_and_protected()
    {
        Data("production").HasResource("AWS::RDS::DBInstance", new Dictionary<string, object>
        {
            ["DeletionPolicy"] = "Retain",
            ["UpdateReplacePolicy"] = "Retain",
        });
    }

    [Fact]
    public void Staging_database_leaves_a_snapshot_when_deleted()
    {
        Data("staging").HasResource("AWS::RDS::DBInstance", new Dictionary<string, object>
        {
            ["DeletionPolicy"] = "Snapshot",
            ["UpdateReplacePolicy"] = "Snapshot",
        });
    }

    [Theory]
    [InlineData("staging", 1)]
    [InlineData("production", 2)]
    public void Database_is_private_and_only_bastion_and_proxy_may_connect(string env, int expectedIngressRules)
    {
        var template = Data(env);

        // Only separate SG-to-SG rules on port 5432: bastion (and proxy in production). No CIDR rules anywhere.
        template.ResourceCountIs("AWS::EC2::SecurityGroupIngress", expectedIngressRules);
        template.ResourcePropertiesCountIs("AWS::EC2::SecurityGroupIngress", new Dictionary<string, object>
        {
            ["IpProtocol"] = "tcp",
            ["FromPort"] = 5432,
            ["ToPort"] = 5432,
            ["SourceSecurityGroupId"] = Match.AnyValue(),
        }, expectedIngressRules);
        template.HasResourceProperties("AWS::EC2::SecurityGroup", new Dictionary<string, object>
        {
            ["GroupDescription"] = $"ExtraSpace {env} PostgreSQL",
            ["SecurityGroupIngress"] = Match.Absent(),
        });
        template.HasResourceProperties("AWS::RDS::DBSubnetGroup", new Dictionary<string, object>
        {
            ["SubnetIds"] = Match.AnyValue(),
        });
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void Bastion_has_no_key_no_inbound_and_requires_imdsv2(string env)
    {
        var template = Data(env);

        template.HasResourceProperties("AWS::EC2::Instance", new Dictionary<string, object>
        {
            ["InstanceType"] = "t4g.nano",
            ["KeyName"] = Match.Absent(),
        });
        template.HasResourceProperties("AWS::EC2::SecurityGroup", new Dictionary<string, object>
        {
            ["GroupDescription"] = $"ExtraSpace {env} bastion, SSM only, no inbound",
            ["SecurityGroupIngress"] = Match.Absent(),
        });
        template.HasResourceProperties("AWS::EC2::LaunchTemplate", new Dictionary<string, object>
        {
            ["LaunchTemplateData"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["MetadataOptions"] = Match.ObjectLike(new Dictionary<string, object> { ["HttpTokens"] = "required" }),
            }),
        });
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void Secrets_are_named_per_environment(string env)
    {
        var template = Data(env);

        template.HasResourceProperties("AWS::SecretsManager::Secret", new Dictionary<string, object>
        {
            ["Name"] = $"es/{env}/db-master",
        });
        foreach (var (suffix, user) in new[] { ("app", "es_app"), ("readonly", "es_readonly") })
        {
            template.HasResourceProperties("AWS::SecretsManager::Secret", new Dictionary<string, object>
            {
                ["Name"] = $"es/{env}/db-{suffix}",
                ["GenerateSecretString"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["SecretStringTemplate"] = $$"""{"username":"{{user}}"}""",
                    ["GenerateStringKey"] = "password",
                    ["ExcludePunctuation"] = true,
                    ["PasswordLength"] = 32,
                }),
            });
        }
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void Master_password_is_never_in_the_template(string env)
    {
        // The password is a {{resolve:secretsmanager:...}} reference built with Fn::Join, never a literal string.
        Data(env).HasResourceProperties("AWS::RDS::DBInstance", new Dictionary<string, object>
        {
            ["MasterUserPassword"] = Match.ObjectLike(new Dictionary<string, object> { ["Fn::Join"] = Match.AnyValue() }),
        });
    }

    [Fact]
    public void Production_has_a_tls_only_proxy_and_staging_has_none()
    {
        Data("staging").ResourceCountIs("AWS::RDS::DBProxy", 0);

        var production = Data("production");
        production.ResourceCountIs("AWS::RDS::DBProxy", 1);
        production.HasResourceProperties("AWS::RDS::DBProxy", new Dictionary<string, object>
        {
            ["DBProxyName"] = "es-production-proxy",
            ["EngineFamily"] = "POSTGRESQL",
            ["RequireTLS"] = true,
        });
    }

    [Theory]
    [InlineData("staging", 14)]
    [InlineData("production", 90)]
    public void Parameters_force_tls_and_log_slow_queries_and_logs_expire(string env, int retentionDays)
    {
        var template = Data(env);

        template.HasResourceProperties("AWS::RDS::DBParameterGroup", new Dictionary<string, object>
        {
            ["Family"] = "postgres18",
            ["Parameters"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["rds.force_ssl"] = "1",
                ["log_min_duration_statement"] = "500",
                ["idle_in_transaction_session_timeout"] = "60000",
            }),
        });
        template.HasResourceProperties("AWS::Logs::LogGroup", new Dictionary<string, object>
        {
            ["LogGroupName"] = $"/aws/rds/instance/es-{env}-postgres/postgresql",
            ["RetentionInDays"] = retentionDays,
        });
        template.ResourceCountIs("AWS::Lambda::Function", 0);
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void Outputs_give_what_the_runbook_needs(string env)
    {
        var template = Data(env);

        template.HasOutput("DbEndpoint", Match.AnyValue());
        template.HasOutput("DbMasterSecretName", new Dictionary<string, object> { ["Value"] = $"es/{env}/db-master" });
        template.HasOutput("BastionInstanceId", Match.AnyValue());
    }

    [Fact]
    public void The_app_enters_through_the_proxy_in_production_and_directly_in_staging()
    {
        var staging = (ExtraSpace.Infra.Stacks.DataStack)TestStacks.Stack("EsData-staging");
        var production = (ExtraSpace.Infra.Stacks.DataStack)TestStacks.Stack("EsData-production");

        Assert.Null(staging.ProxySecurityGroup);
        Assert.Same(staging.DatabaseSecurityGroup, staging.AppEntrySecurityGroup);
        Assert.NotNull(production.ProxySecurityGroup);
        Assert.Same(production.ProxySecurityGroup, production.AppEntrySecurityGroup);
    }
}
