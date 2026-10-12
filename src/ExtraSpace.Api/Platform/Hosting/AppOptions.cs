namespace ExtraSpace.Api.Platform.Hosting;

/// <summary>Which parts this process runs (S0 spec §7): <c>Api</c> serves HTTP, <c>Jobs</c> runs the Hangfire server, <c>All</c> does both.</summary>
public enum AppRole { All, Api, Jobs }

public sealed class AppOptions
{
    public const string Section = "App";

    public AppRole Role { get; set; } = AppRole.All;

    /// <summary>Git SHA of the running build; set by the Docker image (S0-6).</summary>
    public string Version { get; set; } = "dev";

    /// <summary>Lowest allowed Flutter app version per platform (<c>ios</c>, <c>android</c>, <c>web</c>).</summary>
    public Dictionary<string, string> MinVersion { get; set; } = new();
}
