using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExtraSpace.Api.Features.Identity;
using ExtraSpace.Api.Platform.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace ExtraSpace.Api.Tests.TestSupport;

internal static class IdentityTestData
{
    /// <summary>Base64 of 49 bytes; a public, test-only signing key.</summary>
    public const string SigningKey = "dGVzdC1vbmx5LXNpZ25pbmcta2V5LW5vdC1hLXNlY3JldC0wMTIzNDU2Nzg5YWJjZA==";

    public static string NewEmail(string prefix = "c") => $"{prefix}-{Guid.NewGuid():N}@example.test";

    public static async Task WithDbAsync(this ApiFactory api, Func<AppDbContext, Task> action)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public static async Task<Customer> CreateCustomerAsync(this ApiFactory api, string? email = null)
    {
        var customer = new Customer { Email = email ?? NewEmail(), FirstName = "Jane", LastName = "Tan", Phone = "+6590000000" };
        await api.WithDbAsync(async db =>
        {
            db.Add(customer);
            await db.SaveChangesAsync();
        });
        return customer;
    }

    public static string AccessTokenFor(this ApiFactory api, Customer customer) =>
        api.Services.GetRequiredService<AccessTokens>().Issue(customer.Id, customer.Email).Token;

    /// <summary>A token our app did not issue in this shape: another key, issuer, audience or algorithm, or a lifetime already over.</summary>
    /// <remarks>Signed by hand: the token library refuses HS512 with our 49-byte key, but an attacker's encoder would not.</remarks>
    public static string ForgedToken(Guid customerId, string key = SigningKey, int expiresInMinutes = 15,
        string issuer = "extraspace", string audience = "extraspace-app", string algorithm = SecurityAlgorithms.HmacSha256)
    {
        var now = DateTimeOffset.UtcNow;
        var header = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new { alg = algorithm, typ = "JWT" }));
        var payload = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = issuer,
            aud = audience,
            sub = customerId.ToString(),
            iat = now.AddMinutes(-30).ToUnixTimeSeconds(),
            nbf = now.AddMinutes(-30).ToUnixTimeSeconds(),
            exp = now.AddMinutes(expiresInMinutes).ToUnixTimeSeconds(),
        }));
        var keyBytes = Convert.FromBase64String(key);
        var data = Encoding.ASCII.GetBytes($"{header}.{payload}");
        var signature = algorithm switch
        {
            SecurityAlgorithms.HmacSha256 => HMACSHA256.HashData(keyBytes, data),
            SecurityAlgorithms.HmacSha384 => HMACSHA384.HashData(keyBytes, data),
            SecurityAlgorithms.HmacSha512 => HMACSHA512.HashData(keyBytes, data),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm)),
        };
        return $"{header}.{payload}.{Base64Url.EncodeToString(signature)}";
    }
}
