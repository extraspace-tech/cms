using Amazon.CDK.Assertions;
using Xunit;

namespace ExtraSpace.Infra.Tests;

public class ObservabilityTests
{
    private static Template App(string env) => TestStacks.For($"EsApp-{env}");

    [Theory]
    [InlineData("staging", "Trace")]
    [InlineData("staging", "Errors")]
    [InlineData("staging", "Slow")]
    [InlineData("staging", "Outside-call failures")]
    [InlineData("staging", "One user")]
    [InlineData("production", "Trace")]
    public void The_five_saved_queries_exist(string env, string name)
    {
        App(env).HasResourceProperties("AWS::Logs::QueryDefinition", new Dictionary<string, object>
        {
            ["Name"] = $"extraspace/{env}/{name}",
        });
        App(env).ResourceCountIs("AWS::Logs::QueryDefinition", 5);
    }

    [Fact]
    public void Trace_query_follows_a_request_into_its_jobs_across_both_log_groups()
    {
        var production = App("production");

        production.HasResourceProperties("AWS::Logs::QueryDefinition", new Dictionary<string, object>
        {
            ["Name"] = "extraspace/production/Trace",
            ["QueryString"] = Match.StringLikeRegexp("traceId = \"PASTE-ID\" or correlationId = \"PASTE-ID\""),
            ["LogGroupNames"] = new object[] { Match.AnyValue(), Match.AnyValue() },
        });
    }

    [Fact]
    public void Staging_trace_query_reads_its_one_log_group()
    {
        App("staging").HasResourceProperties("AWS::Logs::QueryDefinition", new Dictionary<string, object>
        {
            ["Name"] = "extraspace/staging/Trace",
            ["LogGroupNames"] = new object[] { Match.AnyValue() },
        });
    }

    private static string QueryString(string env, string name)
    {
        var query = App(env).FindResources("AWS::Logs::QueryDefinition", new Dictionary<string, object>
        {
            ["Properties"] = new Dictionary<string, object> { ["Name"] = $"extraspace/{env}/{name}" },
        }).Values.Single();
        return (string)((IDictionary<string, object>)((IDictionary<string, object>)query)["Properties"])["QueryString"];
    }

    private static string OnlyId(string env, string type) => App(env).FindResources(type).Keys.Single();

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void Errors_query_shows_the_lines_the_error_alarm_counts(string env)
    {
        // The query a person opens after the error-lines alarm must list what the ErrorLines metric counts:
        // Error and Fatal, without the request summary line, with or without an exception.
        var query = QueryString(env, "Errors");

        Assert.StartsWith(
            "filter level in [\"Error\", \"Fatal\"] and messageTemplate != \"HTTP {method} {route} responded {status} in {elapsedMs} ms\"\n", query);
        Assert.DoesNotContain("ispresent(exception)", query);
    }

    [Fact]
    public void Errors_query_groups_by_plain_fields_only()
    {
        // Logs Insights may refuse a function inside "by"; nobody would notice until an incident.
        var query = QueryString("staging", "Errors");
        var origin = query.IndexOf("| fields coalesce(route, jobName, \"-\") as origin", StringComparison.Ordinal);
        var stats = query.IndexOf("| stats ", StringComparison.Ordinal);

        Assert.True(origin >= 0 && stats > origin, query);
        var by = query[(query.IndexOf(" by ", stats, StringComparison.Ordinal) + 4)..].Split('\n')[0];
        Assert.Equal("origin, exceptionType", by);
        Assert.DoesNotContain("(", by);
    }

    [Theory]
    [InlineData("Outside-call failures", "filter ispresent(integration) and outcome != \"ok\"")]
    [InlineData("One user", "filter customerId = \"PASTE-ID\" or staffId = \"PASTE-ID\"")]
    public void Queries_keep_their_key_clause(string name, string clause) => Assert.Contains(clause, QueryString("staging", name));

