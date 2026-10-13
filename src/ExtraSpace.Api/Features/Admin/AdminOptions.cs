namespace ExtraSpace.Api.Features.Admin;

internal sealed class AdminOptions
{
    public const string Section = "Admin";

    /// <summary>First staff emails (S0 spec §6.2); used only while no active Owner exists. Remove once one Owner exists.</summary>
    public string[] BootstrapEmails { get; set; } = [];

    /// <summary>Company email domains (<c>example.com</c>). Empty = any. Another domain cannot be added as staff and never gets a code.</summary>
    public string[] AllowedEmailDomains { get; set; } = [];

    public bool AllowsDomain(string email)
    {
        if (AllowedEmailDomains.Length == 0) return true;
        var domain = email[(email.LastIndexOf('@') + 1)..].Trim();
        return AllowedEmailDomains.Any(d => d.Trim().TrimStart('@').Equals(domain, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsBootstrap(string email) => BootstrapEmails.Any(b => b.Trim().Equals(email, StringComparison.OrdinalIgnoreCase));
}
