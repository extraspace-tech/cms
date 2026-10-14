using System.Collections.Immutable;
using System.Diagnostics;
using ExtraSpace.Api.Platform.Tracing;
using Hangfire.Client;
using Hangfire.Common;
using Hangfire.Server;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog.Context;

namespace ExtraSpace.Api.Platform.Jobs;

/// <summary>
/// Job tracing (S0 spec §7, §4.4): stores the enqueuing request's correlationId on the job, gives each run its own trace,
/// and logs start, success and each failed attempt once (Hangfire's own retry logging is off).
/// </summary>
/// <remarks>
/// Hangfire's filters are process-wide, so there is one shared instance. It logs through the oldest live host's logger: each
/// host attaches its own (<see cref="JobTracingLogger"/>) and detaches it when disposed, so a disposed test host is never used.
/// </remarks>
internal sealed partial class JobTracingFilter : IClientFilter, IServerFilter, IApplyStateFilter
{
    internal const string CorrelationParameter = "correlationId";
    private const string ScopeItem = "es.jobScope";

    public static JobTracingFilter Shared { get; } = new();

    private ImmutableList<ILogger> _loggers = [];

    private JobTracingFilter() { }

    private ILogger Logger => Volatile.Read(ref _loggers) is { Count: > 0 } loggers ? loggers[0] : NullLogger.Instance;

    internal void Attach(ILogger hostLogger) => ImmutableInterlocked.Update(ref _loggers, l => l.Add(hostLogger));

    internal void Detach(ILogger hostLogger) => ImmutableInterlocked.Update(ref _loggers, l => l.Remove(hostLogger));

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
        LogStarted(Logger, jobName);
    }

    public void OnPerformed(PerformedContext context)
    {
        if (context.Items.TryGetValue(ScopeItem, out var item) is false || item is not RunScope scope) return;
        var jobName = JobName(context.BackgroundJob.Job);
        var elapsedMs = (long)Stopwatch.GetElapsedTime(scope.Started).TotalMilliseconds;
        if (context.Exception is null) LogSucceeded(Logger, jobName, elapsedMs);
        // Hangfire wraps what the job threw; log the job's own exception.
        else LogFailed(Logger, context.Exception is JobPerformanceException { InnerException: { } inner } ? inner : context.Exception, jobName, elapsedMs);
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
            LogFailedPermanently(Logger, jobName);
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

/// <summary>Attaches this host's logger to <see cref="JobTracingFilter.Shared"/> for as long as the host lives.</summary>
internal sealed class JobTracingLogger : IDisposable
{
    private readonly ILogger<JobTracingFilter> _logger;

    public JobTracingLogger(ILogger<JobTracingFilter> logger)
    {
        _logger = logger;
        JobTracingFilter.Shared.Attach(logger);
    }

    public void Dispose() => JobTracingFilter.Shared.Detach(_logger);
}
