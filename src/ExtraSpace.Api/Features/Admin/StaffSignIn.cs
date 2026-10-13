using ExtraSpace.Api.Platform.Data;
using ExtraSpace.Api.Platform.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExtraSpace.Api.Features.Admin;

internal sealed partial class StaffSignIn(AppDbContext db, IOptions<AdminOptions> options, TimeProvider time, ILogger<StaffSignIn> logger)
{
    /// <summary>The staff id to sign in, or null when this person may not (unknown, inactive, linked to another Entra user).</summary>
    public async Task<Guid?> ResolveAsync(string? objectId, string? email, string? name, CancellationToken cancellationToken)
    {
        email = email?.Trim().ToLowerInvariant();
        var bootstrapped = false;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var staff = objectId is null ? null : await db.Set<StaffUser>().FirstOrDefaultAsync(s => s.EntraObjectId == objectId, cancellationToken);
        if (staff is null && email is not null)
        {
            staff = await db.Set<StaffUser>().FirstOrDefaultAsync(s => s.Email == email, cancellationToken);
            if (staff is { EntraObjectId: { } linked } && objectId is not null && linked != objectId)
                return Refuse(email, "linked to another Entra user");
            if (staff is null && options.Value.BootstrapEmails.Any(b => b.Trim().Equals(email, StringComparison.OrdinalIgnoreCase)))
            {
                await StaffLocks.OwnersAsync(db, cancellationToken);
                if (!await db.Set<StaffUser>().AnyAsync(s => s.Active && s.RoleId == Role.OwnerId, cancellationToken))
                {
                    staff = new StaffUser { Email = email, RoleId = Role.OwnerId };
                    db.Add(staff);
                    bootstrapped = true;
                }
            }
        }
        if (staff is null) return Refuse(email, "unknown");
        if (!staff.Active) return Refuse(email, "inactive");

        staff.EntraObjectId ??= objectId;
        if (string.IsNullOrWhiteSpace(staff.DisplayName)) staff.DisplayName = name;
        staff.LastSignInAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (bootstrapped) LogBootstrap(logger, staff.Id);
        LogSignedIn(logger, staff.Id);
        return staff.Id;
    }

    private Guid? Refuse(string? email, string reason)
    {
        LogRefused(logger, email is null ? "(none)" : Pii.MaskEmail(email), reason);
        return null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Staff {staffId} signed in")]
    private static partial void LogSignedIn(ILogger logger, Guid staffId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Staff sign-in refused for {email}: {reason}")]
    private static partial void LogRefused(ILogger logger, string email, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Bootstrap Owner {staffId} created")]
    private static partial void LogBootstrap(ILogger logger, Guid staffId);
}
