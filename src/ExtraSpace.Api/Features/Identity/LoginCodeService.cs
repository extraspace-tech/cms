using System.Globalization;
using ExtraSpace.Api.Integrations.Email;
using ExtraSpace.Api.Platform.Data;
using ExtraSpace.Api.Platform.Logging;
using Microsoft.EntityFrameworkCore;

namespace ExtraSpace.Api.Features.Identity;

/// <summary>Email + one-time code sign-in (S0 spec §6.1).</summary>
internal sealed partial class LoginCodeService(
    AppDbContext db, LoginCodeHasher hasher, TokenService tokens, IEmailSender email, TimeProvider time,
    ILogger<LoginCodeService> logger)
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ResendGap = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);
    private const int MaxPerWindow = 5;
    internal const int MaxAttempts = 5;

    public async Task RequestAsync(string rawEmail, string? ip, CancellationToken cancellationToken)
    {
        var address = EmailAddresses.Normalize(rawEmail);
        var now = time.GetUtcNow().UtcDateTime;
        var code = LoginCodeHasher.NewCode();
        Guid codeId;

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await LockAsync(address, cancellationToken);
            var row = await ReadRowForUpdateAsync(address, cancellationToken);
            if (row is null)
            {
                row = new CustomerLoginCode { Email = address, WindowStartedAt = now };
                db.Add(row);
            }
            else
            {
                if (now - row.WindowStartedAt >= Window)
                {
                    row.WindowStartedAt = now;
                    row.SentInWindow = 0;
                }
                // One 429 with the longer wait when both the 60 s gap and the hourly cap apply.
                var allowedAt = row.LastSentAt + ResendGap;
                if (row.SentInWindow >= MaxPerWindow && row.WindowStartedAt + Window > allowedAt) allowedAt = row.WindowStartedAt + Window;
                if (allowedAt > now) throw IdentityErrors.RateLimited(allowedAt - now);
            }

            row.CodeHash = hasher.Hash(address, code);
            row.ExpiresAt = now + CodeLifetime;
            row.Attempts = 0;
            row.ConsumedAt = null;
            row.RequestIp = TokenService.ClipIp(ip);
            row.LastSentAt = now;
            row.SentInWindow++;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            codeId = row.Id;
        }

        email.Enqueue(new EmailMessage("login-code", address,
            new Dictionary<string, string>
            {
                ["code"] = code,
                ["minutes"] = ((int)CodeLifetime.TotalMinutes).ToString(CultureInfo.InvariantCulture),
            },
            CodeLifetime));
        LogRequested(logger, codeId, Pii.MaskEmail(address));
    }

    public async Task<SignIn> VerifyAsync(string rawEmail, string code, string? ip, string? userAgent, CancellationToken cancellationToken)
    {
        var address = EmailAddresses.Normalize(rawEmail);
        var now = time.GetUtcNow().UtcDateTime;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(address, cancellationToken);
        var row = await ReadRowForUpdateAsync(address, cancellationToken);
        if (row is null || row.ConsumedAt is not null || row.ExpiresAt <= now || row.CodeHash.Length == 0) throw IdentityErrors.InvalidCode();
        if (row.Attempts >= MaxAttempts) throw IdentityErrors.TooManyAttempts();

        if (!hasher.Matches(row.CodeHash, address, code))
        {
            row.Attempts++;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);   // the failed attempt must count although we answer with an error
            if (row.Attempts < MaxAttempts) throw IdentityErrors.InvalidCode();
            LogLocked(logger, row.Id);
            throw IdentityErrors.TooManyAttempts();
        }

        row.ConsumedAt = now;
        var customer = await db.Set<Customer>().SingleOrDefaultAsync(c => c.Email == address, cancellationToken);
        var isNew = customer is null;
        if (customer is null)
        {
            customer = new Customer { Email = address };
            db.Add(customer);
        }
        var signIn = tokens.StartSession(customer, ip, userAgent);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (isNew) LogCreated(logger, customer.Id);
        LogSignedIn(logger, customer.Id);
        return signIn;
    }

    // Parallel calls for one address (a double tap) run one after another; the lock ends with the transaction.
    private Task LockAsync(string address, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"login-code:" + address}, 0))", cancellationToken);

    // The row lock makes a housekeeping delete either wait for us or win first (then the row reads as null), never a 0-row update.
    private Task<CustomerLoginCode?> ReadRowForUpdateAsync(string address, CancellationToken cancellationToken) =>
        db.Set<CustomerLoginCode>().FromSql($"SELECT * FROM customer_login_code WHERE email = {address} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    [LoggerMessage(Level = LogLevel.Information, Message = "Login code {codeId} requested for {email}")]
    private static partial void LogRequested(ILogger logger, Guid codeId, string email);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login code {codeId} locked after too many wrong attempts")]
    private static partial void LogLocked(ILogger logger, Guid codeId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Customer {customerId} created")]
    private static partial void LogCreated(ILogger logger, Guid customerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Customer {customerId} signed in")]
    private static partial void LogSignedIn(ILogger logger, Guid customerId);
}
