using Amazon.CDK;
using Amazon.CDK.AWS.CloudWatch;
using Amazon.CDK.AWS.CloudWatch.Actions;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.SNS;
using Amazon.CDK.AWS.SNS.Subscriptions;
using Constructs;

namespace ExtraSpace.Infra.Stacks;

internal sealed class ObservabilityProps
{
    public required EnvConfig Config { get; init; }
    public required string AlarmEmail { get; init; }
    public required ApplicationLoadBalancer LoadBalancer { get; init; }
    public required ApplicationTargetGroup TargetGroup { get; init; }
    public required IReadOnlyList<LogGroup> LogGroups { get; init; }
    public required IReadOnlyList<string> ServiceNames { get; init; }
}

/// <summary>The saved log queries, metric filters and alarms of S0 spec §4.4 and §9.2.</summary>
internal sealed class Observability : Construct
{
    private const string SummaryTemplate = "HTTP {method} {route} responded {status} in {elapsedMs} ms";
    private readonly string _env;
    private readonly string _namespace;
    private readonly Topic _topic;

    public Observability(Construct scope, string id, ObservabilityProps props) : base(scope, id)
    {
        _env = props.Config.Name;
        _namespace = $"ExtraSpace/{_env}";
        _topic = new Topic(this, "Alarms", new TopicProps { TopicName = $"es-{_env}-alarms" });
        _topic.AddSubscription(new EmailSubscription(props.AlarmEmail));

        var groupNames = props.LogGroups.Select(g => g.LogGroupName).ToArray();
        Query("Trace", groupNames,
            "fields @timestamp, role, level, message, exception\n" +
            "| filter traceId = \"PASTE-ID\" or correlationId = \"PASTE-ID\"\n" +
            "| sort @timestamp asc\n| limit 1000");
        Query("Errors", groupNames,
            // Lists exactly the lines the ErrorLines metric counts. exceptionType is empty for a line without an exception.
            $"filter level in [\"Error\", \"Fatal\"] and messageTemplate != \"{SummaryTemplate}\"\n" +
            "| parse exception /^(?<exceptionType>[\\w\\.]+)/\n" +
            "| fields coalesce(route, jobName, \"-\") as origin\n" +
            "| stats count(*) as errors by origin, exceptionType\n" +
            "| sort errors desc");
        // Static files answer before routing and log route "(unmatched)"; they would drown the list.
        Query("Slow", groupNames,
            "fields @timestamp, route, status, elapsedMs, message, traceId\n" +
            $"| filter (messageTemplate = \"{SummaryTemplate}\" and elapsedMs > 1000 and route != \"(unmatched)\")\n" +
            "    or messageTemplate like /^Slow database command/\n" +
            "| sort elapsedMs desc\n| limit 200");
        Query("Outside-call failures", groupNames,
            "filter ispresent(integration) and outcome != \"ok\"\n" +
            "| stats count(*) as failures by integration, operation, outcome\n" +
            "| sort failures desc");
        Query("One user", groupNames,
            "fields @timestamp, level, method, route, status, message, traceId\n" +
            "| filter customerId = \"PASTE-ID\" or staffId = \"PASTE-ID\"\n" +
            "| sort @timestamp desc\n| limit 500");

        var errorLines = LogMetric(props.LogGroups, "ErrorLines",
            $"{{ (($.level = \"Error\") || ($.level = \"Fatal\")) && ($.messageTemplate != \"{SummaryTemplate}\") }}");
        var jobsFailed = LogMetric(props.LogGroups, "JobsFailedPermanently",
            "{ $.messageTemplate = \"Job {jobName} failed permanently\" }");
        var outsideFailures = LogMetric(props.LogGroups, "OutsideCallFailures",
            "{ ($.integration = \"*\") && ($.outcome != \"ok\") }");
        var outboxFailed = LogMetric(props.LogGroups, "OutboxFailedPermanently",
            "{ $.messageTemplate = \"Outbox message {outboxId} of kind {kind} failed permanently after {attempt} attempts\" }");
        var siteLinkMonthly = LogMetric(props.LogGroups, "SiteLinkMonthlyCalls",
            "{ $.messageTemplate = \"SiteLink calls this month reached {total}, past the alarm level {alarm}\" }");
        var staffLocked = LogMetric(props.LogGroups, "StaffCodesLocked",
            "{ ($.messageTemplate = \"Login code {codeId} locked after too many wrong attempts\") && ($.purpose = \"Staff\") }");

        var albDimensions = new Dictionary<string, object> { ["LoadBalancer"] = props.LoadBalancer.LoadBalancerFullName };
        Alarm("5xx", Metric("AWS/ApplicationELB", "HTTPCode_Target_5XX_Count", albDimensions, "Sum", 5), 5, 1,
            ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD, "5 or more 5xx answers from the app in 5 minutes");
        // Requests that hang past the idle timeout get a 504 from the load balancer; no target-side metric shows them.
        Alarm("elb-5xx", Metric("AWS/ApplicationELB", "HTTPCode_ELB_5XX_Count", albDimensions, "Sum", 5), 5, 1,
            ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD, "5 or more 5xx answers made by the load balancer itself (502/503/504) in 5 minutes");
        Alarm("unhealthy-targets", Metric("AWS/ApplicationELB", "UnHealthyHostCount", new Dictionary<string, object>
            {
                ["LoadBalancer"] = props.LoadBalancer.LoadBalancerFullName,
                ["TargetGroup"] = props.TargetGroup.TargetGroupFullName,
            }, "Maximum", 1), 1, 3,
            ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD, "a task failed its health check for 3 minutes");

        foreach (var name in props.ServiceNames)
        {
            // A start-up crash can leave no JSON log line at all: the task count still shows it.
            Alarm($"no-running-task-{name}", Metric("ECS/ContainerInsights", "RunningTaskCount", new Dictionary<string, object>
                {
                    ["ClusterName"] = CiStack.ClusterName(_env),
                    ["ServiceName"] = $"es-{_env}-{name}",
                }, "Minimum", 1), 1, 5,
                ComparisonOperator.LESS_THAN_THRESHOLD, $"no {name} task has been running for 5 minutes", TreatMissingData.BREACHING);
        }

        Alarm("error-lines", errorLines(5), 5, 1, ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD, "5 or more Error or Fatal lines in 5 minutes");
        Alarm("job-failed-permanently", jobsFailed(5), 1, 1, ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD, "a job used up its retries");
        Alarm("outside-call-failures", outsideFailures(5), 5, 1, ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD, "5 or more failed outside calls in 5 minutes");
        Alarm("outbox-failed", outboxFailed(5), 1, 1, ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD, "an outbox step used up its retries (Needs attention)");
        // CloudWatch cannot sum a month; the app logs one line when the month's total crosses SiteLink:MonthlyCallAlarm.
        Alarm("sitelink-monthly-calls", siteLinkMonthly(60), 1, 1, ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD, "SiteLink calls this month passed SiteLink:MonthlyCallAlarm");
        Alarm("staff-codes-locked", staffLocked(60), 3, 1, ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD, "3 or more staff sign-in codes locked in an hour");

        var database = new Dictionary<string, object> { ["DBInstanceIdentifier"] = $"es-{_env}-postgres" };
        Alarm("db-cpu", Metric("AWS/RDS", "CPUUtilization", database, "Average", 5), 80, 3,
            ComparisonOperator.GREATER_THAN_THRESHOLD, "database CPU above 80 % for 15 minutes");
        Alarm("db-connections", Metric("AWS/RDS", "DatabaseConnections", database, "Maximum", 5), props.Config.DbConnectionsAlarm, 1,
            ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD, "database connections near the limit");
        Alarm("db-free-storage", Metric("AWS/RDS", "FreeStorageSpace", database, "Minimum", 5), 5.0 * 1024 * 1024 * 1024, 1,
            ComparisonOperator.LESS_THAN_THRESHOLD, "less than 5 GB free on the database");
    }

