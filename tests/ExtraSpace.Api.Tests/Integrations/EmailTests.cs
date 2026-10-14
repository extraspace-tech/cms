using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using ExtraSpace.Api.Integrations.Email;
using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ExtraSpace.Api.Tests.Integrations;

public sealed class EmailTests(ApiFactory api)
{
    private readonly HttpClient _client = api.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewAddress() => $"email-{Guid.NewGuid():N}@example.test";

    private static string? Str(JsonElement line, string name) =>
        line.TryGetProperty(name, out var value) ? value.ToString() : null;

    private async Task<string> SendAsync(string to, string code, int? validForMs = null)
    {
        var url = $"/test/email?to={Uri.EscapeDataString(to)}&code={Uri.EscapeDataString(code)}"
            + (validForMs is { } ms ? $"&validForMs={ms}" : "");
        var response = await _client.PostAsync(url, null, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("jobId").GetString()!;
    }

    [Fact]
    public async Task The_job_renders_the_template_and_sends_it()
    {
        var to = NewAddress();
        var jobId = await SendAsync(to, "12<34>56");

        var email = await api.Emails.WaitForAsync(to);
        email.Subject.Should().Be("Your ExtraSpace sign-in code");
        email.Html.Should().Contain("12&lt;34&gt;56").And.NotContain("12<34>56");
        email.Text.Should().Contain("12<34>56").And.Contain("10 minutes");

        var sent = await api.Logs.WaitForAsync(l => Str(l, "messageTemplate") == "Email {template} sent to {email}"
            && Str(l, "email") == ExtraSpace.Api.Platform.Logging.Pii.MaskEmail(to) && Str(l, "jobId") == jobId);
        Str(sent.Single(), "template").Should().Be("login-code");
    }

    [Fact]
    public async Task Job_arguments_hold_no_address_or_values()
    {
        var to = NewAddress();
        var jobId = await SendAsync(to, "918273");

        await using var connection = new NpgsqlConnection(api.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "SELECT invocationdata::text || ' ' || COALESCE(arguments::text, '') FROM hangfire.job WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", long.Parse(jobId));
        var stored = (string)(await command.ExecuteScalarAsync(Ct))!;
        stored.Should().Contain("SendAsync", "the query must have read the stored job");
        stored.Should().NotContain(to).And.NotContain("918273");
    }

    [Fact]
    public async Task An_expired_payload_is_dropped_with_a_warning()
    {
        var to = NewAddress();
        var jobId = await SendAsync(to, "555666", validForMs: 1);

        var warning = await api.Logs.WaitForAsync(l => Str(l, "jobId") == jobId
            && Str(l, "messageTemplate") == "Email {template} expired before it was sent");
        Str(warning.Single(), "level").Should().Be("Warning");
        api.Emails.SentTo(to).Should().BeEmpty();
    }

    [Fact]
    public async Task An_unreadable_payload_that_has_not_expired_fails_the_job()
    {
        var jobs = api.Services.GetRequiredService<IBackgroundJobClient>();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        var jobId = jobs.Enqueue<EmailJob>(j => j.SendAsync("login-code", expiresAt, "not-a-protected-payload", CancellationToken.None));
        try
        {
            var failed = await api.Logs.WaitForAsync(l => Str(l, "jobId") == jobId
                && Str(l, "messageTemplate") == "Job {jobName} failed in {elapsedMs} ms");
            Str(failed[0], "level").Should().Be("Error");
            Str(failed[0], "exception").Should().Contain("CryptographicException");
            api.Logs.All().Should().NotContain(l => Str(l, "jobId") == jobId
                && Str(l, "messageTemplate") == "Email {template} expired before it was sent");
        }
        finally
        {
            jobs.Delete(jobId); // stop the retries (attempts 2-5) from running during later tests
        }
    }

    [Fact]
    public void Sealing_fails_in_the_request_when_a_template_value_is_missing_or_unknown()
    {
        var sealer = api.Services.GetRequiredService<EmailSealer>();
        var missing = () => sealer.Seal(new EmailMessage("login-code", NewAddress(),
            new Dictionary<string, string> { ["code"] = "123456" }, TimeSpan.FromMinutes(10)));
        missing.Should().Throw<InvalidOperationException>().WithMessage("*minutes*");
        var unknown = () => sealer.Seal(new EmailMessage("no-such-template", NewAddress(),
            new Dictionary<string, string>(), TimeSpan.FromMinutes(10)));
        unknown.Should().Throw<InvalidOperationException>().WithMessage("*no-such-template*");
    }

    private static IConfiguration Config(string transport) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Email:Transport"] = transport }).Build();

    [Theory]
    [InlineData("Log", "Production")]
    [InlineData("Log", "Testing")]
    [InlineData("None", "Production")]
    [InlineData("None", "Development")]
    public void A_transport_outside_its_environment_stops_start_up(string transport, string environment)
    {
        var add = () => new ServiceCollection().AddEmail(Config(transport), new HostingEnvironment { EnvironmentName = environment });
        add.Should().Throw<InvalidOperationException>().WithMessage($"Email:Transport={transport} *");
    }

    [Theory]
    [InlineData("Log", "Development")]
    [InlineData("None", "Testing")]
    public void A_transport_in_its_environment_is_allowed(string transport, string environment)
    {
        var add = () => new ServiceCollection().AddEmail(Config(transport), new HostingEnvironment { EnvironmentName = environment });
        add.Should().NotThrow();
    }

    [Fact]
    public void Ses_without_a_sender_fails_validation()
    {
        var provider = new ServiceCollection()
            .AddEmail(Config("Ses"), new HostingEnvironment { EnvironmentName = "Production" }).BuildServiceProvider();
        var read = () => provider.GetRequiredService<IOptions<EmailOptions>>().Value;
        read.Should().Throw<OptionsValidationException>().WithMessage("*Email:From*");
    }

    [Fact]
    public void Rendering_fails_loudly_on_a_missing_value_or_unknown_template()
    {
        var render = () => EmailTemplates.Render("login-code", "a@example.test", new Dictionary<string, string> { ["code"] = "1" });
        render.Should().Throw<InvalidOperationException>().WithMessage("*minutes*");
        var unknown = () => EmailTemplates.EnsureKnown("no-such-template");
        unknown.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void The_ses_request_carries_html_text_and_sender()
    {
        var request = SesEmailTransport.ToRequest(new RenderedEmail("to@example.test", "Subject", "<p>h</p>", "t"), "ExtraSpace <no-reply@example.test>");
        request.FromEmailAddress.Should().Be("ExtraSpace <no-reply@example.test>");
        request.Destination.ToAddresses.Should().Equal("to@example.test");
        request.Content.Simple.Subject.Data.Should().Be("Subject");
        request.Content.Simple.Body.Html.Data.Should().Be("<p>h</p>");
        request.Content.Simple.Body.Text.Data.Should().Be("t");
    }

    [Fact]
    public async Task A_ses_rejection_keeps_the_address_out_of_the_log_and_the_exception()
    {
        const string to = "rejected@example.test";
        var ses = DispatchProxy.Create<IAmazonSimpleEmailServiceV2, ThrowingSes>();
        ((ThrowingSes)(object)ses).Error = new MessageRejectedException($"Email address is not verified: {to}")
        {
            ErrorCode = "MessageRejected",
            StatusCode = HttpStatusCode.BadRequest,
        };
        var logger = new FakeLogger<SesEmailTransport>();
        var transport = new SesEmailTransport(ses, Options.Create(new EmailOptions { From = "no-reply@example.test" }), logger);

        var send = () => transport.SendAsync(new RenderedEmail(to, "Subject", "<p>h</p>", "t"), Ct);

        var thrown = (await send.Should().ThrowAsync<InvalidOperationException>()).Which;
        thrown.Message.Should().Be("SES SendEmail failed: MessageRejected (HTTP 400)");
        thrown.InnerException.Should().BeNull();
        logger.LatestRecord.Level.Should().Be(LogLevel.Warning);
        logger.LatestRecord.Message.Should().StartWith("Outside call ses SendEmail: error in ").And.EndWith("(HTTP 400)");
        logger.Collector.GetSnapshot().Should().AllSatisfy(r => r.Message.Should().NotContain(to));
    }

    /// <summary>An SES client whose SendEmailAsync fails with <see cref="Error"/>; any other call is a test bug.</summary>
    public class ThrowingSes : DispatchProxy
    {
        public Exception? Error { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IAmazonSimpleEmailServiceV2.SendEmailAsync)
                ? Task.FromException<SendEmailResponse>(Error!)
                : throw new NotSupportedException(targetMethod?.Name);
    }
}
