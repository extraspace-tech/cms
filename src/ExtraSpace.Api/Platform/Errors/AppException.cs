namespace ExtraSpace.Api.Platform.Errors;

/// <summary>A business error with its HTTP status and stable code; the exception handler turns it into ProblemDetails.</summary>
public sealed class AppException(int status, string code, string title) : Exception(title)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public string Title { get; } = title;

    /// <summary>Written as the Retry-After header (whole seconds, rounded up); for 429 answers.</summary>
    public TimeSpan? RetryAfter { get; init; }
}
