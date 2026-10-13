using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ExtraSpace.Api.Integrations.Email;

namespace ExtraSpace.Api.Tests.TestSupport;

/// <summary>Stands in for SES in tests: keeps every email the jobs send.</summary>
public sealed partial class FakeEmailTransport : IEmailTransport
{
    private readonly ConcurrentQueue<RenderedEmail> _sent = new();

    public IReadOnlyList<RenderedEmail> SentTo(string to) => _sent.Where(e => e.To == to).ToList();

    public Task SendAsync(RenderedEmail email, CancellationToken cancellationToken)
    {
        _sent.Enqueue(email);
        return Task.CompletedTask;
    }

    public async Task<RenderedEmail> WaitForAsync(string to, int count = 1, int timeoutSeconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (SentTo(to).Count < count)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Expected {count} emails to {to}, got {SentTo(to).Count}.");
            await Task.Delay(100);
        }
        return SentTo(to)[count - 1];
    }

    /// <summary>The 6-digit code in the n-th login-code email to this address.</summary>
    public async Task<string> CodeForAsync(string to, int count = 1) =>
        SixDigits().Match((await WaitForAsync(to, count)).Text).Value;

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex SixDigits();
}
