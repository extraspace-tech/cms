namespace ExtraSpace.Api.Features.Identity;

internal static class EmailAddresses
{
    /// <summary>One spelling per address (S0 spec §5.1): trimmed, lower-case.</summary>
    public static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
