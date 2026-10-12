using Microsoft.AspNetCore.Mvc;

namespace ExtraSpace.Api.Platform.Errors;

/// <summary>Writes an error response from middleware that runs before MVC (version check, rate limiter).</summary>
public static class ProblemWriter
{
    public static async Task WriteAsync(HttpContext http, int status, string code, string title)
    {
        http.Response.StatusCode = status;
        var problem = new ProblemDetails { Status = status, Title = title };
        problem.Extensions["code"] = code;
        var service = http.RequestServices.GetRequiredService<IProblemDetailsService>();
        await service.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = problem });
    }
}
