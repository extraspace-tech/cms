using ExtraSpace.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace ExtraSpace.Api.Tests.TestSupport;

public sealed class StampedThing : IHasTimestamps
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>A tiny context in its own database, so interceptor tests never touch the app's migrated schema.</summary>
public sealed class StampedDbContext(DbContextOptions<StampedDbContext> options) : DbContext(options)
{
    public DbSet<StampedThing> Things => Set<StampedThing>();
}
