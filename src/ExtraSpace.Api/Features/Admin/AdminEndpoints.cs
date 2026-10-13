using ExtraSpace.Api.Platform.Logging;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace ExtraSpace.Api.Features.Admin;

internal static class AdminEndpoints
{
    /// <summary>Every route of the group needs a signed-in staff member unless it says <c>.AllowAnonymous()</c>.</summary>
    public static RouteGroupBuilder RequireStaff(this RouteGroupBuilder group)
    {
        group.RequireAuthorization(StaffAuth.Policy);
        group.AddEndpointFilter<AdminRequestFilter>();
        return group;
    }

    public static RouteGroupBuilder MapAdminEndpoints(this RouteGroupBuilder admin)
    {
        var auth = admin.MapGroup("/auth").WithTags("Admin auth");
        // Anonymous: the emailed code is the credential. AdminRequestFilter skips anonymous routes (no CSRF check, no audit).
        auth.MapPost("/code", RequestCode).AllowAnonymous().RequireRateLimiting(StaffAuth.CodeRateLimit);
        auth.MapPost("/code/verify", VerifyCode).AllowAnonymous().RequireRateLimiting(StaffAuth.VerifyRateLimit);
        auth.MapGet("/csrf", Csrf);
        // Cast: a handler taking only HttpContext binds the RequestDelegate overload, which discards the result (200, not 204; ASP0016).
        auth.MapPost("/logout", (Delegate)Logout);

        admin.MapGet("/me", GetMe).WithTags("Admin");

        // Groups without a prefix: a group's own "" route has the template text "/admin/api/staff/", which the audit action and log route would carry.
        var staff = admin.MapGroup("").WithTags("Staff").RequireAuthorization(AdminPermissions.StaffManage);
        staff.MapGet("/staff", ListStaff);
        staff.MapPost("/staff", AddStaff);
        staff.MapPatch("/staff/{id:guid}", UpdateStaff);

        var roles = admin.MapGroup("").WithTags("Roles");
        roles.MapGet("/roles", ListRoles);
        roles.MapPost("/roles", CreateRole).RequireAuthorization(AdminPermissions.RolesManage);
        roles.MapPatch("/roles/{id:guid}", UpdateRole).RequireAuthorization(AdminPermissions.RolesManage);
        roles.MapDelete("/roles/{id:guid}", DeleteRole).RequireAuthorization(AdminPermissions.RolesManage);

        admin.MapGet("/permissions", ListPermissions).WithTags("Roles");
        admin.MapGet("/audit", SearchAudit).WithTags("Audit").RequireAuthorization(AdminPermissions.AuditRead);
        return admin;
    }

    // Always 202: the answer never tells whether the address belongs to a staff member.
    private static async Task<Accepted> RequestCode(StaffCodeBody body, HttpContext http, StaffSignIn signIn, CancellationToken cancellationToken)
    {
        await signIn.RequestCodeAsync(body.Email!, http.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<NoContent> VerifyCode(StaffVerifyBody body, HttpContext http, StaffSignIn signIn, CancellationToken cancellationToken)
    {
        var staffId = await signIn.VerifyCodeAsync(body.Email!, body.Code!, cancellationToken);
        RequestLog.Add(http, "staffId", staffId);
        await http.SignInAsync(StaffAuth.CookieScheme, StaffAuth.PrincipalFor(staffId));   // the cookie keeps only the id
        return TypedResults.NoContent();
    }

    private static Ok<CsrfResponse> Csrf(HttpContext http, IAntiforgery antiforgery) =>
        TypedResults.Ok(new CsrfResponse(antiforgery.GetAndStoreTokens(http).RequestToken!));

    private static async Task<NoContent> Logout(HttpContext http)
    {
        await http.SignOutAsync(StaffAuth.CookieScheme);
        return TypedResults.NoContent();
    }

    private static Ok<AdminMeResponse> GetMe(HttpContext http) =>
        http.StaffSession() is { } s
            ? TypedResults.Ok(new AdminMeResponse(s.Id, s.Email, s.DisplayName, s.RoleId, s.RoleName, s.IsOwner, s.Permissions))
            : throw AdminErrors.Unauthenticated();

    private static StaffSession Actor(HttpContext http) => http.StaffSession() ?? throw AdminErrors.Unauthenticated();

    private static async Task<Ok<List<StaffResponse>>> ListStaff(StaffService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.ListAsync(cancellationToken));

    private static async Task<Created<StaffResponse>> AddStaff(AddStaffBody body, HttpContext http, StaffService service, CancellationToken cancellationToken) =>
        TypedResults.Created((string?)null, await service.AddAsync(Actor(http), body, cancellationToken));

    private static async Task<Ok<StaffResponse>> UpdateStaff(Guid id, UpdateStaffBody body, HttpContext http, StaffService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.UpdateAsync(Actor(http), id, body, cancellationToken));

    private static async Task<Ok<List<RoleResponse>>> ListRoles(RoleService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.ListAsync(cancellationToken));

    private static async Task<Created<RoleResponse>> CreateRole(CreateRoleBody body, RoleService service, CancellationToken cancellationToken) =>
        TypedResults.Created((string?)null, await service.CreateAsync(body, cancellationToken));

    private static async Task<Ok<RoleResponse>> UpdateRole(Guid id, UpdateRoleBody body, RoleService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.UpdateAsync(id, body, cancellationToken));

    private static async Task<NoContent> DeleteRole(Guid id, [FromQuery] uint version, RoleService service, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, version, cancellationToken);
        return TypedResults.NoContent();
    }

    private static Ok<List<PermissionResponse>> ListPermissions(PermissionCatalog catalog) =>
        TypedResults.Ok(catalog.All.Select(p => new PermissionResponse(p.Name, p.Description)).ToList());

    // from/to bind as text: DateTimeOffset binding would read a value without an offset in the server's time zone.
    private static async Task<Ok<AuditPage>> SearchAudit(Guid? staffId, string? entityType, Guid? entityId, string? from, string? to,
        AuditQuery audit, CancellationToken cancellationToken, int page = 1, int pageSize = 50) =>
        TypedResults.Ok(await audit.SearchAsync(staffId, entityType, entityId, from, to, page, pageSize, cancellationToken));
}
