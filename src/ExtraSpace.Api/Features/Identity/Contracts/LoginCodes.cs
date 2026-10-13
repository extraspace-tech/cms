namespace ExtraSpace.Api.Features.Identity.Contracts;

/// <summary>Who the code signs in. A code only verifies for the purpose it was sent for.</summary>
public enum LoginCodePurpose { Customer, Staff }

public interface ILoginCodes
{
    /// <summary>Creates a code and queues its email, or throws 429 RATE_LIMITED (per-email limits, S0 spec §6.1).</summary>
    Task SendAsync(LoginCodePurpose purpose, string email, string? ip, CancellationToken cancellationToken);

    /// <summary>
    /// Checks the code. A wrong code counts an attempt and throws 400 INVALID_CODE / TOO_MANY_ATTEMPTS. On a match it marks
    /// the code used and runs <paramref name="onVerified"/> in the SAME transaction; if that throws, the code stays unused.
    /// </summary>
    Task<T> VerifyAsync<T>(LoginCodePurpose purpose, string email, string code,
        Func<CancellationToken, Task<T>> onVerified, CancellationToken cancellationToken);
}
