using Amazon.CDK;
using Amazon.CDK.AWS.CertificateManager;
using Amazon.CDK.AWS.Route53;
using Amazon.CDK.AWS.SES;
using Constructs;

namespace ExtraSpace.Infra.Stacks;

public sealed class EdgeStackProps : StackProps
{
    public required EnvConfig Config { get; init; }
    public required DeployInputs Inputs { get; init; }
}

/// <summary>The HTTPS certificate and the email domain. Both are proven through DNS records in the delegated zone, so they sit apart from the app: slow to create, rarely changed.</summary>
public sealed class EdgeStack : Stack
{
    public ICertificate Certificate { get; }

    public EdgeStack(Construct scope, string id, EdgeStackProps props) : base(scope, id, props)
    {
        var inputs = props.Inputs;
        var domain = inputs.EmailDomain;
        // Attributes, not a lookup: a lookup calls AWS at synth time and writes the account id to cdk.context.json.
        var zone = HostedZone.FromHostedZoneAttributes(this, "Zone", new HostedZoneAttributes
        {
            HostedZoneId = inputs.ZoneId,
            ZoneName = inputs.ZoneName,
        });

        Certificate = new Certificate(this, "Certificate", new CertificateProps
        {
            DomainName = inputs.Host,
            Validation = CertificateValidation.FromDns(zone),
        });

        var identity = new EmailIdentity(this, "EmailIdentity", new EmailIdentityProps
        {
            Identity = Identity.Domain(domain),
            MailFromDomain = $"mail.{domain}",
        });

        // The DKIM names are tokens, so they go in as L1 records: the L2 record would append the zone name again.
        var index = 0;
        foreach (var record in identity.DkimRecords)
        {
            _ = new CfnRecordSet(this, $"Dkim{++index}", new CfnRecordSetProps
            {
                HostedZoneId = inputs.ZoneId,
                Name = record.Name,
                Type = "CNAME",
                Ttl = "1800",
                ResourceRecords = new[] { record.Value },
            });
        }

        _ = new MxRecord(this, "MailFromMx", new MxRecordProps
        {
            Zone = zone,
            RecordName = $"mail.{domain}.",
            Values = new IMxRecordValue[] { new MxRecordValue { Priority = 10, HostName = $"feedback-smtp.{InfraComposer.Region}.amazonses.com" } },
        });
        _ = new TxtRecord(this, "MailFromSpf", new TxtRecordProps
        {
            Zone = zone,
            RecordName = $"mail.{domain}.",
            Values = new[] { "v=spf1 include:amazonses.com ~all" },
        });
        _ = new TxtRecord(this, "Dmarc", new TxtRecordProps
        {
            Zone = zone,
            RecordName = $"_dmarc.{domain}.",
            Values = new[] { "v=DMARC1; p=quarantine" },
        });

        _ = new CfnOutput(this, "CertificateArn", new CfnOutputProps { Value = Certificate.CertificateArn });
    }
}
