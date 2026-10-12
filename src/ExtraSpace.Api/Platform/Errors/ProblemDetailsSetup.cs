using System.Text.Json;
using ExtraSpace.Api.Platform.Tracing;

namespace ExtraSpace.Api.Platform.Errors;

public static class ProblemDetailsSetup
{
    /// <summary>Runs for every ProblemDetails the app writes: exception handler, status-code pages, validation, ProblemWriter.</summary>
    public static void Customize(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        problem.Status ??= context.HttpContext.Response.StatusCode;
        if (problem is HttpValidationProblemDetails validation)
        {
            validation.Title = "One or more fields are invalid.";
            validation.Extensions["code"] = ErrorCodes.ValidationFailed;
            validation.Errors = CamelCaseKeys(validation.Errors);
        }
        problem.Extensions.TryAdd("code", ErrorCodes.ForStatus(problem.Status.Value));
        // Overwrites the framework's default traceId (the full traceparent form) with our 32-hex id.
        problem.Extensions["traceId"] = RequestTrace.TraceId(context.HttpContext);
    }

    // .NET 10 validation keys errors by the C# member path (`Address.PostCode`); clients read the JSON names (`address.postCode`).
    private static Dictionary<string, string[]> CamelCaseKeys(IDictionary<string, string[]> errors)
    {
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (key, messages) in errors)
        {
            var name = string.Join('.', key.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
            result[name] = result.TryGetValue(name, out var existing) ? [.. existing, .. messages] : messages;
        }
        return result;
    }
}
