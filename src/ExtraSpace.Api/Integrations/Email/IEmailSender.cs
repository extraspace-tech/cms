namespace ExtraSpace.Api.Integrations.Email;

/// <summary>
/// An email to send. <paramref name="ValidFor"/> bounds how long the queued job may still send it (a login code is useless
/// after 10 minutes); the address and values are encrypted inside the job, so the job tables never hold them in clear.
/// </summary>
internal sealed record EmailMessage(string Template, string To, IReadOnlyDictionary<string, string> Values, TimeSpan ValidFor);

internal interface IEmailSender
{
    /// <summary>Queues the email as a Hangfire job and returns its id. Call it after the database commit (S0 spec §7).</summary>
    string Enqueue(EmailMessage message);
}
