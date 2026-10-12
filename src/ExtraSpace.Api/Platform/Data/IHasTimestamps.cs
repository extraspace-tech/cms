namespace ExtraSpace.Api.Platform.Data;

/// <summary>Every table has <c>created_at</c> / <c>updated_at</c> (S0 spec §5); <see cref="TimestampsInterceptor"/> sets them.</summary>
public interface IHasTimestamps
{
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}
