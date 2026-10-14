namespace ExtraSpace.Api.Integrations.SiteLink;

internal sealed class SiteLinkOptions
{
    public const string Section = "SiteLink";

    public string BaseUrl { get; set; } = "";
    public string? CorpCode { get; set; }
    /// <summary>Must be <c>user:::licence key</c>; a bare user name fails with -98 / -89, so it counts as not configured.</summary>
    public string? CorpUserName { get; set; }
    public string? CorpPassword { get; set; }
    public string? ApiKey { get; set; }
    /// <summary>Location codes a write may reach (S1 spec §6.2). Default <c>LSETUP</c> (appsettings.json).</summary>
    public string[] WriteAllowList { get; set; } = [];
    /// <summary>Production only: writes may reach every site. Other environments refuse to start with it.</summary>
    public bool AllowWritesToAllSites { get; set; }
    public int CallsPerSecond { get; set; } = 2;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    public int BreakerMinimumThroughput { get; set; } = 5;
    /// <summary>Monthly total that raises one Error line; 0 = off (SiteLink's limit is unknown, spec open item 6).</summary>
    public long MonthlyCallAlarm { get; set; }

    // Not a start-up failure (S1-1 decision 6): each call answers 503 and logs a Warning until the secret is fixed.
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(CorpCode) && CorpUserName?.Contains(":::", StringComparison.Ordinal) == true
        && !string.IsNullOrWhiteSpace(CorpPassword);

    public bool MayWriteTo(string locationCode) =>
        AllowWritesToAllSites || WriteAllowList.Contains(locationCode, StringComparer.OrdinalIgnoreCase);
}
