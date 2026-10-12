namespace ExtraSpace.Api.Platform.Errors;

/// <summary>Stable codes the app and admin UI switch on (S0 spec §4.1). Each feature adds its own in its own spec.</summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string Conflict = "CONFLICT";
    public const string RateLimited = "RATE_LIMITED";
    public const string AppUpdateRequired = "APP_UPDATE_REQUIRED";
    public const string InvalidCode = "INVALID_CODE";
    public const string TooManyAttempts = "TOO_MANY_ATTEMPTS";
    public const string InternalError = "INTERNAL_ERROR";

    /// <summary>The code for a response that carries none of its own.</summary>
    public static string ForStatus(int status) => status switch
    {
        400 => ValidationFailed,
        401 => Unauthenticated,
        403 => Forbidden,
        404 => NotFound,
        409 => Conflict,
        426 => AppUpdateRequired,
        429 => RateLimited,
        >= 500 => InternalError,
        _ => $"HTTP_{status}",
    };
}
