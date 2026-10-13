using System.Diagnostics;
using ExtraSpace.Api.Platform.Tracing;
using Hangfire.Client;
using Hangfire.Common;
using Hangfire.Server;
using Hangfire.States;
using Hangfire.Storage;
using Serilog.Context;

namespace ExtraSpace.Api.Platform.Jobs;

/// <summary>
/// Job tracing (S0 spec §7, §4.4): stores the enqueuing request's correlationId on the job, gives each run its own trace,
/// and logs start, success and each failed attempt once (Hangfire's own retry logging is off).
/// </summary>
internal sealed partial class JobTracingFilter(ILogger<JobTracingFilter> logger) : IClientFilter, IServerFilter, IApplyStateFilter
{
    internal const string CorrelationParameter = "correlationId";
    private const string ScopeItem = "es.jobScope";

    public void OnCreating(CreatingContext context) =>
        context.SetJobParameter(CorrelationParameter, Correlation.CurrentId ?? Activity.Current?.TraceId.ToHexString());

    public void OnCreated(CreatedContext context) { }

    public void OnPerforming(PerformingContext context)
    {
        var activity = new Activity("job").Start();
        var correlationId = context.GetJobParameter<string>(CorrelationParameter) ?? activity.TraceId.ToHexString();
        var jobName = JobName(context.BackgroundJob.Job);
        context.Items[ScopeItem] = new RunScope(
            activity,
            Correlation.Begin(correlationId),
            LogContext.PushProperty("jobId", context.BackgroundJob.Id),
            LogContext.PushProperty("jobName", jobName),
            LogContext.PushProperty("attempt", context.GetJobParameter<int>("RetryCount") + 1),
            Stopwatch.GetTimestamp());
        LogStarted(logger, jobName);
    }

    public void OnPerformed(PerformedContext context)
    {
        if (context.Items.TryGetValue(ScopeItem, out var item) is false || item is not RunScope scope) return;
        var jobName = JobName(context.BackgroundJob.Job);
        var elapsedMs = (long)Stopwatch.GetElapsedTime(scope.Started).TotalMilliseconds;
        if (context.Exception is null) LogSucceeded(logger, jobName, elapsedMs);
        // Hangfire wraps what the job threw; log the job's own exception.
        else LogFailed(logger, context.Exception is JobPerformanceException { InnerException: { } inner } ? inner : context.Exception, jobName, elapsedMs);
        scope.Dispose();
    }

    // AutomaticRetry schedules a retry until the attempts run out; only then is Failed applied.
    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        if (context.NewState is not FailedState) return;
        var jobId = context.BackgroundJob.Id;
        var correlationId = SerializationHelper.Deserialize<string>(context.Connection.GetJobParameter(jobId, CorrelationParameter));
        var retryCount = context.Connection.GetJobParameter(jobId, "RetryCount");
        var jobName = JobName(context.BackgroundJob.Job);
        using (LogContext.PushProperty("jobId", jobId))
        using (LogContext.PushProperty("jobName", jobName))
        using (LogContext.PushProperty("attempt", (retryCount is null ? 0 : SerializationHelper.Deserialize<int>(retryCount)) + 1))
        using (LogContext.PushProperty("correlationId", correlationId))
        {
            LogFailedPermanently(logger, jobName);
        }
    }

    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction) { }

    // Job is null when Hangfire cannot load the type or method (renamed or removed class, rolling deploy).
    internal static string JobName(Job? job) => job?.Type.Name switch
    {
        null => "unknown",
        var name when name.EndsWith("Job", StringComparison.Ordinal) => name[..^3],
        var name => name,
    };

    private sealed record RunScope(Activity Activity, IDisposable Correlation, IDisposable JobId, IDisposable JobName, IDisposable Attempt, long Started)
        : IDisposable
    {
        public void Dispose()
        {
            Attempt.Dispose();
            JobName.Dispose();
            JobId.Dispose();
            Correlation.Dispose();
            Activity.Stop();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {jobName} started")]
    private static partial void LogStarted(ILogger logger, string jobName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {jobName} succeeded in {elapsedMs} ms")]
    private static partial void LogSucceeded(ILogger logger, string jobName, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job {jobName} failed in {elapsedMs} ms")]
    private static partial void LogFailed(ILogger logger, Exception exception, string jobName, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job {jobName} failed permanently")]
    private static partial void LogFailedPermanently(ILogger logger, string jobName);
}
