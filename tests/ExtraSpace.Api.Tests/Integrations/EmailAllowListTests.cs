using System.Text.Json;
using ExtraSpace.Api.Integrations.Email;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExtraSpace.Api.Tests.Integrations;

public sealed class EmailAllowListTests
{
    [Theory]
    [InlineData("qa@example.test", true)]
    [InlineData("QA@Example.Test", true)]
    [InlineData("anyone@team.example.test", true)]
    [InlineData("anyone@other.example.test", false)]
    [InlineData("qa@example.test.evil.test", false)]
    [InlineData("xqa@example.test", false)]
    public void An_entry_is_an_address_or_a_domain(string to, bool allowed)
    {
        var options = new EmailOptions { AllowedRecipients = ["qa@example.test", "@team.example.test"] };

        options.Allows(to).Should().Be(allowed);
    }

    [Fact]
    public void An_empty_list_allows_everyone() => new EmailOptions().Allows("anyone@example.test").Should().BeTrue();

    [Theory]
    [InlineData("qa@example.test", 1)]
    [InlineData("stranger@example.test", 0)]
    public async Task A_recipient_off_the_allow_list_gets_nothing(string to, int expectedEmails)
    {
        var protection = new EphemeralDataProtectionProvider();
        var transport = new FakeEmailTransport();
        var job = new EmailJob(protection, transport, TimeProvider.System,
            Options.Create(new EmailOptions { AllowedRecipients = ["qa@example.test"] }), NullLogger<EmailJob>.Instance);
        var payload = protection.CreateProtector(HangfireEmailSender.Purpose).ToTimeLimitedDataProtector().Protect(
            JsonSerializer.Serialize(new EmailPayload(to, new Dictionary<string, string> { ["code"] = "123456", ["minutes"] = "10" })),
            TimeSpan.FromMinutes(5));

        await job.SendAsync("login-code", DateTimeOffset.UtcNow.AddMinutes(5), payload, TestContext.Current.CancellationToken);

        transport.SentTo(to).Should().HaveCount(expectedEmails);
    }
}
