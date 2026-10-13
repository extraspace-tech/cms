namespace ExtraSpace.Api.Integrations.Email;

public sealed record RenderedEmail(string To, string Subject, string Html, string Text);

/// <summary>Delivers one rendered email: SES, the Development log, or a test fake.</summary>
internal interface IEmailTransport
{
    Task SendAsync(RenderedEmail email, CancellationToken cancellationToken);
}
