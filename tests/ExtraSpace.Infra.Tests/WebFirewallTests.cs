using Amazon.CDK.Assertions;
using Xunit;

namespace ExtraSpace.Infra.Tests;

public class WebFirewallTests
{
    private static Template App(string env) => TestStacks.For($"EsApp-{env}");

    private static string Acl(string env) => System.Text.Json.JsonSerializer.Serialize(App(env).FindResources("AWS::WAFv2::WebACL"));

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void One_regional_acl_allows_by_default_and_sits_on_the_load_balancer(string env)
    {
        var template = App(env);

        template.HasResourceProperties("AWS::WAFv2::WebACL", new Dictionary<string, object>
        {
            ["Name"] = $"es-{env}",
            ["Scope"] = "REGIONAL",
            ["DefaultAction"] = new Dictionary<string, object> { ["Allow"] = new Dictionary<string, object>() },
        });
        template.ResourceCountIs("AWS::WAFv2::WebACLAssociation", 1);
        template.HasResourceProperties("AWS::WAFv2::WebACLAssociation", new Dictionary<string, object>
        {
            ["ResourceArn"] = new Dictionary<string, object>
            {
                ["Ref"] = template.FindResources("AWS::ElasticLoadBalancingV2::LoadBalancer").Keys.Single(),
            },
            ["WebACLArn"] = new Dictionary<string, object>
            {
                ["Fn::GetAtt"] = new object[] { template.FindResources("AWS::WAFv2::WebACL").Keys.Single(), "Arn" },
            },
        });
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void Managed_groups_run_whole(string env)
    {
        // An override or an exclusion would switch single managed rules to count without the group looking different.
        var acl = Acl(env);

        Assert.DoesNotContain("RuleActionOverrides", acl);
        Assert.DoesNotContain("ExcludedRules", acl);
        Assert.DoesNotContain("\"Count\"", acl);
    }

    // The app's own 429 shape instead of the firewall's 403 HTML page. No traceId: the request never reaches the app.
    private const string RateLimitedBody = """{"type":"about:blank","title":"Too many requests","status":429,"code":"RATE_LIMITED"}""";

    private static Dictionary<string, object> BlockWith429() => new()
    {
        ["Block"] = new Dictionary<string, object>
        {
            ["CustomResponse"] = new Dictionary<string, object>
            {
                ["ResponseCode"] = 429,
                ["CustomResponseBodyKey"] = "rate-limited",
                ["ResponseHeaders"] = new object[] { new Dictionary<string, object> { ["Name"] = "Retry-After", ["Value"] = "300" } },
            },
        },
    };

    private static Dictionary<string, object> PathStartsWith(string path) => new()
    {
        ["ByteMatchStatement"] = new Dictionary<string, object>
        {
            ["SearchString"] = path,
            ["FieldToMatch"] = new Dictionary<string, object> { ["UriPath"] = new Dictionary<string, object>() },
            ["PositionalConstraint"] = "STARTS_WITH",
            ["TextTransformations"] = new object[] { new Dictionary<string, object> { ["Priority"] = 0, ["Type"] = "LOWERCASE" } },
        },
    };

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void Sign_in_paths_are_limited_to_100_requests_in_five_minutes_per_ip(string env)
    {
        App(env).HasResourceProperties("AWS::WAFv2::WebACL", new Dictionary<string, object>
        {
            ["Rules"] = Match.ArrayWith(new object[]
            {
                Match.ObjectLike(new Dictionary<string, object>
                {
                    ["Name"] = "RateAuth",
                    ["Action"] = BlockWith429(),
                    ["Statement"] = new Dictionary<string, object>
                    {
                        ["RateBasedStatement"] = new Dictionary<string, object>
                        {
                            ["Limit"] = 100,
                            ["EvaluationWindowSec"] = 300,
                            ["AggregateKeyType"] = "IP",
                            ["ScopeDownStatement"] = new Dictionary<string, object>
                            {
                                ["OrStatement"] = new Dictionary<string, object>
                                {
                                    ["Statements"] = new object[] { PathStartsWith("/api/v1/auth/"), PathStartsWith("/admin/api/auth/") },
                                },
                            },
                        },
                    },
                }),
            }),
        });
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void Rate_limit_answer_has_the_apps_own_429_shape(string env)
    {
        App(env).HasResourceProperties("AWS::WAFv2::WebACL", new Dictionary<string, object>
        {
            ["CustomResponseBodies"] = new Dictionary<string, object>
            {
                ["rate-limited"] = new Dictionary<string, object> { ["ContentType"] = "APPLICATION_JSON", ["Content"] = RateLimitedBody },
            },
        });

        using var body = System.Text.Json.JsonDocument.Parse(RateLimitedBody);
        Assert.Equal("RATE_LIMITED", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(429, body.RootElement.GetProperty("status").GetInt32());
        // Only the two rate rules answer this way; the managed groups keep the default block.
        Assert.Equal(2, Acl(env).Split("\"CustomResponseBodyKey\":\"rate-limited\"").Length - 1);
    }

    [Theory]
    [InlineData("AWSManagedRulesCommonRuleSet")]
    [InlineData("AWSManagedRulesKnownBadInputsRuleSet")]
    [InlineData("AWSManagedRulesAmazonIpReputationList")]
    [InlineData("AWSManagedRulesSQLiRuleSet")]
    public void The_four_managed_groups_block(string group)
    {
        App("staging").HasResourceProperties("AWS::WAFv2::WebACL", new Dictionary<string, object>
        {
            ["Rules"] = Match.ArrayWith(new object[]
            {
                Match.ObjectLike(new Dictionary<string, object>
                {
                    // "None" keeps the group's own block actions; "Count" would only observe.
                    ["OverrideAction"] = new Dictionary<string, object> { ["None"] = new Dictionary<string, object>() },
                    ["Statement"] = new Dictionary<string, object>
                    {
                        ["ManagedRuleGroupStatement"] = new Dictionary<string, object> { ["VendorName"] = "AWS", ["Name"] = group },
                    },
                }),
            }),
        });
    }

    [Fact]
    public void Every_ip_is_limited_to_2000_requests_in_five_minutes()
    {
        App("staging").HasResourceProperties("AWS::WAFv2::WebACL", new Dictionary<string, object>
        {
            ["Rules"] = Match.ArrayWith(new object[]
            {
                Match.ObjectLike(new Dictionary<string, object>
                {
                    ["Name"] = "RateAll",
                    ["Action"] = BlockWith429(),
                    ["Statement"] = new Dictionary<string, object>
                    {
                        ["RateBasedStatement"] = new Dictionary<string, object>
                        {
                            ["Limit"] = 2000,
                            ["EvaluationWindowSec"] = 300,
                            ["AggregateKeyType"] = "IP",
                        },
                    },
                }),
            }),
        });
    }

    [Fact]
    public void Auth_rate_rule_lowercases_the_path()
    {
        // ASP.NET routes ignore case; a WAF byte match does not. Without LOWERCASE, /API/V1/AUTH/code walks around the rule.
        var acl = Acl("staging");
        var rule = acl[acl.IndexOf("\"RateAuth\"", StringComparison.Ordinal)..];

        Assert.Contains("\"Limit\":100", rule);
        Assert.Contains("\"SearchString\":\"/api/v1/auth/\"", rule);
        Assert.Contains("\"SearchString\":\"/admin/api/auth/\"", rule);
        Assert.Contains("\"PositionalConstraint\":\"STARTS_WITH\"", rule);
        Assert.Equal(2, rule.Split("\"Type\":\"LOWERCASE\"").Length - 1);
        Assert.DoesNotContain("\"Type\":\"NONE\"", rule);
    }

    [Fact]
    public void Priorities_are_unique()
    {
        var acl = App("staging").FindResources("AWS::WAFv2::WebACL").Values.Single();
        var rules = (System.Collections.IEnumerable)((IDictionary<string, object>)((IDictionary<string, object>)acl)["Properties"])["Rules"];
        var priorities = rules.Cast<IDictionary<string, object>>().Select(r => Convert.ToInt32(r["Priority"])).ToList();

        Assert.Equal(6, priorities.Count);
        Assert.Equal(priorities.Count, priorities.Distinct().Count());
    }
}
