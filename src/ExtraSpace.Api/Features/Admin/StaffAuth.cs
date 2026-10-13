using System.Security.Claims;
using System.Threading.RateLimiting;
using ExtraSpace.Api.Features.Admin.Contracts;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace ExtraSpace.Api.Features.Admin;

internal static class StaffAuth
{
    public const string CookieScheme = "Staff";
    public const string Policy = "Staff";
    public const string PermissionClaim = "perm";
    public const string CookieName = "__Host-es-admin";
    public const string CsrfCookieName = "__Host-es-csrf";
    // Development runs on plain http (local walk, Vite dev server), and a __Host- cookie must be Secure.
    public const string DevCookieName = "es-admin";
    public const string DevCsrfCookieName = "es-csrf";
    public const string CsrfHeader = "X-CSRF-TOKEN";
    public const string CodeRateLimit = "admin-auth-code";
    public const string VerifyRateLimit = "admin-auth-verify";

    public static ClaimsPrincipal PrincipalFor(Guid staffId, IEnumerable<string>? permissions = null)
    {
        var identity = new ClaimsIdentity(CookieScheme);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, staffId.ToString()));
        foreach (var permission in permissions ?? []) identity.AddClaim(new Claim(PermissionClaim, permission));
        return new ClaimsPrincipal(identity);
    }

    public static Guid? StaffId(this ClaimsPrincipal user) =>
        user.Identity is { IsAuthenticated: true, AuthenticationType: CookieScheme }
        && Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// <summary>Loaded by <see cref="StaffAccess.ValidateAsync"/> for this request; null when the request is not signed in as staff.</summary>
    public static StaffSession? StaffSession(this HttpContext http) => http.Items[typeof(StaffSession)] as StaffSession;

    public static IServiceCollection AddStaffAuth(this IServiceCollection services, IHostEnvironment environment)
    {
        var dev = environment.IsDevelopment();
        var secure = dev ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        services.AddAuthentication().AddCookie(CookieScheme, o =>
        {
            o.Cookie.Name = dev ? DevCookieName : CookieName;
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = secure;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.Path = "/";
            o.ExpireTimeSpan = TimeSpan.FromHours(8);
            o.SlidingExpiration = true;
            o.Events = new CookieAuthenticationEvents
            {
                // An API answers with a status, never a redirect (plan decision 6); the status-code pages write the problem.
                OnRedirectToLogin = c => { c.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; },
                OnRedirectToAccessDenied = c => { c.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; },
                OnValidatePrincipal = StaffAccess.ValidateAsync,
            };
        });

        services.AddAntiforgery(o =>
        {
            o.HeaderName = CsrfHeader;
            o.Cookie.Name = dev ? DevCsrfCookieName : CsrfCookieName;
            o.Cookie.SecurePolicy = secure;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.SuppressXFrameOptionsHeader = true;   // SecurityHeadersMiddleware sends DENY
        });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policy, p => p.AddAuthenticationSchemes(CookieScheme).RequireAuthenticatedUser());
        services.AddOptions<AuthorizationOptions>().Configure<IEnumerable<Permission>>((o, permissions) =>
        {
            foreach (var permission in permissions)
                o.AddPolicy(permission.Name, p => p.AddAuthenticationSchemes(CookieScheme).RequireClaim(PermissionClaim, permission.Name));
        });
        // Per IP, in memory, so per API task; the per-email limits live in the database (S0 spec §6.1).
        services.Configure<RateLimiterOptions>(o =>
        {
            o.AddPolicy(CodeRateLimit, http => PerIpPerHour(http, 20));
            o.AddPolicy(VerifyRateLimit, http => PerIpPerHour(http, 30));
        });
        return services;
    }

    private static RateLimitPartition<string> PerIpPerHour(HttpContext http, int permits) =>
        RateLimitPartition.GetFixedWindowLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromHours(1), QueueLimit = 0 });
}
