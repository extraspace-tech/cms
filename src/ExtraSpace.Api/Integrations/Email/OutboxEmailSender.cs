using ExtraSpace.Api.Platform.Outbox;

namespace ExtraSpace.Api.Integrations.Email;

internal sealed class OutboxEmailSender(IOutbox outbox, EmailSealer sealer) : IEmailSender
{
    // No natural key: every email request is its own step.
    public void Enqueue(EmailMessage message) =>
        outbox.Enqueue(EmailOutboxHandler.KindName, sealer.Seal(message), $"email:{Guid.CreateVersion7()}");
}
