using ExtraSpace.Api.Platform.Jobs;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ExtraSpace.Api.Platform.Outbox;

public static class OutboxSetup
{
    internal static IServiceCollection AddOutbox(this IServiceCollection services)
    {
        services.AddScoped<IOutbox, Outbox>();
        // One per DbContext scope: it remembers whether this unit of work added rows. EF also calls its transaction methods.
        services.AddScoped<ISaveChangesInterceptor, OutboxNudge>();
        services.AddRecurringJob<OutboxDispatchJob>(OutboxDispatchJob.Name, OutboxDispatchJob.Cron);
        return services;
    }

    public static IServiceCollection AddOutboxHandler<THandler>(this IServiceCollection services) where THandler : class, IOutboxHandler =>
        services.AddScoped<IOutboxHandler, THandler>();
}
