using Amazon.CDK.Assertions;
using Xunit;

namespace ExtraSpace.Infra.Tests;

public class EdgeStackTests
{
    private static Template Edge(string env) => TestStacks.For($"EsEdge-{env}");

    [Theory]
    [InlineData("staging", "staging.app.example.test")]
    [InlineData("production", "app.example.test")]
    public void Certificate_is_for_the_host_and_validated_through_dns(string env, string host)
    {
        Edge(env).HasResourceProperties("AWS::CertificateManager::Certificate", new Dictionary<string, object>
        {
            ["DomainName"] = host,
            ["ValidationMethod"] = "DNS",
            ["DomainValidationOptions"] = new object[]
            {
                new Dictionary<string, object> { ["DomainName"] = host, ["HostedZoneId"] = "Z0000000TEST" },
            },
        });
    }

    [Theory]
    [InlineData("staging", "staging.app.example.test")]
    [InlineData("production", "app.example.test")]
    public void Email_identity_is_the_environment_domain_with_dkim_mail_from_and_dmarc(string env, string domain)
    {
        var template = Edge(env);

        template.HasResourceProperties("AWS::SES::EmailIdentity", new Dictionary<string, object>
        {
            ["EmailIdentity"] = domain,
            ["MailFromAttributes"] = Match.ObjectLike(new Dictionary<string, object> { ["MailFromDomain"] = $"mail.{domain}" }),
        });
        template.ResourcePropertiesCountIs("AWS::Route53::RecordSet", new Dictionary<string, object>
        {
            ["Type"] = "CNAME",
            ["HostedZoneId"] = "Z0000000TEST",
        }, 3);
        template.HasResourceProperties("AWS::Route53::RecordSet", new Dictionary<string, object>
        {
            ["Name"] = $"mail.{domain}.",
            ["Type"] = "MX",
            ["ResourceRecords"] = new object[] { "10 feedback-smtp.ap-southeast-1.amazonses.com" },
        });
        template.HasResourceProperties("AWS::Route53::RecordSet", new Dictionary<string, object>
        {
            ["Name"] = $"mail.{domain}.",
            ["Type"] = "TXT",
            ["ResourceRecords"] = new object[] { "\"v=spf1 include:amazonses.com ~all\"" },
        });
        template.HasResourceProperties("AWS::Route53::RecordSet", new Dictionary<string, object>
        {
            ["Name"] = $"_dmarc.{domain}.",
            ["Type"] = "TXT",
            ["ResourceRecords"] = new object[] { "\"v=DMARC1; p=quarantine\"" },
        });
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void Each_dkim_record_points_at_its_own_token(string env)
    {
        var template = Edge(env);
        var identity = Assert.Single(template.FindResources("AWS::SES::EmailIdentity")).Key;

        for (var n = 1; n <= 3; n++)
        {
            template.HasResourceProperties("AWS::Route53::RecordSet", new Dictionary<string, object>
            {
                ["Type"] = "CNAME",
                ["HostedZoneId"] = "Z0000000TEST",
                ["Name"] = new Dictionary<string, object> { ["Fn::GetAtt"] = new object[] { identity, $"DkimDNSTokenName{n}" } },
                ["ResourceRecords"] = new object[]
                {
                    new Dictionary<string, object> { ["Fn::GetAtt"] = new object[] { identity, $"DkimDNSTokenValue{n}" } },
                },
            });
        }
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void All_six_records_go_into_the_delegated_zone(string env)
    {
        var template = Edge(env);

        template.ResourceCountIs("AWS::Route53::RecordSet", 6);
        template.ResourcePropertiesCountIs("AWS::Route53::RecordSet", new Dictionary<string, object>
        {
            ["HostedZoneId"] = "Z0000000TEST",
        }, 6);
    }

    [Fact]
    public void Staging_and_production_write_different_record_names()
    {
        var staging = LiteralRecordNames("staging");
        var production = LiteralRecordNames("production");

        // MX and SPF share one name, DMARC has its own.
        Assert.Equal(2, staging.Count);
        Assert.Equal(2, production.Count);
        Assert.Empty(staging.Intersect(production, StringComparer.OrdinalIgnoreCase));
    }

    private static HashSet<string> LiteralRecordNames(string env) =>
        Edge(env).FindResources("AWS::Route53::RecordSet").Values
            .Select(resource => ((IDictionary<string, object>)resource["Properties"])["Name"])
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public void No_zone_is_created_and_no_lambda_is_needed(string env)
    {
        // The zone is the one client IT delegated; this stack only writes records into it.
        Edge(env).ResourceCountIs("AWS::Route53::HostedZone", 0);
        Edge(env).ResourceCountIs("AWS::Lambda::Function", 0);
    }
}
