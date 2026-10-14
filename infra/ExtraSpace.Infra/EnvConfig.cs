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
    bool DbAdvancedInsights,
    string AspNetEnvironment,
    int ApiCpu,
    int ApiMemoryMiB,
    int ApiCount,
    int ApiMaxCount,
    bool SeparateJobs,
    int JobsCpu,
    int JobsMemoryMiB,
    int LogRetentionDays,
    int DbConnectionsAlarm,
    string[] SiteLinkWriteAllowList,
    bool SiteLinkAllowAllWrites,
    // The owner sets it once SiteLink states the monthly limit (S1 spec open item 6); 0 = off.
    long SiteLinkMonthlyCallAlarm)
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
        DbAdvancedInsights: false,
        AspNetEnvironment: "Staging",
        ApiCpu: 1024,
        ApiMemoryMiB: 2048,
        ApiCount: 1,
        ApiMaxCount: 1,
        SeparateJobs: false,          // one task runs both roles (App:Role=All)
        JobsCpu: 0,
        JobsMemoryMiB: 0,
        LogRetentionDays: 14,
        DbConnectionsAlarm: 150,
        SiteLinkWriteAllowList: ["LSETUP"],
        SiteLinkAllowAllWrites: false,
        SiteLinkMonthlyCallAlarm: 0);

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
        DbAdvancedInsights: true,
        AspNetEnvironment: "Production",
        ApiCpu: 2048,
        ApiMemoryMiB: 4096,
        ApiCount: 2,
        ApiMaxCount: 4,
        SeparateJobs: true,
        JobsCpu: 1024,
        JobsMemoryMiB: 2048,
        LogRetentionDays: 90,
        DbConnectionsAlarm: 300,
        SiteLinkWriteAllowList: [],
        SiteLinkAllowAllWrites: true,
        SiteLinkMonthlyCallAlarm: 0);

    public static IReadOnlyList<EnvConfig> All { get; } = [Staging, Production];
}
