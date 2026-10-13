using Hangfire;
using Microsoft.Extensions.Logging;

namespace ExtraSpace.Api.Tests.TestSupport;

/// <summary>Test-only Hangfire jobs, enqueued through <c>POST /test/jobs/{kind}</c>.</summary>
public sealed class TestJobs(ILogger<TestJobs> logger)
{
    public void Ok(string marker) => logger.LogInformation("Test job ran {marker}", marker);

    [AutomaticRetry(Attempts = 0, LogEvents = false)]
    public void Fail(string marker) => throw new InvalidOperationException("job-boom " + marker);
}
