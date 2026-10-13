namespace ExtraSpace.Api.Features.Identity;

internal sealed class CustomerAuthOptions
{
    public const string Section = "CustomerAuth";

    public string Issuer { get; set; } = "extraspace";
    public string Audience { get; set; } = "extraspace-app";

    /// <summary>Base64 of at least 32 random bytes. From Secrets Manager on AWS and user-secrets locally; never a committed file.</summary>
    public string SigningKey { get; set; } = "";

    public byte[] SigningKeyBytes() => Convert.FromBase64String(SigningKey);

    public static bool IsValidKey(string key)
    {
        try
        {
            return Convert.FromBase64String(key).Length >= 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
