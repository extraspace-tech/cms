using ExtraSpace.Api.Platform.Data;
using ExtraSpace.Api.Platform.Jobs;
using ExtraSpace.Engines;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace ExtraSpace.Api.Features.Sites;

internal sealed partial class UnitRefreshJob(AppDbContext db, IServiceScopeFactory scopes, TimeProvider time, ILogger<UnitRefreshJob> logger)
    : IRecurringJob
{
    public const string Name = "UnitRefresh";
    /// <summary>Hourly: each active site is refreshed in the hour that is 03:00 in its own zone (S1 spec §6.7; SG/MY UTC+8, KR UTC+9).</summary>
    public const string Cron = "0 * * * *";

    [DisableConcurrentExecution(timeoutInSeconds: 3600)]
    [AutomaticRetry(Attempts = 0, LogEvents = false)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var sites = await db.Set<Site>().AsNoTracking().Where(s => s.Active)
            .Select(s => new { s.Id, s.LocationCode, s.TimeZone }).ToListAsync(cancellationToken);
        foreach (var site in sites)
        {
            try
            {
                if (!IsDue(site.TimeZone, now)) continue;   // inside the try: a bad time zone fails only its own site
                // A scope per site: a failed site must not leave tracked rows behind for the next one.
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<UnitRefreshService>().RefreshAsync(site.Id, UnitRefreshMode.Nightly, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogSiteFailed(logger, exception, site.LocationCode);   // one site's failure never stops the others
            }
        }
    }

    /// <summary>Staff "Sync now" / "Full resync". Not retried, and never a failed job: the result shows on the Units page.</summary>
    [AutomaticRetry(Attempts = 0, LogEvents = false)]
    public async Task RefreshSiteAsync(Guid siteId, UnitRefreshMode mode, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<UnitRefreshService>().RefreshAsync(siteId, mode, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogStaffRefreshFailed(logger, exception, siteId);
        }
    }

    internal static bool IsDue(string timeZone, DateTimeOffset now) => SiteClock.ToLocal(now, SiteClock.Zone(timeZone)).Hour == 3;

    [LoggerMessage(Level = LogLevel.Error, Message = "Nightly unit refresh of {siteCode} failed")]
    private static partial void LogSiteFailed(ILogger logger, Exception exception, string siteCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unit refresh of site {siteId} requested by staff failed")]
    private static partial void LogStaffRefreshFailed(ILogger logger, Exception exception, Guid siteId);
}
