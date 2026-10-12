namespace ExtraSpace.Api.Platform.Http;

public static class AppVersion
{
    /// <summary>
    /// Reads "1.4.0", "1.4", "1.4.0+12" or "1.4.0-beta.2" as 1.4.0; build and pre-release parts are ignored.
    /// Returns null when the value cannot be read.
    /// </summary>
    public static Version? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var core = value.Trim();
        var cut = core.IndexOfAny(['-', '+']);
        if (cut >= 0) core = core[..cut];
        if (!Version.TryParse(core, out var version)) return null;
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
    }
}
