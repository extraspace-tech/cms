using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace ExtraSpace.Api.Platform.Errors;

/// <summary>Writes every ProblemDetails as JSON, whatever the Accept header; the framework writer refuses non-JSON Accept values.</summary>
internal sealed class JsonProblemWriter(IOptions<JsonOptions> json, IOptions<ProblemDetailsOptions> options) : IProblemDetailsWriter
{
    public bool CanWrite(ProblemDetailsContext context) => true;

    public ValueTask WriteAsync(ProblemDetailsContext context)
    {
        var http = context.HttpContext;
        var problem = context.ProblemDetails;
        problem.Status ??= http.Response.StatusCode;
        // The framework's default type and title (ProblemDetailsDefaults is internal); the result constructor applies them.
        _ = TypedResults.Problem(problem);
        options.Value.CustomizeProblemDetails?.Invoke(context);
        return new ValueTask(http.Response.WriteAsJsonAsync(
            problem, json.Value.SerializerOptions.GetTypeInfo(problem.GetType()), "application/problem+json", http.RequestAborted));
    }
}
