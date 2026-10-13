using ExtraSpace.Api.Features.Admin.Contracts;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ExtraSpace.Api.Features.Admin;

internal static class AdminSetup
{
    public static IServiceCollection AddAdminFeature(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddPermissions(
            new Permission(AdminPermissions.StaffManage, "Add staff, change their role, deactivate them"),
            new Permission(AdminPermissions.RolesManage, "Create, edit and delete roles"),
            new Permission(AdminPermissions.AuditRead, "Read the audit log"),
            new Permission(AdminPermissions.JobsManage, "Open the background jobs dashboard"));
        services.AddSingleton<PermissionCatalog>();
        services.Configure<AdminOptions>(configuration.GetSection(AdminOptions.Section));
        services.AddScoped<StaffSignIn>();
        services.AddScoped<StaffAccess>();
        services.AddScoped<StaffService>();
        services.AddScoped<RoleService>();
        services.AddScoped<AuditQuery>();
        services.AddStaffAuth(configuration, environment);
        services.AddScoped<AuditContext>();
        services.AddScoped<ISaveChangesInterceptor, AuditInterceptor>();
        return services;
    }
}
