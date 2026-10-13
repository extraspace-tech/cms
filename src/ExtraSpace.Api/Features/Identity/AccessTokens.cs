using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ExtraSpace.Api.Features.Identity;

/// <summary>Issues the 15-minute customer JWT (S0 spec §6.1): HS256, claims sub, email, iss, aud.</summary>
internal sealed class AccessTokens(IOptions<CustomerAuthOptions> options, TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    private readonly JsonWebTokenHandler _handler = new();

    public (string Token, DateTime ExpiresAt) Issue(Guid customerId, string email)
    {
        var auth = options.Value;
        // JWT times are whole seconds; rounding first makes accessTokenExpiresAt equal the token's exp.
        var now = time.GetUtcNow().UtcDateTime;
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
        var expires = now + Lifetime;
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = auth.Issuer,
            Audience = auth.Audience,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = customerId.ToString(),
                [JwtRegisteredClaimNames.Email] = email,
            },
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(auth.SigningKeyBytes()), SecurityAlgorithms.HmacSha256),
        });
        return (token, expires);
    }
}
