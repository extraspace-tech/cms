namespace ExtraSpace.Api.Platform.Http;

/// <summary>Step 5. Set in OnStarting, because the exception handler clears headers before it writes an error.</summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    public Task InvokeAsync(HttpContext http)
    {
        http.Response.OnStarting(() =>
        {
            var headers = http.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            if (!environment.IsDevelopment()) headers.StrictTransportSecurity = "max-age=31536000";
            return Task.CompletedTask;
        });
        return next(http);
    }
}
