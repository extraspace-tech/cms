using ExtraSpace.Api.Platform.Tracing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace ExtraSpace.Api.Platform.Errors;

public static class ProblemDetailsSetup
{
    /// <summary>Runs for every ProblemDetails the app writes: exception handler, status-code pages, MVC, validation, ProblemWriter.</summary>
    public static void Customize(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        problem.Status ??= context.HttpContext.Response.StatusCode;
        problem.Extensions.TryAdd("code", ErrorCodes.ForStatus(problem.Status.Value));
        // Overwrites the framework's default traceId (the full traceparent form) with our 32-hex id.
        problem.Extensions["traceId"] = RequestTrace.TraceId(context.HttpContext);
    }

    public static IActionResult ValidationResponse(ActionContext context)
    {
        var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateValidationProblemDetails(
            context.HttpContext, context.ModelState, StatusCodes.Status400BadRequest, "One or more fields are invalid.");
        problem.Extensions["code"] = ErrorCodes.ValidationFailed;
        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    }
}
