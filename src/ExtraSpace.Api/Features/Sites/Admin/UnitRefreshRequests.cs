using ExtraSpace.Api.Platform.Data;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace ExtraSpace.Api.Features.Sites.Admin;

/// <summary>Staff "Sync now" / "Full resync": queued, not awaited (a pull can take a minute). The result shows on the Units page.</summary>
internal sealed partial class UnitRefreshRequests(AppDbContext db, IBackgroundJobClient jobs, ILogger<UnitRefreshRequests> logger)
{
    public async Task StartAsync(Guid siteId, bool full, CancellationToken cancellationToken)
    {
        var code = await db.Set<Site>().Where(s => s.Id == siteId).Select(s => s.LocationCode).SingleOrDefaultAsync(cancellationToken)
            ?? throw SitesErrors.NotFound("Site");
        var mode = full ? UnitRefreshMode.Full : UnitRefreshMode.Incremental;
        jobs.Enqueue<UnitRefreshJob>(job => job.RefreshSiteAsync(siteId, mode, CancellationToken.None));
        LogRequested(logger, code, mode);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Unit refresh of {siteCode} ({mode}) requested by staff")]
    private static partial void LogRequested(ILogger logger, string siteCode, UnitRefreshMode mode);
}
