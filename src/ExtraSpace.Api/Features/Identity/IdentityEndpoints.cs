using System.Security.Claims;
using ExtraSpace.Api.Features.Identity.Contracts;
using ExtraSpace.Api.Platform.Errors;
using ExtraSpace.Api.Platform.Logging;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ExtraSpace.Api.Features.Identity;

internal static class IdentityEndpoints
{
    /// <summary>Every route of the group needs a signed-in customer unless it says <c>.AllowAnonymous()</c>.</summary>
    public static RouteGroupBuilder RequireCustomer(this RouteGroupBuilder group)
    {
        group.RequireAuthorization(CustomerAuth.Policy);
        group.AddEndpointFilter(static (context, next) =>
        {
            if (context.HttpContext.User.CustomerId() is { } customerId) RequestLog.Add(context.HttpContext, "customerId", customerId);
            return next(context);
        });
        return group;
    }

    public static RouteGroupBuilder MapIdentityEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").AllowAnonymous().WithTags("Auth");
        auth.MapPost("/code", RequestCode).RequireRateLimiting(CustomerAuth.CodeRateLimit);
        auth.MapPost("/code/verify", VerifyCode).RequireRateLimiting(CustomerAuth.VerifyRateLimit);
        // Anonymous: the refresh token in the body is the credential, and an expired access token must still sign out.
        auth.MapPost("/refresh", Refresh);
        auth.MapPost("/logout", Logout);

        api.MapGet("/me", GetMe).WithTags("Me");
        return api;
    }

    private static async Task<Ok<MeResponse>> GetMe(ClaimsPrincipal user, CustomerService customers, CancellationToken cancellationToken) =>
        TypedResults.Ok(await customers.GetMeAsync(user.CustomerId() ?? throw IdentityErrors.Unauthenticated(), cancellationToken)
            ?? throw IdentityErrors.Unauthenticated());

    private static async Task<Accepted> RequestCode(RequestCodeBody body, HttpContext http, LoginCodeService codes, CancellationToken cancellationToken)
    {
        await codes.SendAsync(LoginCodePurpose.Customer, body.Email!, Ip(http), cancellationToken);
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Ok<TokenResponse>> VerifyCode(VerifyCodeBody body, HttpContext http, LoginCodeService codes, CancellationToken cancellationToken)
    {
        var signIn = await codes.VerifyCustomerAsync(body.Email!, body.Code!, Ip(http), UserAgent(http), cancellationToken);
        RequestLog.Add(http, "customerId", signIn.CustomerId);
        return TypedResults.Ok(signIn.Tokens);
    }

    private static async Task<Ok<TokenResponse>> Refresh(RefreshBody body, HttpContext http, TokenService tokens, CancellationToken cancellationToken)
    {
        var signIn = await tokens.RefreshAsync(body.RefreshToken!, Ip(http), UserAgent(http), cancellationToken);
        RequestLog.Add(http, "customerId", signIn.CustomerId);
        return TypedResults.Ok(signIn.Tokens);
    }

    private static async Task<NoContent> Logout(RefreshBody body, TokenService tokens, CancellationToken cancellationToken)
    {
        await tokens.LogoutAsync(body.RefreshToken!, cancellationToken);
        return TypedResults.NoContent();
    }

    private static string? Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();

    private static string? UserAgent(HttpContext http) => http.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;
}

internal static class IdentityErrors
{
    public static AppException Unauthenticated() => new(401, ErrorCodes.Unauthenticated, "Sign in again.");

    public static AppException RateLimited(TimeSpan wait) =>
        new(429, ErrorCodes.RateLimited, "Too many requests. Try again later.") { RetryAfter = wait };

    public static AppException InvalidCode() => new(400, ErrorCodes.InvalidCode, "The code is wrong or has expired.");

    public static AppException TooManyAttempts() => new(400, ErrorCodes.TooManyAttempts, "Too many wrong codes. Ask for a new code.");
}
