using System.Net.Http.Json;
using System.Text.Json;
using ExtraSpace.Api.Platform.Jobs;
using ExtraSpace.Api.Platform.Tracing;
using ExtraSpace.Api.Tests.TestSupport;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExtraSpace.Api.Tests.Platform;

public sealed class JobsTests(ApiFactory api)
{
    private readonly HttpClient _client = api.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string? Str(JsonElement line, string name) =>
        line.TryGetProperty(name, out var value) ? value.ToString() : null;

    private async Task<(string TraceId, string JobId, string Marker)> EnqueueAsync(string kind)
    {
        var response = await _client.PostAsync($"/test/jobs/{kind}", null, Ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (Problem.TraceIdOf(response), body.GetProperty("jobId").GetString()!, body.GetProperty("marker").GetString()!);
    }

    [Fact]
    public async Task A_job_enqueued_by_a_request_carries_its_correlation_id()
    {
        var (traceId, jobId, marker) = await EnqueueAsync("ok");

        var line = (await api.Logs.WaitForAsync(l => Str(l, "marker") == marker)).Single();
        Str(line, "correlationId").Should().Be(traceId);
        Str(line, "traceId").Should().MatchRegex("^[0-9a-f]{32}$").And.NotBe(traceId, "a job run is its own trace");
        Str(line, "jobId").Should().Be(jobId);
        Str(line, "jobName").Should().Be("TestJobs");
        Str(line, "attempt").Should().Be("1");

        var done = await api.Logs.WaitForAsync(l => Str(l, "jobId") == jobId && Str(l, "messageTemplate") == "Job {jobName} succeeded in {elapsedMs} ms");
        Str(done.Single(), "correlationId").Should().Be(traceId);
        api.Logs.All().Where(l => Str(l, "jobId") == jobId && Str(l, "messageTemplate") == "Job {jobName} started").Should().ContainSingle();
    }

    [Fact]
    public async Task A_failed_job_logs_its_exception_once_and_fails_permanently()
    {
        var (traceId, jobId, marker) = await EnqueueAsync("fail");

        var permanent = await api.Logs.WaitForAsync(l => Str(l, "jobId") == jobId && Str(l, "messageTemplate") == "Job {jobName} failed permanently");
        Str(permanent.Single(), "level").Should().Be("Error");
        Str(permanent.Single(), "correlationId").Should().Be(traceId);
        Str(permanent.Single(), "attempt").Should().Be("1");

        var withException = api.Logs.All().Where(l => Str(l, "jobId") == jobId && l.TryGetProperty("exception", out _)).ToList();
        withException.Should().ContainSingle("each failed attempt is logged once, by our filter only");
        Str(withException[0], "exception").Should().Contain("job-boom " + marker);
    }

    [Fact]
    public void A_job_whose_type_cannot_be_loaded_still_fails_permanently()
    {
        string jobId;
        using (Correlation.Begin("c0ffee00c0ffee00c0ffee00c0ffee00"))
        {
            jobId = api.Services.GetRequiredService<IBackgroundJobClient>().Schedule<TestJobs>(j => j.Ok("never"), TimeSpan.FromDays(1));
        }
        var storage = api.Services.GetRequiredService<JobStorage>();
        using var connection = storage.GetConnection();
        using var transaction = connection.CreateWriteTransaction();
        // Hangfire hands filters a null Job when the job's class or method no longer exists.
        var context = new ApplyStateContext(storage, connection, transaction,
            new BackgroundJob(jobId, null, DateTime.UtcNow), new FailedState(new InvalidOperationException("type not found")), "Scheduled");

        var apply = () => api.Services.GetRequiredService<JobTracingFilter>().OnStateApplied(context, transaction);

        apply.Should().NotThrow();
        var line = api.Logs.All().Single(l => Str(l, "jobId") == jobId && Str(l, "messageTemplate") == "Job {jobName} failed permanently");
        Str(line, "jobName").Should().Be("unknown");
        Str(line, "attempt").Should().Be("1");
        Str(line, "correlationId").Should().Be("c0ffee00c0ffee00c0ffee00c0ffee00");
    }

    [Theory]
    [InlineData("Api", 0)]
    [InlineData("Jobs", 2)]
    [InlineData("All", 2)]
    public void Only_the_jobs_roles_run_a_job_server(string role, int hostedServices)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["App:Role"] = role,
            ["ConnectionStrings:Default"] = "Host=unused",
        }).Build();
        var services = new ServiceCollection();
        services.AddPlatformJobs(config);
        services.Count(d => d.ServiceType == typeof(IHostedService)).Should().Be(hostedServices);
    }

    [Fact]
    public async Task Recurring_jobs_follow_their_enabled_switch()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jobs:Second:Enabled"] = "false",
        }).Build();
        var manager = new FakeRecurringJobManager();
        var registrar = new RecurringJobsRegistrar(
            [
                new RecurringJobDefinition("First", Cron.Daily(3), (m, id, cron, o) => m.AddOrUpdate<TestRecurring>(id, j => j.RunAsync(CancellationToken.None), cron, o)),
                new RecurringJobDefinition("Second", Cron.Daily(4), (m, id, cron, o) => m.AddOrUpdate<TestRecurring>(id, j => j.RunAsync(CancellationToken.None), cron, o)),
            ],
            manager, config, NullLogger<RecurringJobsRegistrar>.Instance);

        await registrar.StartAsync(Ct);

        manager.Added.Should().Equal("First");
        manager.Removed.Should().Equal("Second");
        manager.TimeZones.Should().AllSatisfy(z => z.Id.Should().Be("Asia/Singapore"));
    }

    private sealed class TestRecurring : IRecurringJob
    {
        public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeRecurringJobManager : IRecurringJobManager
    {
        public List<string> Added { get; } = [];
        public List<string> Removed { get; } = [];
        public List<TimeZoneInfo> TimeZones { get; } = [];

        public void AddOrUpdate(string recurringJobId, Job job, string cronExpression, RecurringJobOptions options)
        {
            Added.Add(recurringJobId);
            TimeZones.Add(options.TimeZone);
        }

        public void Trigger(string recurringJobId) { }

        public void RemoveIfExists(string recurringJobId) => Removed.Add(recurringJobId);
    }
}
