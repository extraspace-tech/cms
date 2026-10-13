using Hangfire;

namespace ExtraSpace.Api.Features.Admin;

internal static partial class JobsDashboard
{
    /// <summary>
    /// S0 spec §7: /admin/jobs, behind admin sign-in + jobs.manage. Hangfire drops its own (local-only) filters and checks
    /// the app's antiforgery token (header X-CSRF-TOKEN) on its POSTs.
    /// </summary>
    public static IEndpointConventionBuilder MapJobsDashboard(this IEndpointRouteBuilder app) =>
        app.MapHangfireDashboardWithAuthorizationPolicy(AdminPermissions.JobsManage, "/admin/jobs", new DashboardOptions
        {
            AppPath = "/admin",
            DashboardTitle = "ExtraSpace jobs",
            DisplayStorageConnectionString = false,
        }).AddEndpointFilter(LogActionAsync);

    // Dashboard writes are not audited (they change Hangfire's tables, not ours): one line says who did what.
    private static async ValueTask<object?> LogActionAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        var http = context.HttpContext;
        var method = http.Request.Method;
        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && http.Response.StatusCode < 400 && http.User.StaffId() is { } staffId)
            LogAction(http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(JobsDashboard).FullName!),
                http.Request.Path.Value, staffId);
        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Jobs dashboard action {path} by staff {staffId}")]
    private static partial void LogAction(ILogger logger, string? path, Guid staffId);
}
