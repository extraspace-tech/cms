namespace ExtraSpace.Api.Integrations.Email;

/// <summary>
/// An email to send. <paramref name="ValidFor"/> bounds how long the queued job may still send it (a login code is useless
/// after 10 minutes); the address and values are encrypted inside the job, so the job tables never hold them in clear.
/// </summary>
internal sealed record EmailMessage(string Template, string To, IReadOnlyDictionary<string, string> Values, TimeSpan ValidFor);

internal interface IEmailSender
{
    /// <summary>
    /// Adds the email to the caller's unit of work (outbox). Call it before the caller's SaveChanges/commit: the email goes out
    /// only if that commits (S1 spec §9.10).
    /// </summary>
    void Enqueue(EmailMessage message);
}
