using ExtraSpace.Api.Platform.Http;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExtraSpace.Api.Platform.Errors;

/// <summary>The one place exceptions become responses (S0 spec §4.1) and the one place they are logged (§4.4).</summary>
internal sealed partial class AppExceptionHandler(IProblemDetailsService problems, ILogger<AppExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && http.RequestAborted.IsCancellationRequested)
        {
            http.Response.StatusCode = 499; // client closed the request; nobody is listening
            return true;
        }

        var (status, code, title) = Classify(exception);
        if (status >= 500) LogUnhandled(logger, exception);

        http.Response.StatusCode = status;
        if (exception is AppException { RetryAfter: { } retryAfter })
            http.Response.Headers.RetryAfter = RateLimitSetup.RetryAfterSeconds(retryAfter);
        var problem = new ProblemDetails { Status = status, Title = title };
        problem.Extensions["code"] = code;
        await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = problem });
        return true;
    }

    internal static (int Status, string Code, string Title) Classify(Exception exception) => exception switch
    {
        AppException app => (app.Status, app.Code, app.Title),
        DbUpdateConcurrencyException => (409, ErrorCodes.Conflict, "The record was changed by someone else. Reload and try again."),
        _ when IsUniqueViolation(exception) => (409, ErrorCodes.Conflict, "A record with the same value already exists."),
        BadHttpRequestException bad => (bad.StatusCode, ErrorCodes.ForStatus(bad.StatusCode), "The request is invalid."),
        _ => (500, ErrorCodes.InternalError, "An unexpected error occurred."),
    };

    private static bool IsUniqueViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) return true;
        return false;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception")]
    private static partial void LogUnhandled(ILogger logger, Exception exception);
}
