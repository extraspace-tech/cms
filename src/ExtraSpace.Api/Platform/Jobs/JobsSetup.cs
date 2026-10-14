using ExtraSpace.Api.Platform.Hosting;
using Hangfire;
using Hangfire.PostgreSql;

namespace ExtraSpace.Api.Platform.Jobs;

internal static class JobsSetup
{
    /// <summary>Hangfire on PostgreSQL, schema <c>hangfire</c> (S0 spec §7). Every role enqueues; only Jobs and All run the server.</summary>
    public static IServiceCollection AddPlatformJobs(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(JobsOptions.Section).Get<JobsOptions>() ?? new JobsOptions();
        var role = configuration.GetSection(AppOptions.Section).Get<AppOptions>()?.Role ?? AppRole.All;

        services.AddSingleton(JobTracingFilter.Shared);
        services.AddSingleton<JobTracingLogger>();
        services.AddHangfire((sp, config) =>
        {
            _ = sp.GetRequiredService<JobTracingLogger>();   // attaches this host's logger to the shared filter until the host ends
            UseProcessWideFilters();
            config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UsePostgreSqlStorage(
                    c => c.UseNpgsqlConnection(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")
                        ?? throw new InvalidOperationException("ConnectionStrings:Default is not set.")),
                    new PostgreSqlStorageOptions
                    {
                        SchemaName = "hangfire",
                        PrepareSchemaIfNecessary = true,
                        QueuePollInterval = options.QueuePollInterval,
                    });
        });

        if (role is AppRole.Jobs or AppRole.All)
        {
            services.AddHangfireServer(o => o.WorkerCount = options.WorkerCount);
            services.AddHostedService<RecurringJobsRegistrar>();
        }
        return services;
    }

    private static readonly Lock FiltersLock = new();

    // Hangfire's filters are process-wide: the first host adds ours and later hosts in the same process (tests) leave the list
    // alone, so no job is created or run in a window without them. Our filter logs each attempt once; Hangfire's default retry
    // filter would log the same failure again.
    private static void UseProcessWideFilters()
    {
        lock (FiltersLock)
        {
            if (GlobalJobFilters.Filters.Any(f => f.Instance == JobTracingFilter.Shared)) return;
            GlobalJobFilters.Filters.Remove(typeof(AutomaticRetryAttribute));
            GlobalJobFilters.Filters.Add(new AutomaticRetryAttribute { LogEvents = false });
            GlobalJobFilters.Filters.Add(JobTracingFilter.Shared);
        }
    }
}
