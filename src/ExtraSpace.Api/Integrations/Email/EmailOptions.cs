namespace ExtraSpace.Api.Integrations.Email;

internal enum EmailTransport { Ses, Log, None }

internal sealed class EmailOptions
{
    public const string Section = "Email";

    /// <summary><c>Ses</c> (default); <c>Log</c> prints emails, Development only; <c>None</c> leaves the transport to the host (tests).</summary>
    public EmailTransport Transport { get; set; } = EmailTransport.Ses;

    /// <summary>Sender, for example <c>ExtraSpace &lt;no-reply@…&gt;</c>; must be verified in SES (open item 3).</summary>
    public string From { get; set; } = "";
}
