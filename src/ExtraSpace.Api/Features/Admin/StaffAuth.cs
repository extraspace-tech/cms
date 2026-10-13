using System.Security.Claims;
using System.Text.RegularExpressions;
using ExtraSpace.Api.Features.Admin.Contracts;
using ExtraSpace.Api.Platform.Errors;
using ExtraSpace.Api.Platform.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace ExtraSpace.Api.Features.Admin;

internal static partial class StaffAuth
{
    public const string CookieScheme = "Staff";
    public const string EntraScheme = "Entra";
    public const string Policy = "Staff";
    public const string PermissionClaim = "perm";
    public const string CookieName = "__Host-es-admin";
    public const string CsrfCookieName = "__Host-es-csrf";
    // Development runs on plain http (local walk, Vite dev server), and a __Host- cookie must be Secure.
    public const string DevCookieName = "es-admin";
    public const string DevCsrfCookieName = "es-csrf";
    public const string CsrfHeader = "X-CSRF-TOKEN";
    public const string CallbackPath = "/admin/api/auth/callback";

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

    public static IServiceCollection AddStaffAuth(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var entra = configuration.GetSection(AdminOptions.Section).Get<AdminOptions>()?.Entra ?? new EntraOptions();
        var role = configuration.GetSection(AppOptions.Section).Get<AppOptions>()?.Role ?? AppRole.All;
        var dev = environment.IsDevelopment();
        var secure = dev ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        var authentication = services.AddAuthentication().AddCookie(CookieScheme, o =>
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

        if (entra.IsConfigured)
            authentication.AddOpenIdConnect(EntraScheme, o =>
            {
                o.Authority = $"https://login.microsoftonline.com/{entra.TenantId}/v2.0";
                o.ClientId = entra.ClientId;
                o.ClientSecret = entra.ClientSecret;
                o.ResponseType = OpenIdConnectResponseType.Code;
                o.UsePkce = true;
                o.CallbackPath = CallbackPath;
                o.SignInScheme = CookieScheme;
                o.MapInboundClaims = false;
                o.SaveTokens = false;
                // No front-channel sign-out: /signout-oidc would otherwise sign staff out from any cross-site GET. An empty path turns it off.
                o.RemoteSignOutPath = PathString.Empty;
                o.Events = new OpenIdConnectEvents { OnTokenValidated = OnTokenValidatedAsync, OnRemoteFailure = OnRemoteFailureAsync };
            });
        // The jobs task serves no sign-in, so it runs without the Entra secret.
        else if (environment.IsProduction() && role is not AppRole.Jobs)
            throw new InvalidOperationException("Admin:Entra:TenantId, ClientId and ClientSecret must be set in Production.");

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
        return services;
    }

    internal static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        var user = context.Principal!;
        var http = context.HttpContext;
        // Only dev sign-in may resolve without an object id; an Entra token always carries `oid`.
        var objectId = user.FindFirstValue("oid");
        var staffId = objectId is null ? null : await http.RequestServices.GetRequiredService<StaffSignIn>().ResolveAsync(
            objectId, user.FindFirstValue("preferred_username") ?? user.FindFirstValue("email"),
            user.FindFirstValue("name"), http.RequestAborted);
        if (staffId is null)
        {
            if (objectId is null) LogNoObjectId(Logger(http));
            context.HandleResponse();
            var error = AdminErrors.NoAccess();
            await ProblemWriter.WriteAsync(http, error.Status, error.Code, error.Title);
            return;
        }
        context.Principal = PrincipalFor(staffId.Value);   // the cookie keeps only the id (plan decision 5)
        context.Properties!.RedirectUri = "/admin";
    }

    internal static async Task OnRemoteFailureAsync(RemoteFailureContext context)
    {
        if (context.Failure is { } failure)
            LogRemoteFailure(Logger(context.HttpContext), failure.GetType().Name, OpaqueValues().Replace(failure.Message, "[redacted]"));
        context.HandleResponse();
        await ProblemWriter.WriteAsync(context.HttpContext, 401, ErrorCodes.Unauthenticated, "Sign-in did not complete. Try again.");
    }

    private static ILogger Logger(HttpContext http) =>
        http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(StaffAuth).FullName!);

    // Long runs without spaces: the framework puts the raw id_token into "Unable to validate the 'id_token'" messages.
    [GeneratedRegex(@"[A-Za-z0-9._~+/=\-]{40,}")]
    private static partial Regex OpaqueValues();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Staff sign-in did not complete: {failureType}: {failureMessage}")]
    private static partial void LogRemoteFailure(ILogger logger, string failureType, string failureMessage);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Staff sign-in refused: the Entra token has no oid claim")]
    private static partial void LogNoObjectId(ILogger logger);
}
