using System.Text.Json;
using ExtraSpace.Api.Platform.Outbox;

namespace ExtraSpace.Api.Integrations.Email;

/// <summary>Hands a sealed email to <see cref="EmailJob"/>, which keeps its own short retries. At least once: a crash between the two can send it twice.</summary>
internal sealed class EmailOutboxHandler(EmailJobQueue queue) : IOutboxHandler
{
    public const string KindName = "Email";

    public string Kind => KindName;

    public Task HandleAsync(OutboxItem item, CancellationToken cancellationToken)
    {
        queue.Enqueue(item.Payload.Deserialize<SealedEmail>(JsonSerializerOptions.Web) ?? throw new InvalidOperationException("Empty email payload."));
        return Task.CompletedTask;
    }
}
