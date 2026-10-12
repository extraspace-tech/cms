namespace ExtraSpace.Infra;

public sealed record EnvConfig(
    string Name,
    string VpcCidr,
    int NatGateways,
    string DbInstanceType,
    bool DbMultiAz,
    int DbAllocatedStorageGb,
    int DbMaxAllocatedStorageGb,
    int DbBackupRetentionDays,
    bool DbProxy,
    bool DbAdvancedInsights)
{
    public bool IsProduction => Name == "production";

    // Sizes from the AWS cost estimate v4 (docs/notes/aws-cost/aws-cost-estimate-v4.html).
    public static readonly EnvConfig Staging = new(
        Name: "staging",
        VpcCidr: "10.10.0.0/16",
        NatGateways: 1,
        DbInstanceType: "t4g.medium",
        DbMultiAz: false,
        DbAllocatedStorageGb: 20,
        DbMaxAllocatedStorageGb: 50,
        DbBackupRetentionDays: 7,
        DbProxy: false,
        DbAdvancedInsights: false);

    public static readonly EnvConfig Production = new(
        Name: "production",
        VpcCidr: "10.20.0.0/16",
        NatGateways: 2,
        DbInstanceType: "m8g.large",
        DbMultiAz: true,
        DbAllocatedStorageGb: 50,
        DbMaxAllocatedStorageGb: 200,
        DbBackupRetentionDays: 35,
        DbProxy: true,
        DbAdvancedInsights: true);

    public static IReadOnlyList<EnvConfig> All { get; } = [Staging, Production];
}
