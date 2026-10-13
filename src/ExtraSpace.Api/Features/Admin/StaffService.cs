using ExtraSpace.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace ExtraSpace.Api.Features.Admin;

internal sealed class StaffService(AppDbContext db)
{
    public Task<List<StaffResponse>> ListAsync(CancellationToken cancellationToken) =>
        Responses(db.Set<StaffUser>().OrderBy(s => s.Email)).ToListAsync(cancellationToken);

    public async Task<StaffResponse> AddAsync(StaffSession actor, AddStaffBody body, CancellationToken cancellationToken)
    {
        var roleId = body.RoleId!.Value;
        if (roleId == Role.OwnerId && !actor.IsOwner) throw AdminErrors.OwnerOnly();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (roleId == Role.OwnerId)
        {
            await StaffLocks.OwnersAsync(db, cancellationToken);
            if (!await ActorIsActiveOwnerAsync(actor.Id, cancellationToken)) throw AdminErrors.OwnerOnly();
        }
        if (!await db.Set<Role>().AnyAsync(r => r.Id == roleId, cancellationToken)) throw AdminErrors.Invalid("The role does not exist.");
        var staff = new StaffUser { Email = body.Email!.Trim().ToLowerInvariant(), RoleId = roleId };
        db.Add(staff);
        await db.SaveChangesAsync(cancellationToken);   // a second row with the same email → unique violation → 409 CONFLICT
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(staff.Id, cancellationToken);
    }

    public async Task<StaffResponse> UpdateAsync(StaffSession actor, Guid id, UpdateStaffBody body, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (body.RoleId is not null || body.Active is not null) await StaffLocks.OwnersAsync(db, cancellationToken);
        var staff = await db.Set<StaffUser>().FirstOrDefaultAsync(s => s.Id == id, cancellationToken) ?? throw AdminErrors.NotFound("Staff member");
        db.Entry(staff).Property(s => s.Version).OriginalValue = body.Version!.Value;   // stale → DbUpdateConcurrencyException → 409

        if (body.Active == false && id == actor.Id) throw AdminErrors.CannotDeactivateSelf();
        if (body.RoleId is { } roleId && roleId != staff.RoleId && !await db.Set<Role>().AnyAsync(r => r.Id == roleId, cancellationToken))
            throw AdminErrors.Invalid("The role does not exist.");

        var wasOwner = staff.Active && staff.RoleId == Role.OwnerId;
        staff.RoleId = body.RoleId ?? staff.RoleId;
        staff.Active = body.Active ?? staff.Active;
        var isOwner = staff.Active && staff.RoleId == Role.OwnerId;

        if (wasOwner != isOwner || (body.RoleId == Role.OwnerId && db.Entry(staff).Property(s => s.RoleId).IsModified))
        {
            if (!await ActorIsActiveOwnerAsync(actor.Id, cancellationToken)) throw AdminErrors.OwnerOnly();
            if (wasOwner && !isOwner
                && !await db.Set<StaffUser>().AnyAsync(s => s.Id != id && s.Active && s.RoleId == Role.OwnerId, cancellationToken))
                throw AdminErrors.LastOwner();
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    // Under the staff-owners lock: the session was loaded before another Owner may have demoted this actor.
    private Task<bool> ActorIsActiveOwnerAsync(Guid actorId, CancellationToken cancellationToken) =>
        db.Set<StaffUser>().AnyAsync(s => s.Id == actorId && s.Active && s.RoleId == Role.OwnerId, cancellationToken);

    private Task<StaffResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Responses(db.Set<StaffUser>().Where(s => s.Id == id)).SingleAsync(cancellationToken);

    private static IQueryable<StaffResponse> Responses(IQueryable<StaffUser> staff) =>
        staff.Select(s => new StaffResponse(s.Id, s.Email, s.DisplayName, s.RoleId, s.Role.Name, s.Active, s.LastSignInAt, s.Version));
}
