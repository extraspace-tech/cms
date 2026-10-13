namespace ExtraSpace.Api.Integrations.Email;

internal enum EmailTransport { Ses, Log, None }

internal sealed class EmailOptions
{
    public const string Section = "Email";

    /// <summary><c>Ses</c> (default); <c>Log</c> prints emails, Development only; <c>None</c> leaves the transport to the host (tests).</summary>
    public EmailTransport Transport { get; set; } = EmailTransport.Ses;

    /// <summary>Sender, for example <c>ExtraSpace &lt;no-reply@…&gt;</c>; must be verified in SES (open item 3).</summary>
    public string From { get; set; } = "";

    /// <summary>Exact addresses or <c>@domain</c> entries. Empty = everyone. Staging sets it, so a test never emails a real customer.</summary>
    public string[] AllowedRecipients { get; set; } = [];

    public bool Allows(string to) => AllowedRecipients.Length == 0 || AllowedRecipients.Any(entry =>
        entry.StartsWith('@')
            ? to.EndsWith(entry, StringComparison.OrdinalIgnoreCase)
            : to.Equals(entry, StringComparison.OrdinalIgnoreCase));
}
