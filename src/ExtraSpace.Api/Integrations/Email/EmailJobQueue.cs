using Hangfire;

namespace ExtraSpace.Api.Integrations.Email;

internal sealed class EmailJobQueue(IBackgroundJobClient jobs)
{
    public string Enqueue(SealedEmail email) =>
        jobs.Enqueue<EmailJob>(job => job.SendAsync(email.Template, email.ExpiresAt, email.Payload, CancellationToken.None));
}
