using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using ExtraSpace.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace ExtraSpace.Api.Features.Identity;

/// <summary>Refresh-token sessions (S0 spec §6.1): a family per sign-in, rotated on every refresh, revoked as a whole.</summary>
internal sealed partial class TokenService(
    AppDbContext db, AccessTokens access, TimeProvider time, ILogger<TokenService> logger)
{
    public static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(60);

    /// <summary>Adds the first refresh token of a new family; the caller saves and commits.</summary>
    internal SignIn StartSession(Customer customer, string? ip, string? userAgent) =>
        Issue(customer, Guid.CreateVersion7(), ip, userAgent).SignIn;

    public async Task<SignIn> RefreshAsync(string refreshToken, string? ip, string? userAgent, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var hash = Hash(refreshToken);
        var familyId = await FamilyOfAsync(hash, cancellationToken) ?? throw IdentityErrors.Unauthenticated();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockFamilyAsync(familyId, cancellationToken);
        var row = await db.Set<CustomerRefreshToken>()
            .FromSql($"SELECT * FROM customer_refresh_token WHERE token_hash = {hash} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null) throw IdentityErrors.Unauthenticated();   // deleted by housekeeping since the look-up

        if (row.ReplacedById is not null || row.RevokedAt is not null)
        {
            // A token that was already used is back: someone else holds a copy (spec §6.1 theft detection).
            await RevokeFamilyAsync(familyId, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (row.ReplacedById is not null) LogReuse(logger, familyId, row.CustomerId);
            throw IdentityErrors.Unauthenticated();
        }
        if (row.ExpiresAt <= now) throw IdentityErrors.Unauthenticated();

        var customer = await db.Set<Customer>().SingleOrDefaultAsync(c => c.Id == row.CustomerId, cancellationToken)
            ?? throw IdentityErrors.Unauthenticated();
        var (signIn, next) = Issue(customer, familyId, ip, userAgent);
        row.ReplacedById = next.Id;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return signIn;
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (await FamilyOfAsync(Hash(refreshToken), cancellationToken) is not { } familyId) return;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockFamilyAsync(familyId, cancellationToken);
        var revoked = await RevokeFamilyAsync(familyId, time.GetUtcNow().UtcDateTime, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (revoked > 0) LogSignedOut(logger, familyId);
    }

    /// <summary>The advisory-lock key of one family; tests take the same lock to prove refresh and logout wait for it.</summary>
    internal static string FamilyLockKey(Guid familyId) => $"refresh-family:{familyId}";

    // family_id never changes, so it is safe to read before the lock.
    private Task<Guid?> FamilyOfAsync(string hash, CancellationToken cancellationToken) =>
        db.Set<CustomerRefreshToken>().Where(t => t.TokenHash == hash).Select(t => (Guid?)t.FamilyId).SingleOrDefaultAsync(cancellationToken);

    // Every writer of a family takes this lock first: a revoke must also see a token another refresh is inserting.
    private Task LockFamilyAsync(Guid familyId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({FamilyLockKey(familyId)}, 0))", cancellationToken);

    // ExecuteUpdate skips TimestampsInterceptor, so updated_at is set here.
    private Task<int> RevokeFamilyAsync(Guid familyId, DateTime now, CancellationToken cancellationToken) =>
        db.Set<CustomerRefreshToken>().Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.UpdatedAt, now), cancellationToken);

    private (SignIn SignIn, CustomerRefreshToken Row) Issue(Customer customer, Guid familyId, string? ip, string? userAgent)
    {
        var refreshToken = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var row = new CustomerRefreshToken
        {
            CustomerId = customer.Id,
            FamilyId = familyId,
            TokenHash = Hash(refreshToken),
            ExpiresAt = time.GetUtcNow().UtcDateTime + RefreshLifetime,
            Ip = ClipIp(ip),
            UserAgent = Clip(userAgent, 256),
        };
        db.Add(row);
        var (accessToken, accessExpiresAt) = access.Issue(customer.Id, customer.Email);
        return (new SignIn(customer.Id, new TokenResponse(accessToken, accessExpiresAt, refreshToken, row.ExpiresAt)), row);
    }

    internal static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Fits the 45-character ip columns; an IPv6 address with a zone id can be longer.</summary>
    internal static string? ClipIp(string? ip) => Clip(ip, 45);

    private static string? Clip(string? value, int max) => value is null || value.Length <= max ? value : value[..max];

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh token family {familyId} revoked: reuse detected for customer {customerId}")]
    private static partial void LogReuse(ILogger logger, Guid familyId, Guid customerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Refresh token family {familyId} signed out")]
    private static partial void LogSignedOut(ILogger logger, Guid familyId);
}
