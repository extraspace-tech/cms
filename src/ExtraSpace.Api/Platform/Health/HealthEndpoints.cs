using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace ExtraSpace.Api.Platform.Health;

public static class HealthEndpoints
{
    /// <summary><c>/health/live</c>: the process is up (ALB target check). <c>/health/ready</c>: the database answers (S0 spec §4.5).</summary>
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();
    }
}
