using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.DataProtection;

namespace ExtraSpace.Api.Integrations.Email;

internal sealed record EmailPayload(string To, Dictionary<string, string> Values);

internal sealed class HangfireEmailSender(IBackgroundJobClient jobs, IDataProtectionProvider protection, TimeProvider time) : IEmailSender
{
    internal const string Purpose = "ExtraSpace.Integrations.Email.v1";

    public string Enqueue(EmailMessage message)
    {
        // A missing value or unknown template fails this request, not every attempt of the job.
        _ = EmailTemplates.Render(message.Template, message.To, message.Values);
        var payload = JsonSerializer.Serialize(new EmailPayload(message.To, new Dictionary<string, string>(message.Values)));
        // The job checks expiresAt itself; the protector lives a minute longer so an unreadable payload is a real failure.
        var expiresAt = time.GetUtcNow() + message.ValidFor;
        var sealedPayload = protection.CreateProtector(Purpose).ToTimeLimitedDataProtector()
            .Protect(payload, message.ValidFor + TimeSpan.FromMinutes(1));
        return jobs.Enqueue<EmailJob>(job => job.SendAsync(message.Template, expiresAt, sealedPayload, CancellationToken.None));
    }
}
