using ExtraSpace.Api.Platform.Data;
using ExtraSpace.Api.Platform.Logging;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace ExtraSpace.Api.Features.Admin;

internal sealed class StaffAccess(AppDbContext db, PermissionCatalog catalog)
{
    public async Task<StaffSession?> LoadAsync(Guid staffId, CancellationToken cancellationToken)
    {
        var row = await db.Set<StaffUser>().Where(s => s.Id == staffId && s.Active)
            .Select(s => new { s.Id, s.Email, s.DisplayName, s.RoleId, RoleName = s.Role.Name, s.Role.Permissions })
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null) return null;
        var isOwner = row.RoleId == Role.OwnerId;
        var permissions = isOwner ? catalog.Names : catalog.Names.Where(row.Permissions.Contains).ToList();
        return new StaffSession(row.Id, row.Email, row.DisplayName, row.RoleId, row.RoleName, isOwner, permissions);
    }

    /// <summary>Cookie OnValidatePrincipal: a deactivation or role change applies at the very next request (plan decision 5).</summary>
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var http = context.HttpContext;
        var session = context.Principal?.StaffId() is { } staffId
            ? await http.RequestServices.GetRequiredService<StaffAccess>().LoadAsync(staffId, http.RequestAborted)
            : null;
        if (session is null)
        {
            context.RejectPrincipal();
            await http.SignOutAsync(StaffAuth.CookieScheme);
            return;
        }
        context.ReplacePrincipal(StaffAuth.PrincipalFor(session.Id, session.Permissions));
        http.Items[typeof(StaffSession)] = session;
        // Here, not in a route filter: 403s, binding failures and /admin/jobs lines carry the staff id too (S0 spec §4.4).
        RequestLog.Add(http, "staffId", session.Id);
    }
}
