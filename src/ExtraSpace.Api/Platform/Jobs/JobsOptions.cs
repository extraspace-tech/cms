namespace ExtraSpace.Api.Platform.Jobs;

internal sealed class JobsOptions
{
    public const string Section = "Jobs";

    public int WorkerCount { get; set; } = 5;

    /// <summary>How often an idle worker checks the queue. Tests use 200 ms.</summary>
    public TimeSpan QueuePollInterval { get; set; } = TimeSpan.FromSeconds(5);
}
