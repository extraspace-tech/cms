using ExtraSpace.Api.Platform.Data;
using ExtraSpace.Api.Platform.Http;
using Microsoft.EntityFrameworkCore;

namespace ExtraSpace.Api.Features.Sites.Admin;

internal sealed class StorageTypeAdminService(AppDbContext db)
{
    public Task<Paged<StorageTypeResponse>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var rows = db.Set<StorageType>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = Paging.Contains(search);
            rows = rows.Where(t => EF.Functions.ILike(t.Key, pattern, Paging.Escape) || EF.Functions.ILike(t.Name, pattern, Paging.Escape));
        }
        return rows.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Select(t => new StorageTypeResponse(t.Id, t.Key, t.Name, t.DisplayName, t.IconPath, t.ImagePath, t.SortOrder, t.Active, t.Version))
            .ToPageAsync(page, pageSize, cancellationToken);
    }

    public async Task<StorageTypeResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Response(await db.Set<StorageType>().AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw SitesErrors.NotFound("Storage type"));

    public async Task<StorageTypeResponse> CreateAsync(CreateStorageTypeBody body, CancellationToken cancellationToken)
    {
        var type = new StorageType
        {
            Key = body.Key!.Trim(), Name = Patch.Required(body.Name!, "Name"), DisplayName = Patch.Blank(body.DisplayName),
            IconPath = Patch.Blank(body.IconPath), ImagePath = Patch.Blank(body.ImagePath), SortOrder = body.SortOrder ?? 0, Active = body.Active ?? true,
        };
        db.Add(type);
        await db.SaveChangesAsync(cancellationToken);   // a taken key → unique violation → 409 CONFLICT
        return Response(type);
    }

    public async Task<StorageTypeResponse> UpdateAsync(Guid id, UpdateStorageTypeBody body, CancellationToken cancellationToken)
    {
        var type = await db.Set<StorageType>().SingleOrDefaultAsync(t => t.Id == id, cancellationToken) ?? throw SitesErrors.NotFound("Storage type");
        db.Entry(type).Property(t => t.Version).OriginalValue = body.Version!.Value;   // stale → 409
        if (body.Name is not null) type.Name = Patch.Required(body.Name, "Name");
        if (body.SortOrder is { } order) type.SortOrder = order;
        if (body.Active is { } active) type.Active = active;
        Patch.Text(body.DisplayName, v => type.DisplayName = v);
        Patch.Text(body.IconPath, v => type.IconPath = v);
        Patch.Text(body.ImagePath, v => type.ImagePath = v);
        await db.SaveChangesAsync(cancellationToken);
        return Response(type);
    }

    public async Task DeleteAsync(Guid id, uint version, CancellationToken cancellationToken)
    {
        var type = await db.Set<StorageType>().SingleOrDefaultAsync(t => t.Id == id, cancellationToken) ?? throw SitesErrors.NotFound("Storage type");
        db.Entry(type).Property(t => t.Version).OriginalValue = version;
        if (id == StorageType.SelfStorageId || id == StorageType.WorkspaceId)
            throw SitesErrors.StorageTypeInUse("The unit sync uses this storage type. It cannot be deleted.");   // found by key
        if (await db.Set<Unit>().AnyAsync(u => u.StorageTypeId == id, cancellationToken)
            || await db.Set<UnitSize>().AnyAsync(s => s.StorageTypeId == id, cancellationToken)) throw SitesErrors.StorageTypeInUse();
        db.Remove(type);
        await db.SaveChangesAsync(cancellationToken);   // a unit or size linked since the check → foreign-key violation → 409 CONFLICT
    }

    private static StorageTypeResponse Response(StorageType t) =>
        new(t.Id, t.Key, t.Name, t.DisplayName, t.IconPath, t.ImagePath, t.SortOrder, t.Active, t.Version);
}
