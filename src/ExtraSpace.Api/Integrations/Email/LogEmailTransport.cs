namespace ExtraSpace.Api.Integrations.Email;

/// <summary>Development only (S0 spec §9.1): prints the whole email, login code included, so sign-in works without SES.</summary>
internal sealed partial class LogEmailTransport(ILogger<LogEmailTransport> logger) : IEmailTransport
{
    public Task SendAsync(RenderedEmail email, CancellationToken cancellationToken)
    {
        LogEmail(logger, email.To, email.Subject, email.Text);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Development email to {to}: {subject}\n{text}")]
    private static partial void LogEmail(ILogger logger, string to, string subject, string text);
}
