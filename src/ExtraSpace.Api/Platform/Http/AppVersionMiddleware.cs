using ExtraSpace.Api.Platform.Errors;
using ExtraSpace.Api.Platform.Hosting;
using Microsoft.Extensions.Options;

namespace ExtraSpace.Api.Platform.Http;

/// <summary>
/// Step 11 (S0 spec §4.3): an app below the platform's minimum version gets 426 before sign-in runs.
/// Requests without both headers (admin UI, health checks, tools) pass, and so does a version that cannot be read.
/// </summary>
internal sealed partial class AppVersionMiddleware(RequestDelegate next, IOptionsMonitor<AppOptions> options, ILogger<AppVersionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext http)
    {
        var platform = http.Request.Headers["X-App-Platform"].ToString();
        var raw = http.Request.Headers["X-App-Version"].ToString();
        if (platform.Length > 0 && raw.Length > 0 && MinimumFor(platform) is { } minimum)
        {
            var version = AppVersion.Parse(raw);
            if (version is null)
            {
                UnreadableVersion(logger);
            }
            else if (version < minimum)
            {
                await ProblemWriter.WriteAsync(http, StatusCodes.Status426UpgradeRequired, ErrorCodes.AppUpdateRequired,
                    "Please update the app to continue.");
                return;
            }
        }
        await next(http);
    }

    private Version? MinimumFor(string platform)
    {
        foreach (var (name, value) in options.CurrentValue.MinVersion)
            if (string.Equals(name, platform, StringComparison.OrdinalIgnoreCase)) return AppVersion.Parse(value);
        return null;
    }

    // clientPlatform and clientVersion are already on the line (log context, clipped).
    [LoggerMessage(Level = LogLevel.Warning, Message = "Unreadable X-App-Version header; request let through")]
    private static partial void UnreadableVersion(ILogger logger);
}
