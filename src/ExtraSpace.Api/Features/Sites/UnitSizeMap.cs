using ExtraSpace.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace ExtraSpace.Api.Features.Sites;

internal static class UnitSizeMap
{
    /// <summary>
    /// Sets each unit's size from its area (S1 spec §5.1): the first size, by sort order then key, whose storage type is the unit's
    /// (or none) and whose range holds the area (min inclusive, max exclusive); a unit with no area gets none. One SQL rule for the
    /// sync and for size edits; only rows that change are written. <paramref name="siteId"/> null = every unit.
    /// </summary>
    /// <remarks>
    /// Remaps run one at a time (<see cref="LockAsync"/>), in the caller's transaction or in one of their own. Raw SQL skips the
    /// timestamps interceptor, so <c>updated_at</c> comes from <paramref name="time"/>.
    /// </remarks>
    public static async Task<int> RemapAsync(AppDbContext db, Guid? siteId, TimeProvider time, CancellationToken cancellationToken)
    {
        await using var own = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        await LockAsync(db, cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        var changed = await db.Database.ExecuteSqlAsync($"""
            UPDATE unit AS u SET unit_size_id = m.size_id, updated_at = {now}
            FROM unit AS x
            LEFT JOIN LATERAL (
                SELECT s.id AS size_id FROM unit_size AS s
                WHERE x.area_sqft > 0
                  AND (s.storage_type_id IS NULL OR s.storage_type_id = x.storage_type_id)
                  AND x.area_sqft >= COALESCE(s.min_area_sqft, 0)
                  AND (s.max_area_sqft IS NULL OR x.area_sqft < s.max_area_sqft)
                ORDER BY s.sort_order, s.key
                LIMIT 1) AS m ON true
            WHERE u.id = x.id
              AND ({siteId}::uuid IS NULL OR x.site_id = {siteId})
              AND u.unit_size_id IS DISTINCT FROM m.size_id
            """, cancellationToken);
        if (own is not null) await own.CommitAsync(cancellationToken);
        return changed;
    }

    /// <summary>
    /// Serialises remaps until the transaction ends: two at once could each write a stale size, or deadlock on unit rows. Take it
    /// before the transaction locks any unit or unit size row (the refresh before its upsert, a size edit before its save).
    /// </summary>
    public static Task LockAsync(AppDbContext db, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended('unit-size-map', 0))", cancellationToken);
}
