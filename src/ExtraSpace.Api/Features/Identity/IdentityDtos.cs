using System.ComponentModel.DataAnnotations;

namespace ExtraSpace.Api.Features.Identity;

internal sealed record MeResponse(Guid Id, string Email, string? FirstName, string? LastName, string? Phone);

internal sealed record TokenResponse(string AccessToken, DateTime AccessTokenExpiresAt, string RefreshToken, DateTime RefreshTokenExpiresAt);

/// <summary>The outcome of a sign-in or refresh: the tokens for the app, the id for the request log.</summary>
internal sealed record SignIn(Guid CustomerId, TokenResponse Tokens);

// Request bodies are public: the .NET 10 validation generator skips non-public types (the 400 tests fail with internal).
public sealed class RequestCodeBody
{
    [Required, EmailAddress, StrictEmail, MaxLength(254)]
    public string? Email { get; init; }
}

public sealed class VerifyCodeBody
{
    [Required, EmailAddress, StrictEmail, MaxLength(254)]
    public string? Email { get; init; }

    [Required, RegularExpression("^[0-9]{6}$")]
    public string? Code { get; init; }
}

public sealed class RefreshBody
{
    [Required, MaxLength(128)]
    public string? RefreshToken { get; init; }
}
