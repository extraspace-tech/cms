using System.Security.Cryptography;
using System.Text;
using ExtraSpace.Api.Features.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace ExtraSpace.Api.Features.Identity;

/// <summary>
/// HMAC-SHA256 of <c>purpose:email:code</c> with a key derived from the signing key (plan decision 5): a 6-digit code hashed
/// without a secret could be recovered from a database copy in milliseconds.
/// </summary>
internal sealed class LoginCodeHasher(IOptions<CustomerAuthOptions> options)
{
    private readonly byte[] _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, options.Value.SigningKeyBytes(), 32,
        info: Encoding.UTF8.GetBytes("login-code"));

    public static string NewCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    public string Hash(LoginCodePurpose purpose, string email, string code) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(purpose + ":" + email + ":" + code)));

    public bool Matches(string storedHash, LoginCodePurpose purpose, string email, string code) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(storedHash), Encoding.ASCII.GetBytes(Hash(purpose, email, code)));
}
