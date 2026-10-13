using Amazon.CDK.AWS.WAFv2;
using Constructs;

namespace ExtraSpace.Infra.Stacks;

/// <summary>AWS WAF on the load balancer: the managed groups of S0 spec §9.2 plus two rate rules.</summary>
internal sealed class WebFirewall : Construct
{
    private static readonly string[] ManagedGroups =
    [
        "AWSManagedRulesCommonRuleSet",
        "AWSManagedRulesKnownBadInputsRuleSet",
        "AWSManagedRulesAmazonIpReputationList",
        "AWSManagedRulesSQLiRuleSet",
    ];

    // Sign-in paths, lower case: the rule lower-cases the request path before it compares.
    private static readonly string[] AuthPaths = ["/api/v1/auth/", "/admin/api/auth/"];

    // The app's own 429 shape, so the Flutter app and the admin UI handle both limits the same way.
    // No traceId: a blocked request never reaches the app.
    private const string RateLimitedBodyKey = "rate-limited";
    private const string RateLimitedBody = """{"type":"about:blank","title":"Too many requests","status":429,"code":"RATE_LIMITED"}""";
    private const int WindowSeconds = 300;

    public WebFirewall(Construct scope, string id, string env, string loadBalancerArn) : base(scope, id)
    {
        var rules = new List<object>();
        var priority = 0;
        foreach (var group in ManagedGroups)
        {
            priority += 10;
            rules.Add(new CfnWebACL.RuleProperty
            {
                Name = group,
                Priority = priority,
                // None = keep the group's own block actions.
                OverrideAction = new CfnWebACL.OverrideActionProperty { None = new Dictionary<string, object>() },
                Statement = new CfnWebACL.StatementProperty
                {
                    ManagedRuleGroupStatement = new CfnWebACL.ManagedRuleGroupStatementProperty { VendorName = "AWS", Name = group },
                },
                VisibilityConfig = Visibility($"es-{env}-{group}"),
            });
        }

        rules.Add(RateRule("RateAuth", priority += 10, 100, env, new CfnWebACL.StatementProperty
        {
            OrStatement = new CfnWebACL.OrStatementProperty
            {
                Statements = AuthPaths.Select(path => (object)new CfnWebACL.StatementProperty
                {
                    ByteMatchStatement = new CfnWebACL.ByteMatchStatementProperty
                    {
                        SearchString = path,
                        FieldToMatch = new CfnWebACL.FieldToMatchProperty { UriPath = new Dictionary<string, object>() },
                        PositionalConstraint = "STARTS_WITH",
                        // ASP.NET routes ignore case, a byte match does not.
                        TextTransformations = new object[] { new CfnWebACL.TextTransformationProperty { Priority = 0, Type = "LOWERCASE" } },
                    },
                }).ToArray(),
            },
        }));
        rules.Add(RateRule("RateAll", priority + 10, 2000, env, scopeDown: null));

        var acl = new CfnWebACL(this, "Acl", new CfnWebACLProps
        {
            Name = $"es-{env}",
            Scope = "REGIONAL",
            DefaultAction = new CfnWebACL.DefaultActionProperty { Allow = new CfnWebACL.AllowActionProperty() },
            VisibilityConfig = Visibility($"es-{env}"),
            CustomResponseBodies = new Dictionary<string, object>
            {
                [RateLimitedBodyKey] = new CfnWebACL.CustomResponseBodyProperty { ContentType = "APPLICATION_JSON", Content = RateLimitedBody },
            },
            Rules = rules.ToArray(),
        });

        _ = new CfnWebACLAssociation(this, "Association", new CfnWebACLAssociationProps
        {
            ResourceArn = loadBalancerArn,
            WebAclArn = acl.AttrArn,
        });
    }

    private static CfnWebACL.RuleProperty RateRule(string name, int priority, int limit, string env, CfnWebACL.StatementProperty? scopeDown) => new()
    {
        Name = name,
        Priority = priority,
        Action = new CfnWebACL.RuleActionProperty
        {
            Block = new CfnWebACL.BlockActionProperty
            {
                CustomResponse = new CfnWebACL.CustomResponseProperty
                {
                    ResponseCode = 429,
                    CustomResponseBodyKey = RateLimitedBodyKey,
                    ResponseHeaders = new object[]
                    {
                        new CfnWebACL.CustomHTTPHeaderProperty { Name = "Retry-After", Value = WindowSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    },
                },
            },
        },
        Statement = new CfnWebACL.StatementProperty
        {
            RateBasedStatement = new CfnWebACL.RateBasedStatementProperty
            {
                Limit = limit,
                EvaluationWindowSec = WindowSeconds,
                // The load balancer is the edge, so the source address is the real client.
                AggregateKeyType = "IP",
                ScopeDownStatement = scopeDown,
            },
        },
        VisibilityConfig = Visibility($"es-{env}-{name}"),
    };

    private static CfnWebACL.VisibilityConfigProperty Visibility(string metric) => new()
    {
        CloudWatchMetricsEnabled = true,
        SampledRequestsEnabled = true,
        MetricName = metric,
    };
}
