using ExtraSpace.Api.Features.Identity.Contracts;
using ExtraSpace.Api.Platform.Data;
using ExtraSpace.Api.Platform.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExtraSpace.Api.Features.Admin;

/// <summary>Staff sign in with their email and a one-time code (same code rules as customers, S0 spec §6.1).</summary>
internal sealed partial class StaffSignIn(
    AppDbContext db, ILoginCodes codes, IOptions<AdminOptions> options, TimeProvider time, ILogger<StaffSignIn> logger)
{
    // A sign-in saved inside the caller's transaction: its log lines wait for that commit.
    private (Guid StaffId, bool Bootstrapped)? _uncommitted;

    /// <summary>Whether a code may be sent to this address. The answer is checked again, under the lock, when the code is used.</summary>
    public async Task<bool> MaySignInAsync(string email, CancellationToken cancellationToken)
    {
        email = Normalize(email);
        if (!options.Value.AllowsDomain(email)) return false;
        var active = await db.Set<StaffUser>().Where(s => s.Email == email).Select(s => (bool?)s.Active).FirstOrDefaultAsync(cancellationToken);
        if (active is not null) return active.Value;
        return options.Value.IsBootstrap(email) && !await AnyActiveOwnerAsync(cancellationToken);
    }

    /// <summary>Sends a code when the address may sign in; for any other address nothing is stored and nothing is sent.</summary>
    public async Task RequestCodeAsync(string email, string? ip, CancellationToken cancellationToken)
    {
        if (await MaySignInAsync(email, cancellationToken))
            await codes.SendAsync(LoginCodePurpose.Staff, Normalize(email), ip, cancellationToken);
    }

    /// <summary>Checks the code and returns the staff id to sign in. 403 when the person may no longer sign in; the code then stays unused.</summary>
    public async Task<Guid> VerifyCodeAsync(string email, string code, CancellationToken cancellationToken)
    {
        var staffId = await codes.VerifyAsync(LoginCodePurpose.Staff, Normalize(email), code,
            async ct => await ResolveAsync(email, ct) ?? throw AdminErrors.NoAccess(), cancellationToken);
        LogCommitted();
        return staffId;
    }

    /// <summary>
    /// The staff id to sign in, or null when this person may not (unknown, inactive, domain not allowed).
    /// Joins the caller's transaction when there is one and never commits it; otherwise it runs in its own.
    /// </summary>
    public async Task<Guid?> ResolveAsync(string email, CancellationToken cancellationToken)
    {
        email = Normalize(email);
        if (!options.Value.AllowsDomain(email)) return Refuse(email, "domain not allowed");

        var bootstrapped = false;
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        var staff = await db.Set<StaffUser>().FirstOrDefaultAsync(s => s.Email == email, cancellationToken);
        if (staff is null && options.Value.IsBootstrap(email))
        {
            await StaffLocks.OwnersAsync(db, cancellationToken);
            if (!await AnyActiveOwnerAsync(cancellationToken))
            {
                staff = new StaffUser { Email = email, RoleId = Role.OwnerId };
                db.Add(staff);
                bootstrapped = true;
            }
        }
        if (staff is null) return Refuse(email, "unknown");
        if (!staff.Active) return Refuse(email, "inactive");

        staff.LastSignInAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        _uncommitted = (staff.Id, bootstrapped);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            LogCommitted();
        }
        return staff.Id;
    }

    private void LogCommitted()
    {
        if (_uncommitted is not { } signedIn) return;
        _uncommitted = null;
        if (signedIn.Bootstrapped) LogBootstrap(logger, signedIn.StaffId);
        LogSignedIn(logger, signedIn.StaffId);
    }

    private Task<bool> AnyActiveOwnerAsync(CancellationToken cancellationToken) =>
        db.Set<StaffUser>().AnyAsync(s => s.Active && s.RoleId == Role.OwnerId, cancellationToken);

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();

    private Guid? Refuse(string email, string reason)
    {
        LogRefused(logger, Pii.MaskEmail(email), reason);
        return null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Staff {staffId} signed in")]
    private static partial void LogSignedIn(ILogger logger, Guid staffId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Staff sign-in refused for {email}: {reason}")]
    private static partial void LogRefused(ILogger logger, string email, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Bootstrap Owner {staffId} created")]
    private static partial void LogBootstrap(ILogger logger, Guid staffId);
}
