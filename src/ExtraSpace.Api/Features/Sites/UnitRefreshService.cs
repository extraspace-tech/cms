using ExtraSpace.Api.Integrations.SiteLink;
using ExtraSpace.Api.Platform.Data;
using ExtraSpace.Engines;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExtraSpace.Api.Features.Sites;

internal enum UnitRefreshMode { Incremental, Nightly, Full }

internal enum UnitRefreshOutcome { Refreshed, Skipped }

internal sealed record UnitRefreshResult(UnitRefreshOutcome Outcome, int Units);

internal sealed class UnitRefreshException(string message) : Exception(message);

internal sealed partial class UnitRefreshService(AppDbContext db, SiteLinkClient sitelink, TimeProvider time, ILogger<UnitRefreshService> logger)
{
    /// <summary>
    /// Copies a site's changed units from SiteLink (S1 spec §6.7). The upserts, the size mapping and the cursor commit together.
    /// A refresh of a site that is already being refreshed is skipped. A failure is written to the site's sync state and rethrown.
    /// </summary>
    /// <remarks>
    /// The site lock is a session lock, so the SiteLink calls (up to 60 s each) run with no transaction open: the database ends a
    /// session that sits idle in a transaction for 60 s (EsData parameter group). Only the writes run in the transaction.
    /// </remarks>
    public async Task<UnitRefreshResult> RefreshAsync(Guid siteId, UnitRefreshMode mode, CancellationToken cancellationToken)
    {
        var site = await db.Set<Site>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == siteId, cancellationToken)
            ?? throw new InvalidOperationException($"Site {siteId} does not exist.");
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            if (!await TryLockAsync(siteId, cancellationToken))
            {
                LogSkipped(logger, site.LocationCode);
                return new UnitRefreshResult(UnitRefreshOutcome.Skipped, 0);
            }
            try
            {
                return await RefreshLockedAsync(site, mode, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Recorded while the site lock is still held, so it cannot overwrite the state of a refresh that starts next.
                db.ChangeTracker.Clear();
                await RecordFailureAsync(siteId, site.LocationCode, exception);
                throw;
            }
            finally
            {
                await UnlockAsync(siteId, site.LocationCode);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task<UnitRefreshResult> RefreshLockedAsync(Site site, UnitRefreshMode mode, CancellationToken cancellationToken)
    {
        // Only refreshes of this site write its state, and they hold the site lock: the read needs no transaction.
        var state = await db.Set<UnitSyncState>().SingleOrDefaultAsync(s => s.SiteId == site.Id, cancellationToken);
        if (state is null) db.Add(state = new UnitSyncState { SiteId = site.Id });

        if (mode == UnitRefreshMode.Nightly) await CheckCurrencyAsync(site, cancellationToken);
        var full = mode == UnitRefreshMode.Full || state.LastTimePolledTicks is null;
        // Every poll asks for the units hidden from the website too, so our copy holds all of them the same way.
        var page = await sitelink.UnitsInformationAsync(site.LocationCode, full ? null : state.LastTimePolledTicks,
            includeExcludedFromWebsite: true, SiteClock.Zone(site.TimeZone), cancellationToken);
        if (full && page.Units.Count == 0) throw new UnitRefreshException("SiteLink returned no units for a full refresh.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Before the first unit row lock: a size edit's global remap takes the same lock and then updates every unit.
        await UnitSizeMap.LockAsync(db, cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        await UpsertAsync(site.Id, page.Units, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await UnitSizeMap.RemapAsync(db, site.Id, time, cancellationToken);
        state.LastTimePolledTicks = page.NextTicks ?? state.LastTimePolledTicks;
        state.LastSyncedAt = now;
        state.LastAttemptAt = now;
        state.LastError = null;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        LogRefreshed(logger, site.LocationCode, mode, page.Units.Count);
        return new UnitRefreshResult(UnitRefreshOutcome.Refreshed, page.Units.Count);
    }

    internal static string LockKey(Guid siteId) => "unit-refresh:" + siteId;

    private async Task<bool> TryLockAsync(Guid siteId, CancellationToken cancellationToken) =>
        await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock(hashtextextended({LockKey(siteId)}, 0)) AS \"Value\"")
            .SingleAsync(cancellationToken);

    private async Task UnlockAsync(Guid siteId, string siteCode)
    {
        try
        {
            await db.Database.SqlQuery<bool>($"SELECT pg_advisory_unlock(hashtextextended({LockKey(siteId)}, 0)) AS \"Value\"")
                .SingleAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            // A broken connection already lost the lock with its session; the pool's reset is the safety net for the rest.
            LogUnlockFailed(logger, exception, siteCode);
        }
    }

    private async Task CheckCurrencyAsync(Site site, CancellationToken cancellationToken)
    {
        var expected = Currencies.Decimals(site.Currency);
        if (await sitelink.CurrencyDecimalsAsync(site.LocationCode, cancellationToken) is { } decimals && decimals != expected)
            throw new UnitRefreshException($"SiteLink uses {decimals} currency decimals at this site, but {site.Currency} has {expected}.");
    }

    private async Task UpsertAsync(Guid siteId, IReadOnlyList<SiteLinkUnit> rows, DateTime now, CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return;
        var ids = rows.Select(r => r.UnitId).Distinct().ToArray();
        var existing = await db.Set<Unit>().Where(u => u.SiteId == siteId && ids.Contains(u.SiteLinkUnitId))
            .ToDictionaryAsync(u => u.SiteLinkUnitId, cancellationToken);
        var storageTypes = await db.Set<StorageType>().AsNoTracking().ToDictionaryAsync(t => t.Key, t => (Guid?)t.Id, cancellationToken);
        foreach (var row in rows)
        {
            var isNew = !existing.TryGetValue(row.UnitId, out var unit);
            if (unit is null)
            {
                unit = new Unit { SiteId = siteId, SiteLinkUnitId = row.UnitId, UnitName = row.UnitName };
                db.Add(unit);
                existing[row.UnitId] = unit;
            }
            // A blank or absent type name says nothing: a stored unit keeps its storage type, a new one is self-storage.
            var storageTypeId = isNew || !string.IsNullOrWhiteSpace(row.TypeName)
                ? storageTypes.GetValueOrDefault(StorageType.KeyFor(row.TypeName ?? ""))
                : unit.StorageTypeId;
            Apply(unit, row, storageTypeId, now);
        }
    }

    /// <summary>
    /// A null column keeps the stored value (S1 spec §6.5). A new unit starts from the entity defaults, which are the defaults for an
    /// absent column: type 0, size 0, rentable, every other flag false.
    /// </summary>
    private static void Apply(Unit unit, SiteLinkUnit row, Guid? storageTypeId, DateTime now)
    {
        unit.UnitName = row.UnitName;
        unit.SiteLinkUnitTypeId = row.UnitTypeId ?? unit.SiteLinkUnitTypeId;
        unit.Width = row.Width ?? unit.Width;
        unit.Length = row.Length ?? unit.Length;
        // From the stored sides with SiteLinkUnit's own rounding, so a row with one side only cannot leave the area stale.
        unit.AreaSqft = (row with { Width = unit.Width, Length = unit.Length }).AreaSqft!.Value;
        unit.Climate = row.Climate ?? unit.Climate;
        unit.Inside = row.Inside ?? unit.Inside;
        unit.Power = row.Power ?? unit.Power;
        unit.Alarm = row.Alarm ?? unit.Alarm;
        unit.Ada = row.Ada ?? unit.Ada;
        unit.Mobile = row.Mobile ?? unit.Mobile;
        unit.Rented = row.Rented ?? unit.Rented;
        unit.Rentable = row.Rentable ?? unit.Rentable;
        unit.WaitingListReserved = row.WaitingListReserved ?? unit.WaitingListReserved;
        unit.Floor = row.Floor ?? unit.Floor;
        unit.Description = row.Description ?? unit.Description;
        unit.StandardRate = row.StandardRate ?? unit.StandardRate;
        unit.WebRate = row.WebRate ?? unit.WebRate;
        unit.BoardRate = row.BoardRate ?? unit.BoardRate;
        unit.PushRate = row.PushRate ?? unit.PushRate;
        unit.PreferredRate = row.PreferredRate ?? unit.PreferredRate;
        unit.StandardSecurityDeposit = row.StandardSecurityDeposit ?? unit.StandardSecurityDeposit;
        // Absent or a SiteLink null date = not deleted.
        unit.DeletedAt = row.DeletedAt;
        unit.StorageTypeId = storageTypeId;
        unit.LastSyncedAt = now;
    }

    private async Task RecordFailureAsync(Guid siteId, string siteCode, Exception exception)
    {
        try
        {
            var now = time.GetUtcNow().UtcDateTime;
            var error = Describe(exception);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO unit_sync_state (site_id, last_attempt_at, last_error) VALUES ({siteId}, {now}, {error})
                ON CONFLICT (site_id) DO UPDATE SET last_attempt_at = excluded.last_attempt_at, last_error = excluded.last_error
                """, CancellationToken.None);
        }
        catch (Exception recordFailure)
        {
            LogRecordFailed(logger, recordFailure, siteCode);   // keep the original error the caller sees
        }
    }

    /// <summary>What staff see on the Units page: no SiteLink text, no data values beyond our own messages.</summary>
    internal static string Describe(Exception exception)
    {
        var text = exception switch
        {
            SiteLinkException e => $"SiteLink refused the call (code {e.ReturnCode}).",
            SiteLinkUnavailableException => "SiteLink could not be reached.",
            UnitRefreshException e => e.Message,
            // Our parse helpers name the column, never the value.
            FormatException e => "SiteLink sent a value we could not read: " + e.Message,
            DbUpdateException => "Saving the units failed.",
            NpgsqlException => "The database could not complete the refresh.",
            _ => exception.GetType().Name,
        };
        return text.Length <= 1000 ? text : text[..1000];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Unit refresh of {siteCode} ({mode}): {units} units")]
    private static partial void LogRefreshed(ILogger logger, string siteCode, UnitRefreshMode mode, int units);

    [LoggerMessage(Level = LogLevel.Information, Message = "Unit refresh of {siteCode} skipped: another refresh of that site is running")]
    private static partial void LogSkipped(ILogger logger, string siteCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not release the unit refresh lock of {siteCode}")]
    private static partial void LogUnlockFailed(ILogger logger, Exception exception, string siteCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not record the failed unit refresh of {siteCode}")]
    private static partial void LogRecordFailed(ILogger logger, Exception exception, string siteCode);
}
