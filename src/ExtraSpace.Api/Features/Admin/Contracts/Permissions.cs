namespace ExtraSpace.Api.Features.Admin.Contracts;

public sealed record Permission(string Name, string Description);

public static class PermissionRegistration
{
    /// <summary>Each feature registers the permissions it checks; one authorization policy is made per name (S0 spec §6.3).</summary>
    public static IServiceCollection AddPermissions(this IServiceCollection services, params Permission[] permissions)
    {
        foreach (var permission in permissions) services.AddSingleton(permission);
        return services;
    }
}
