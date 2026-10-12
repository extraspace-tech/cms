using System.Globalization;
using System.Threading.RateLimiting;
using ExtraSpace.Api.Platform.Errors;
using Microsoft.AspNetCore.RateLimiting;

namespace ExtraSpace.Api.Platform.Http;

/// <summary>The shared 429 answer. Features add their own policies (S0-3: code requests and checks).</summary>
public static class RateLimitSetup
{
    public static void Configure(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (context, _) =>
        {
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                context.HttpContext.Response.Headers.RetryAfter =
                    ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            await ProblemWriter.WriteAsync(context.HttpContext, StatusCodes.Status429TooManyRequests, ErrorCodes.RateLimited,
                "Too many requests. Try again later.");
        };
    }
}
