using ExtraSpace.Api.Platform.Data;
using ExtraSpace.Api.Platform.Jobs;
using Microsoft.EntityFrameworkCore;

namespace ExtraSpace.Api.Features.Identity;

/// <summary>Daily clean-up (S0 spec §7). Idempotent: it only deletes rows past a cut-off, so a second run deletes nothing.</summary>
internal sealed partial class IdentityHousekeepingJob(AppDbContext db, TimeProvider time, ILogger<IdentityHousekeepingJob> logger) : IRecurringJob
{
    private static readonly TimeSpan CodeRetention = TimeSpan.FromDays(1);
    private static readonly TimeSpan TokenRetention = TimeSpan.FromDays(30);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var codeCutoff = now - CodeRetention;
        var tokenCutoff = now - TokenRetention;
        // No per-email advisory lock: PostgreSQL re-checks expires_at on a row a code request just renewed, so a fresh code is never deleted.
        var codes = await db.Set<LoginCode>().Where(c => c.ExpiresAt < codeCutoff).ExecuteDeleteAsync(cancellationToken);
        var tokens = await db.Set<CustomerRefreshToken>().Where(t => t.ExpiresAt < tokenCutoff).ExecuteDeleteAsync(cancellationToken);
        LogDone(logger, codes, tokens);
    }

    // Not {tokens}: a property named like *token* is masked to *** (S0 spec §4.4).
    [LoggerMessage(Level = LogLevel.Information, Message = "Identity housekeeping deleted {codeRows} login codes and {refreshRows} refresh tokens")]
    private static partial void LogDone(ILogger logger, int codeRows, int refreshRows);
}
