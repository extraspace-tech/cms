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

        services.AddSingleton<JobTracingFilter>();
        services.AddHangfire((sp, config) =>
        {
            // Hangfire's filters are process-wide: a second host in the same process (tests) must replace ours, not add more.
            // Our filter logs each attempt once; Hangfire's default retry filter would log the same failure again.
            GlobalJobFilters.Filters.Remove(typeof(AutomaticRetryAttribute));
            GlobalJobFilters.Filters.Remove(typeof(JobTracingFilter));
            config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseFilter(new AutomaticRetryAttribute { LogEvents = false })
                .UseFilter(sp.GetRequiredService<JobTracingFilter>())
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
}
