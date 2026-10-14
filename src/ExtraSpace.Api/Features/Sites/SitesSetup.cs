using ExtraSpace.Api.Features.Admin.Contracts;
using ExtraSpace.Api.Features.Sites.Admin;
using ExtraSpace.Api.Platform.Jobs;

namespace ExtraSpace.Api.Features.Sites;

internal static class SitesSetup
{
    public static IServiceCollection AddSitesFeature(this IServiceCollection services)
    {
        services.AddPermissions(new Permission(SitesPermissions.Manage, "Edit sites, storage types and unit sizes, and start unit syncs"));
        services.AddScoped<UnitRefreshService>();
        services.AddScoped<SiteAdminService>();
        services.AddScoped<StorageTypeAdminService>();
        services.AddScoped<UnitSizeAdminService>();
        services.AddScoped<UnitAdminQuery>();
        services.AddScoped<UnitRefreshRequests>();
        services.AddRecurringJob<UnitRefreshJob>(UnitRefreshJob.Name, UnitRefreshJob.Cron);
        return services;
    }
}
