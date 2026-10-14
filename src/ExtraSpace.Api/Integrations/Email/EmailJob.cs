using System.Text.Json;
using ExtraSpace.Api.Platform.Logging;
using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace ExtraSpace.Api.Integrations.Email;

/// <summary>Sends one queued email. At-least-once: a duplicate run resends the same email, which is harmless.</summary>
internal sealed partial class EmailJob(IDataProtectionProvider protection, IEmailTransport transport, TimeProvider time, IOptions<EmailOptions> options, ILogger<EmailJob> logger)
{
    // 10+20+40+80+160 s = 310 s: every retry runs inside the 10-minute code life, so a key-ring fault ends in "failed permanently".
    [AutomaticRetry(Attempts = 5, LogEvents = false, DelaysInSeconds = [10, 20, 40, 80, 160])]
    public async Task SendAsync(string template, DateTimeOffset expiresAt, string payload, CancellationToken cancellationToken)
    {
        if (time.GetUtcNow() >= expiresAt)
        {
            // Sending a stale login code helps nobody, so no retry.
            LogExpired(logger, template);
            return;
        }

        // No catch: an unreadable payload (key ring fault) must fail and alarm, not be dropped quietly.
        var json = protection.CreateProtector(EmailSealer.Purpose).ToTimeLimitedDataProtector().Unprotect(payload);
        var message = JsonSerializer.Deserialize<EmailPayload>(json)!;
        if (!options.Value.Allows(message.To))
        {
            LogNotAllowed(logger, template, Pii.MaskEmail(message.To));
            return;
        }
        await transport.SendAsync(EmailTemplates.Render(template, message.To, message.Values), cancellationToken);
        LogSent(logger, template, Pii.MaskEmail(message.To));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email {template} expired before it was sent")]
    private static partial void LogExpired(ILogger logger, string template);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email {template} not sent: {email} is not on the allow-list")]
    private static partial void LogNotAllowed(ILogger logger, string template, string email);

    [LoggerMessage(Level = LogLevel.Information, Message = "Email {template} sent to {email}")]
    private static partial void LogSent(ILogger logger, string template, string email);
}
