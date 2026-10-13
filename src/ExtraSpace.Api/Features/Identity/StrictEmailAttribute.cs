using System.ComponentModel.DataAnnotations;
using System.Net.Mail;

namespace ExtraSpace.Api.Features.Identity;

/// <summary>
/// A plain address only: after trimming, no inner whitespace, no display name, and a dotted domain.
/// <c>[EmailAddress]</c> alone lets <c>a@b</c> and <c>Jane &lt;jane@example.test&gt;</c> through.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class StrictEmailAttribute : ValidationAttribute
{
    public StrictEmailAttribute() : base("The {0} field is not a valid e-mail address.") { }

    public override bool IsValid(object? value)
    {
        if (value is null) return true;   // [Required] decides
        if (value is not string raw) return false;
        var email = raw.Trim();
        if (email.Any(char.IsWhiteSpace)) return false;
        if (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email) return false;
        var domain = parsed.Host;
        return domain.Contains('.') && !domain.StartsWith('.') && !domain.EndsWith('.');
    }
}
