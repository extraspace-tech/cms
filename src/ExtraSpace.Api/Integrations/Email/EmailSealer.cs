using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace ExtraSpace.Api.Integrations.Email;

internal sealed record EmailPayload(string To, Dictionary<string, string> Values);

/// <summary>An email whose address and values are encrypted; safe to store in the outbox and the job tables.</summary>
internal sealed record SealedEmail(string Template, DateTimeOffset ExpiresAt, string Payload);

internal sealed class EmailSealer(IDataProtectionProvider protection, TimeProvider time)
{
    internal const string Purpose = "ExtraSpace.Integrations.Email.v1";

    public SealedEmail Seal(EmailMessage message)
    {
        // A missing value or unknown template fails this request, not every attempt of the job.
        _ = EmailTemplates.Render(message.Template, message.To, message.Values);
        var payload = JsonSerializer.Serialize(new EmailPayload(message.To, new Dictionary<string, string>(message.Values)));
        // The job checks ExpiresAt itself; the protector lives a minute longer so an unreadable payload is a real failure.
        var sealedPayload = protection.CreateProtector(Purpose).ToTimeLimitedDataProtector()
            .Protect(payload, message.ValidFor + TimeSpan.FromMinutes(1));
        return new SealedEmail(message.Template, time.GetUtcNow() + message.ValidFor, sealedPayload);
    }
}
