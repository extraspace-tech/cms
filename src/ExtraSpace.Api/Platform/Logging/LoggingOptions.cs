namespace ExtraSpace.Api.Platform.Logging;

public sealed class LoggingOptions
{
    public const string Section = "Logging";

    /// <summary>Database commands slower than this log a Warning (S0 spec §4.4).</summary>
    public int SlowQueryMs { get; set; } = 500;
}
