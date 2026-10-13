using ExtraSpace.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace ExtraSpace.Api.Features.Admin;

internal static class StaffLocks
{
    /// <summary>Serialises every write that can change who is an active Owner (bootstrap, role/active changes). Inside a transaction.</summary>
    public static Task OwnersAsync(AppDbContext db, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended('staff-owners', 0))", cancellationToken);
}