    [Theory]
    [InlineData("staging", "5xx", "HTTPCode_Target_5XX_Count")]
    [InlineData("production", "5xx", "HTTPCode_Target_5XX_Count")]
    // The load balancer's own 502/503/504: requests that hang past the idle timeout never reach a target-side metric.
    [InlineData("staging", "elb-5xx", "HTTPCode_ELB_5XX_Count")]
    [InlineData("production", "elb-5xx", "HTTPCode_ELB_5XX_Count")]
    public void Five_xx_alarms_watch_this_load_balancer(string env, string name, string metric)
    {
        App(env).HasResourceProperties("AWS::CloudWatch::Alarm", new Dictionary<string, object>
        {
            ["AlarmName"] = $"es-{env}-{name}",
            ["Namespace"] = "AWS/ApplicationELB",
            ["MetricName"] = metric,
            ["Statistic"] = "Sum",
            ["Period"] = 300,
            ["EvaluationPeriods"] = 1,
            ["Threshold"] = 5,
            ["ComparisonOperator"] = "GreaterThanOrEqualToThreshold",
            ["TreatMissingData"] = "notBreaching",
            ["Dimensions"] = new object[] { LoadBalancerDimension(env) },
        });
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void Unhealthy_targets_alarm_watches_this_target_group(string env)
    {
        App(env).HasResourceProperties("AWS::CloudWatch::Alarm", new Dictionary<string, object>
        {
            ["AlarmName"] = $"es-{env}-unhealthy-targets",
            ["Namespace"] = "AWS/ApplicationELB",
            ["MetricName"] = "UnHealthyHostCount",
            ["Statistic"] = "Maximum",
            ["Period"] = 60,
            ["EvaluationPeriods"] = 3,
            ["Threshold"] = 1,
            ["ComparisonOperator"] = "GreaterThanOrEqualToThreshold",
            ["TreatMissingData"] = "notBreaching",
            ["Dimensions"] = new object[]
            {
                LoadBalancerDimension(env),
                new Dictionary<string, object>
                {
                    ["Name"] = "TargetGroup",
                    ["Value"] = new Dictionary<string, object>
                    {
                        ["Fn::GetAtt"] = new object[] { OnlyId(env, "AWS::ElasticLoadBalancingV2::TargetGroup"), "TargetGroupFullName" },
                    },
                },
            },
        });
    }

    private static Dictionary<string, object> LoadBalancerDimension(string env) => new()
    {
        ["Name"] = "LoadBalancer",
        ["Value"] = new Dictionary<string, object>
        {
            ["Fn::GetAtt"] = new object[] { OnlyId(env, "AWS::ElasticLoadBalancingV2::LoadBalancer"), "LoadBalancerFullName" },
        },
    };

    [Fact]
    public void Slow_query_leaves_static_files_out()
    {
        App("staging").HasResourceProperties("AWS::Logs::QueryDefinition", new Dictionary<string, object>
        {
            ["Name"] = "extraspace/staging/Slow",
            ["QueryString"] = Match.StringLikeRegexp(@"route != ""\(unmatched\)"""),
        });
    }

    [Theory]
    // Fatal too: a host that fails to start (the migration guard) logs at Fatal, not Error.
    [InlineData("ErrorLines", "{ (($.level = \"Error\") || ($.level = \"Fatal\")) && ($.messageTemplate != \"HTTP {method} {route} responded {status} in {elapsedMs} ms\") }")]
    [InlineData("JobsFailedPermanently", "{ $.messageTemplate = \"Job {jobName} failed permanently\" }")]
    [InlineData("OutsideCallFailures", "{ ($.integration = \"*\") && ($.outcome != \"ok\") }")]
    [InlineData("StaffCodesLocked", "{ ($.messageTemplate = \"Login code {codeId} locked after too many wrong attempts\") && ($.purpose = \"Staff\") }")]
    public void Metric_filters_read_our_log_fields(string metric, string pattern)
    {
        App("staging").HasResourceProperties("AWS::Logs::MetricFilter", new Dictionary<string, object>
        {
            ["FilterPattern"] = pattern,
            ["MetricTransformations"] = new object[]
            {
                Match.ObjectLike(new Dictionary<string, object>
                {
                    ["MetricNamespace"] = "ExtraSpace/staging",
                    ["MetricName"] = metric,
                    ["MetricValue"] = "1",
                }),
            },
        });
    }

    [Fact]
    public void Production_filters_both_log_groups()
    {
        App("staging").ResourceCountIs("AWS::Logs::MetricFilter", 4);
        App("production").ResourceCountIs("AWS::Logs::MetricFilter", 8);
    }

    [Theory]
    [InlineData("staging", "5xx")]
    [InlineData("staging", "elb-5xx")]
    [InlineData("production", "elb-5xx")]
    [InlineData("staging", "unhealthy-targets")]
    [InlineData("staging", "no-running-task-api")]
    [InlineData("staging", "error-lines")]
    [InlineData("staging", "job-failed-permanently")]
    [InlineData("staging", "outside-call-failures")]
    [InlineData("staging", "staff-codes-locked")]
    [InlineData("staging", "db-cpu")]
    [InlineData("staging", "db-connections")]
    [InlineData("staging", "db-free-storage")]
    [InlineData("production", "no-running-task-jobs")]
    public void Every_alarm_exists_and_notifies_the_topic(string env, string name)
    {
        App(env).HasResourceProperties("AWS::CloudWatch::Alarm", new Dictionary<string, object>
        {
            ["AlarmName"] = $"es-{env}-{name}",
            ["AlarmActions"] = Match.AnyValue(),
        });
    }

    [Theory]
    [InlineData("staging", 11)]
    [InlineData("production", 12)]
    public void Alarm_count_is_known(string env, int expected) => App(env).ResourceCountIs("AWS::CloudWatch::Alarm", expected);

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void A_missing_task_count_counts_as_down(string env)
    {
        // No tasks means no datapoints. Treated as "not breaching", the alarm that watches for exactly this would stay green.
        App(env).HasResourceProperties("AWS::CloudWatch::Alarm", new Dictionary<string, object>
        {
            ["AlarmName"] = $"es-{env}-no-running-task-api",
            ["MetricName"] = "RunningTaskCount",
            ["Namespace"] = "ECS/ContainerInsights",
            ["ComparisonOperator"] = "LessThanThreshold",
            ["Threshold"] = 1,
            ["TreatMissingData"] = "breaching",
        });
    }

    [Theory]
    [InlineData("staging", 150)]
    [InlineData("production", 300)]
    public void Database_alarms_name_the_instance(string env, int connections)
    {
        App(env).HasResourceProperties("AWS::CloudWatch::Alarm", new Dictionary<string, object>
        {
            ["AlarmName"] = $"es-{env}-db-connections",
            ["Threshold"] = connections,
            ["Dimensions"] = new object[] { new Dictionary<string, object> { ["Name"] = "DBInstanceIdentifier", ["Value"] = $"es-{env}-postgres" } },
        });
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void Alarms_go_to_one_topic_with_an_email_subscription(string env)
    {
        var template = App(env);

        template.HasResourceProperties("AWS::SNS::Topic", new Dictionary<string, object> { ["TopicName"] = $"es-{env}-alarms" });
        template.HasResourceProperties("AWS::SNS::Subscription", new Dictionary<string, object>
        {
            ["Protocol"] = "email",
            ["Endpoint"] = "alarms@example.test",
        });
    }
}
