using System.Diagnostics;
using Amazon.Runtime;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Microsoft.Extensions.Options;

namespace ExtraSpace.Api.Integrations.Email;

/// <summary>AWS SES v2. One log line per call (S0 spec §4.4); the job logs the exception and Hangfire retries.</summary>
internal sealed partial class SesEmailTransport(
    IAmazonSimpleEmailServiceV2 ses, IOptions<EmailOptions> options, ILogger<SesEmailTransport> logger) : IEmailTransport
{
    public async Task SendAsync(RenderedEmail email, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var response = await ses.SendEmailAsync(ToRequest(email, options.Value.From), cancellationToken);
            LogCall(logger, "ses", "SendEmail", "ok", Elapsed(started), (int)response.HttpStatusCode);
        }
        catch (AmazonServiceException e)
        {
            LogCallFailed(logger, "ses", "SendEmail", "error", Elapsed(started), (int)e.StatusCode);
            // SES messages can name the recipient (sandbox MessageRejected); only the code and status reach logs and Hangfire.
            throw new InvalidOperationException($"SES SendEmail failed: {e.ErrorCode} (HTTP {(int)e.StatusCode})");
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogCallFailed(logger, "ses", "SendEmail", e is OperationCanceledException ? "timeout" : "error", Elapsed(started), 0);
            throw;
        }
    }

    internal static SendEmailRequest ToRequest(RenderedEmail email, string from) => new()
    {
        FromEmailAddress = from,
        Destination = new Destination { ToAddresses = [email.To] },
        Content = new EmailContent
        {
            Simple = new Message
            {
                Subject = new Content { Data = email.Subject, Charset = "UTF-8" },
                Body = new Body
                {
                    Html = new Content { Data = email.Html, Charset = "UTF-8" },
                    Text = new Content { Data = email.Text, Charset = "UTF-8" },
                },
            },
        },
    };

    private static long Elapsed(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    [LoggerMessage(Level = LogLevel.Information, Message = "Outside call {integration} {operation}: {outcome} in {elapsedMs} ms (HTTP {httpStatus})")]
    private static partial void LogCall(ILogger logger, string integration, string operation, string outcome, long elapsedMs, int httpStatus);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outside call {integration} {operation}: {outcome} in {elapsedMs} ms (HTTP {httpStatus})")]
    private static partial void LogCallFailed(ILogger logger, string integration, string operation, string outcome, long elapsedMs, int httpStatus);
}