    private void Query(string name, string[] logGroupNames, string query) =>
        _ = new CfnQueryDefinition(this, $"Query{name.Replace(" ", "").Replace("-", "")}", new CfnQueryDefinitionProps
        {
            Name = $"extraspace/{_env}/{name}",
            LogGroupNames = logGroupNames,
            QueryString = query,
        });

    // One filter per log group, all feeding the same metric. Returns the metric for a period in minutes.
    private Func<int, Metric> LogMetric(IReadOnlyList<LogGroup> logGroups, string metricName, string pattern)
    {
        for (var i = 0; i < logGroups.Count; i++)
        {
            _ = new MetricFilter(this, $"{metricName}{i}", new MetricFilterProps
            {
                LogGroup = logGroups[i],
                MetricNamespace = _namespace,
                MetricName = metricName,
                FilterPattern = FilterPattern.Literal(pattern),
                MetricValue = "1",
            });
        }
        return minutes => Metric(_namespace, metricName, new Dictionary<string, object>(), "Sum", minutes);
    }

    private static Metric Metric(string ns, string name, Dictionary<string, object> dimensions, string statistic, int minutes) =>
        new(new MetricProps
        {
            Namespace = ns,
            MetricName = name,
            DimensionsMap = dimensions.ToDictionary(d => d.Key, d => (string)d.Value),
            Statistic = statistic,
            Period = Duration.Minutes(minutes),
        });

    private void Alarm(string name, Metric metric, double threshold, int periods, ComparisonOperator comparison, string description,
        TreatMissingData missing = TreatMissingData.NOT_BREACHING)
    {
        var alarm = new Alarm(this, $"Alarm{name.Replace("-", "")}", new AlarmProps
        {
            AlarmName = $"es-{_env}-{name}",
            AlarmDescription = description,
            Metric = metric,
            Threshold = threshold,
            EvaluationPeriods = periods,
            ComparisonOperator = comparison,
            TreatMissingData = missing,
        });
        alarm.AddAlarmAction(new SnsAction(_topic));
    }
}
