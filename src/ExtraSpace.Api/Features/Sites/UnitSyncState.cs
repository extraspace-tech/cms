namespace ExtraSpace.Api.Features.Sites;

/// <summary>The unit sync's own row per site, so the sync never writes the staff-edited <see cref="Site"/> (S1 spec §5.1).</summary>
internal sealed class UnitSyncState
{
    public Guid SiteId { get; init; }
    /// <summary>SiteLink's .NET-ticks cursor, round-tripped only; null = no successful pull yet.</summary>
    public long? LastTimePolledTicks { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
}
