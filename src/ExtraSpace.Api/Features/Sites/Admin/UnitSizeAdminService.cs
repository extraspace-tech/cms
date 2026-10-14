using ExtraSpace.Api.Platform.Data;
using ExtraSpace.Api.Platform.Http;
using Microsoft.EntityFrameworkCore;

namespace ExtraSpace.Api.Features.Sites.Admin;

/// <summary>Each write re-maps every unit's size in the same transaction (S1-1 decision 9), so the Units page is right at once.</summary>
internal sealed class UnitSizeAdminService(AppDbContext db, TimeProvider time)
{
    private static readonly string[] Clearable = ["minAreaSqft", "maxAreaSqft", "storageTypeId"];

    public Task<Paged<UnitSizeResponse>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var rows = db.Set<UnitSize>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = Paging.Contains(search);
            rows = rows.Where(s => EF.Functions.ILike(s.Key, pattern, Paging.Escape) || EF.Functions.ILike(s.Label, pattern, Paging.Escape));
        }
        return Project(rows.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)).ToPageAsync(page, pageSize, cancellationToken);
    }

    public async Task<UnitSizeResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await Project(db.Set<UnitSize>().AsNoTracking().Where(s => s.Id == id)).SingleOrDefaultAsync(cancellationToken)
            ?? throw SitesErrors.NotFound("Unit size");

    public async Task<UnitSizeResponse> CreateAsync(CreateUnitSizeBody body, CancellationToken cancellationToken)
    {
        var size = new UnitSize
        {
            Key = body.Key!.Trim(), Label = Patch.Required(body.Label!, "Label"), DisplayName = Patch.Blank(body.DisplayName),
            Description = Patch.Blank(body.Description), UnitGroup = Patch.Blank(body.UnitGroup), ImagePath = Patch.Blank(body.ImagePath),
            MinAreaSqft = Area(body.MinAreaSqft), MaxAreaSqft = Area(body.MaxAreaSqft), SortOrder = body.SortOrder ?? 0,
            StorageTypeId = body.StorageTypeId,
        };
        await SaveAndRemapAsync(size, add: true, cancellationToken);
        return await GetAsync(size.Id, cancellationToken);
    }

    public async Task<UnitSizeResponse> UpdateAsync(Guid id, UpdateUnitSizeBody body, CancellationToken cancellationToken)
    {
        var size = await db.Set<UnitSize>().SingleOrDefaultAsync(s => s.Id == id, cancellationToken) ?? throw SitesErrors.NotFound("Unit size");
        db.Entry(size).Property(s => s.Version).OriginalValue = body.Version!.Value;   // stale → 409
        var clear = Patch.Clears(body.Clear, Clearable);
        if (body.Key is not null) size.Key = Patch.Required(body.Key, "Key");   // the pattern lets "" through
        if (body.Label is not null) size.Label = Patch.Required(body.Label, "Label");
        if (body.SortOrder is { } order) size.SortOrder = order;
        Patch.Value(Area(body.MinAreaSqft), "minAreaSqft", clear, v => size.MinAreaSqft = v);
        Patch.Value(Area(body.MaxAreaSqft), "maxAreaSqft", clear, v => size.MaxAreaSqft = v);
        Patch.Value(body.StorageTypeId, "storageTypeId", clear, v => size.StorageTypeId = v);
        Patch.Text(body.DisplayName, v => size.DisplayName = v);
        Patch.Text(body.Description, v => size.Description = v);
        Patch.Text(body.UnitGroup, v => size.UnitGroup = v);
        Patch.Text(body.ImagePath, v => size.ImagePath = v);
        await SaveAndRemapAsync(size, add: false, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, uint version, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Before the check: a unit refresh maps units only under this lock, so none can take the size between the check and the delete.
        await UnitSizeMap.LockAsync(db, cancellationToken);
        var size = await db.Set<UnitSize>().SingleOrDefaultAsync(s => s.Id == id, cancellationToken) ?? throw SitesErrors.NotFound("Unit size");
        db.Entry(size).Property(s => s.Version).OriginalValue = version;
        if (await db.Set<Unit>().AnyAsync(u => u.UnitSizeId == id, cancellationToken)) throw SitesErrors.UnitSizeInUse();
        db.Remove(size);
        await db.SaveChangesAsync(cancellationToken);
        // No remap: a size that holds no unit is no unit's first match, so removing it moves none.
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task SaveAndRemapAsync(UnitSize size, bool add, CancellationToken cancellationToken)
    {
        if (size.MinAreaSqft is { } min && size.MaxAreaSqft is { } max && min > max)
            throw SitesErrors.Invalid("The minimum area must not be above the maximum.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Before the save locks the size row: a unit refresh takes this lock first too, so the two cannot deadlock.
        await UnitSizeMap.LockAsync(db, cancellationToken);
        if (add) db.Add(size);
        await db.SaveChangesAsync(cancellationToken);
        await UnitSizeMap.RemapAsync(db, null, time, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // Validated as stored: numeric(10,2) would round a third decimal away silently and move a range boundary.
    private static decimal? Area(decimal? area) =>
        area is not { } value || decimal.Round(value, 2) == value ? area : throw SitesErrors.Invalid("An area can have at most 2 decimals.");

    private IQueryable<UnitSizeResponse> Project(IQueryable<UnitSize> sizes) =>
        from s in sizes
        join t in db.Set<StorageType>() on s.StorageTypeId equals (Guid?)t.Id into types
        from t in types.DefaultIfEmpty()
        select new UnitSizeResponse(s.Id, s.Key, s.Label, s.DisplayName, s.Description, s.UnitGroup, s.ImagePath, s.MinAreaSqft, s.MaxAreaSqft,
            s.SortOrder, s.StorageTypeId, t == null ? null : t.Name, s.Version);
}
