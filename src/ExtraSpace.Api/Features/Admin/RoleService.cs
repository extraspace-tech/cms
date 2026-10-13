using ExtraSpace.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace ExtraSpace.Api.Features.Admin;

internal sealed class RoleService(AppDbContext db, PermissionCatalog catalog)
{
    public async Task<List<RoleResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var roles = await Rows(db.Set<Role>().OrderByDescending(r => r.IsSystem).ThenBy(r => r.Name)).ToListAsync(cancellationToken);
        return roles.Select(Response).ToList();
    }

    public async Task<RoleResponse> CreateAsync(CreateRoleBody body, CancellationToken cancellationToken)
    {
        var role = new Role { Name = CheckedName(body.Name!), Permissions = CheckedPermissions(body.Permissions!) };
        db.Add(role);
        await db.SaveChangesAsync(cancellationToken);   // same name ignoring case → unique violation → 409 CONFLICT
        return await GetAsync(role.Id, cancellationToken);
    }

    public async Task<RoleResponse> UpdateAsync(Guid id, UpdateRoleBody body, CancellationToken cancellationToken)
    {
        var role = await FindEditableAsync(id, body.Version!.Value, cancellationToken);
        if (body.Name is not null) role.Name = CheckedName(body.Name);
        if (body.Permissions is not null) role.Permissions = CheckedPermissions(body.Permissions);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, uint version, CancellationToken cancellationToken)
    {
        var role = await FindEditableAsync(id, version, cancellationToken);
        if (await db.Set<StaffUser>().AnyAsync(s => s.RoleId == id, cancellationToken)) throw AdminErrors.RoleInUse();
        db.Remove(role);
        await db.SaveChangesAsync(cancellationToken);   // a role assigned since the check → foreign-key violation → 409 CONFLICT
    }

    private async Task<Role> FindEditableAsync(Guid id, uint version, CancellationToken cancellationToken)
    {
        var role = await db.Set<Role>().FirstOrDefaultAsync(r => r.Id == id, cancellationToken) ?? throw AdminErrors.NotFound("Role");
        if (role.IsSystem) throw AdminErrors.SystemRole();
        db.Entry(role).Property(r => r.Version).OriginalValue = version;   // stale → DbUpdateConcurrencyException → 409
        return role;
    }

    private static string CheckedName(string name) =>
        name.Trim() is { Length: > 0 } trimmed ? trimmed : throw AdminErrors.Invalid("The role name cannot be blank.");

    private string[] CheckedPermissions(string[] permissions)
    {
        // A JSON null element arrives as a null string: unknown too.
        foreach (var permission in permissions)
            if (permission is null || !catalog.Contains(permission)) throw AdminErrors.Invalid($"Unknown permission '{permission ?? "null"}'.");
        return permissions.Distinct().Order(StringComparer.Ordinal).ToArray();
    }

    private async Task<RoleResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Response(await Rows(db.Set<Role>().Where(r => r.Id == id)).SingleAsync(cancellationToken));

    private IQueryable<RoleRow> Rows(IQueryable<Role> roles) =>
        roles.Select(r => new RoleRow(r.Id, r.Name, r.Permissions, r.IsSystem, db.Set<StaffUser>().Count(s => s.RoleId == r.Id), r.Version));

    // Owner always has every permission; other roles show only what is still in the catalogue, so a PATCH of what GET returned passes.
    private RoleResponse Response(RoleRow r) =>
        new(r.Id, r.Name, r.Id == Role.OwnerId ? catalog.Names : r.Permissions.Where(catalog.Contains).ToList(), r.IsSystem, r.StaffCount, r.Version);

    private sealed record RoleRow(Guid Id, string Name, string[] Permissions, bool IsSystem, int StaffCount, uint Version);
}
