using System.Diagnostics;

namespace ExtraSpace.Api.Platform.Tracing;

public static class ActivitySetup
{
    private static int _registered;

    /// <summary>ASP.NET Core only creates a request Activity when something listens; this guarantees one, so every request has a W3C trace id.</summary>
    public static void EnsureRequestActivities()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1) return;
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        });
    }
}
