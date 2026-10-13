using Hangfire;

namespace ExtraSpace.Api.Platform.Jobs;

/// <summary>A job that runs on a schedule (S0 spec §7). Implementations must be idempotent.</summary>
internal interface IRecurringJob
{
    Task RunAsync(CancellationToken cancellationToken);
}

internal sealed record RecurringJobDefinition(
    string Name, string Cron, Action<IRecurringJobManager, string, string, RecurringJobOptions> AddOrUpdate);

internal static class RecurringJobs
{
    /// <summary>Declares a recurring job; <c>Jobs:&lt;name&gt;:Enabled=false</c> removes it at the next start-up.</summary>
    public static IServiceCollection AddRecurringJob<TJob>(this IServiceCollection services, string name, string cron)
        where TJob : class, IRecurringJob
    {
        services.AddScoped<TJob>();
        services.AddSingleton(new RecurringJobDefinition(name, cron,
            (manager, id, schedule, options) => manager.AddOrUpdate<TJob>(id, job => job.RunAsync(CancellationToken.None), schedule, options)));
        return services;
    }
}

/// <summary>Writes the declared schedules to Hangfire at start-up (roles Jobs and All only).</summary>
internal sealed partial class RecurringJobsRegistrar(
    IEnumerable<RecurringJobDefinition> jobs, IRecurringJobManager manager, IConfiguration configuration,
    ILogger<RecurringJobsRegistrar> logger) : IHostedService
{
    // Business schedules use Singapore time (S0 spec §4.6); both SG and MY are UTC+8.
    private static readonly TimeZoneInfo Schedule = TimeZoneInfo.FindSystemTimeZoneById("Asia/Singapore");

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var job in jobs)
        {
            if (configuration.GetValue($"{JobsOptions.Section}:{job.Name}:Enabled", true))
            {
                job.AddOrUpdate(manager, job.Name, job.Cron, new RecurringJobOptions { TimeZone = Schedule });
            }
            else
            {
                manager.RemoveIfExists(job.Name);
                LogDisabled(logger, job.Name);
            }
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Recurring job {jobName} is disabled by configuration")]
    private static partial void LogDisabled(ILogger logger, string jobName);
}
