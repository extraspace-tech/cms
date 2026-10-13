using System.Globalization;
using ExtraSpace.Api.Features.Identity.Contracts;
using ExtraSpace.Api.Integrations.Email;
using ExtraSpace.Api.Platform.Data;
using ExtraSpace.Api.Platform.Logging;
using Microsoft.EntityFrameworkCore;

namespace ExtraSpace.Api.Features.Identity;

/// <summary>Email + one-time code sign-in (S0 spec §6.1), one engine for customers and staff.</summary>
internal sealed partial class LoginCodeService(
    AppDbContext db, LoginCodeHasher hasher, TokenService tokens, IEmailSender sender, TimeProvider time,
    ILogger<LoginCodeService> logger) : ILoginCodes
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ResendGap = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);
    private const int MaxPerWindow = 5;
    internal const int MaxAttempts = 5;

    public async Task SendAsync(LoginCodePurpose purpose, string email, string? ip, CancellationToken cancellationToken)
    {
        var address = EmailAddresses.Normalize(email);
        var now = time.GetUtcNow().UtcDateTime;
        var code = LoginCodeHasher.NewCode();
        Guid codeId;

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await LockAsync(purpose, address, cancellationToken);
            var row = await ReadRowForUpdateAsync(purpose, address, cancellationToken);
            if (row is null)
            {
                row = new LoginCode { Purpose = purpose, Email = address, WindowStartedAt = now };
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

            row.CodeHash = hasher.Hash(purpose, address, code);
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

        sender.Enqueue(new EmailMessage("login-code", address,
            new Dictionary<string, string>
            {
                ["code"] = code,
                ["minutes"] = ((int)CodeLifetime.TotalMinutes).ToString(CultureInfo.InvariantCulture),
            },
            CodeLifetime));
        // The purpose rides on the line as a property; the message stays the one S0-3 dashboards and tests know.
        using (PurposeScope(purpose)) LogRequested(logger, codeId, Pii.MaskEmail(address));
    }

    public async Task<T> VerifyAsync<T>(LoginCodePurpose purpose, string email, string code,
        Func<CancellationToken, Task<T>> onVerified, CancellationToken cancellationToken)
    {
        var address = EmailAddresses.Normalize(email);
        var now = time.GetUtcNow().UtcDateTime;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(purpose, address, cancellationToken);
        var row = await ReadRowForUpdateAsync(purpose, address, cancellationToken);
        if (row is null || row.ConsumedAt is not null || row.ExpiresAt <= now || row.CodeHash.Length == 0) throw IdentityErrors.InvalidCode();
        if (row.Attempts >= MaxAttempts) throw IdentityErrors.TooManyAttempts();

        if (!hasher.Matches(row.CodeHash, purpose, address, code))
        {
            row.Attempts++;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);   // the failed attempt must count although we answer with an error
            if (row.Attempts < MaxAttempts) throw IdentityErrors.InvalidCode();
            using (PurposeScope(purpose)) LogLocked(logger, row.Id);
            throw IdentityErrors.TooManyAttempts();
        }

        row.ConsumedAt = now;
        T result;
        try
        {
            result = await onVerified(cancellationToken);
        }
        catch
        {
            // The transaction rolls back at dispose; a later save on this context must not write the code as used.
            db.Entry(row).State = EntityState.Detached;
            throw;
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<SignIn> VerifyCustomerAsync(string email, string code, string? ip, string? userAgent, CancellationToken cancellationToken)
    {
        var address = EmailAddresses.Normalize(email);
        var (signIn, isNew) = await VerifyAsync(LoginCodePurpose.Customer, address, code, async ct =>
        {
            var customer = await db.Set<Customer>().SingleOrDefaultAsync(c => c.Email == address, ct);
            var created = customer is null;
            if (customer is null)
            {
                customer = new Customer { Email = address };
                db.Add(customer);
            }
            return (tokens.StartSession(customer, ip, userAgent), created);
        }, cancellationToken);

        if (isNew) LogCreated(logger, signIn.CustomerId);
        LogSignedIn(logger, signIn.CustomerId);
        return signIn;
    }

    private IDisposable? PurposeScope(LoginCodePurpose purpose) =>
        logger.BeginScope(new Dictionary<string, object> { ["purpose"] = purpose.ToString() });

    // Parallel calls for one address (a double tap) run one after another; the lock ends with the transaction.
    private Task LockAsync(LoginCodePurpose purpose, string address, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"login-code:" + purpose + ":" + address}, 0))", cancellationToken);

    // The row lock makes a housekeeping delete either wait for us or win first (then the row reads as null), never a 0-row update.
    private Task<LoginCode?> ReadRowForUpdateAsync(LoginCodePurpose purpose, string address, CancellationToken cancellationToken) =>
        db.Set<LoginCode>().FromSql($"SELECT * FROM login_code WHERE purpose = {purpose.ToString()} AND email = {address} FOR UPDATE")
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
