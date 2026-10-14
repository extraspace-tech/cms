using ExtraSpace.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace ExtraSpace.Api.Features.Sites.Admin;

internal sealed class UnitAdminQuery(AppDbContext db)
{
    public Task<Paged<UnitListItem>> ListAsync(string? search, Guid? siteId, bool? available, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        var rows = db.Set<Unit>().AsNoTracking();
        if (siteId is { } site) rows = rows.Where(u => u.SiteId == site);
        if (available is { } flag) rows = rows.Where(u => u.Available == flag);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = Paging.Contains(search);
            rows = rows.Where(u => EF.Functions.ILike(u.UnitName, pattern, Paging.Escape)
                || (u.Description != null && EF.Functions.ILike(u.Description, pattern, Paging.Escape)));
        }
        return rows.OrderBy(u => u.UnitName).ThenBy(u => u.Id)
            .Select(u => new UnitListItem(u.Id, u.UnitName, u.SiteId, u.Site.Name, u.Site.Currency,
                u.StorageType == null ? null : u.StorageType.Name, u.UnitSize == null ? null : u.UnitSize.Label,
                u.StandardRate, u.Available, u.Rented))
            .ToPageAsync(page, pageSize, cancellationToken);
    }

    public async Task<UnitResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Set<Unit>().AsNoTracking().Where(u => u.Id == id)
            .Select(u => new UnitResponse(u.Id, u.UnitName, u.SiteId, u.Site.Name, u.Site.Currency,
                u.StorageType == null ? null : u.StorageType.Name, u.UnitSize == null ? null : u.UnitSize.Label,
                u.Width, u.Length, u.AreaSqft, u.Floor, u.Climate, u.StandardRate, u.WebRate, u.Rented, u.Available, u.DeletedAt, u.LastSyncedAt))
            .SingleOrDefaultAsync(cancellationToken) ?? throw SitesErrors.NotFound("Unit");

    public Task<List<UnitSyncStatus>> SyncStatusAsync(CancellationToken cancellationToken) =>
        (from s in db.Set<Site>().AsNoTracking()
         join st in db.Set<UnitSyncState>() on s.Id equals st.SiteId into states
         from st in states.DefaultIfEmpty()
         orderby s.LocationCode
         select new UnitSyncStatus(s.Id, s.LocationCode, s.Name, s.Active, db.Set<Unit>().Count(u => u.SiteId == s.Id),
             st == null ? null : st.LastSyncedAt, st == null ? null : st.LastAttemptAt, st == null ? null : st.LastError))
        .ToListAsync(cancellationToken);
}
