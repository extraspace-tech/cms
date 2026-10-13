using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExtraSpace.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ExtraSpace.Api.Features.Admin;

/// <summary>Adds the audit_log rows inside the same SaveChanges as the change, so neither commits without the other.</summary>
internal sealed class AuditInterceptor(AuditContext audit, TimeProvider time) : SaveChangesInterceptor
{
    private static readonly HashSet<string> Skipped =
    [
        nameof(IHasTimestamps.CreatedAt), nameof(IHasTimestamps.UpdatedAt),
        nameof(IHasStaffStamps.CreatedByStaffId), nameof(IHasStaffStamps.UpdatedByStaffId), "Version",
    ];

    // Enum values as names, like the wire format.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerOptions.Web) { Converters = { new JsonStringEnumConverter() } };

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Record(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Record(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Record(DbContext? context)
    {
        if (context is null || audit.StaffId is not { } staffId) return;
        // Rows left by an earlier failed save on this context: this save records the changes again.
        foreach (var stale in context.ChangeTracker.Entries<AuditLog>().Where(e => e.State == EntityState.Added).ToList())
            stale.State = EntityState.Detached;
        context.ChangeTracker.DetectChanges();
        var now = time.GetUtcNow().UtcDateTime;
        var rows = new List<AuditLog>();
        foreach (var entry in context.ChangeTracker.Entries<IAudited>().ToList())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            if (entry.Entity is IHasStaffStamps)
            {
                if (entry.State == EntityState.Added) entry.Property(nameof(IHasStaffStamps.CreatedByStaffId)).CurrentValue = staffId;
                if (entry.State != EntityState.Deleted) entry.Property(nameof(IHasStaffStamps.UpdatedByStaffId)).CurrentValue = staffId;
            }
            var changes = Changes(entry);
            if (changes.Count == 0) continue;
            rows.Add(new AuditLog
            {
                StaffUserId = staffId,
                Action = audit.Action,
                EntityType = entry.Metadata.GetTableName()!,
                EntityId = (Guid)entry.Property("Id").CurrentValue!,
                Changes = JsonSerializer.SerializeToDocument(changes, Json),
                Ip = audit.Ip,
                OccurredAt = now,
            });
        }
        context.AddRange(rows);
    }

    private static Dictionary<string, AuditChange> Changes(EntityEntry entry)
    {
        var changes = new Dictionary<string, AuditChange>();
        foreach (var property in entry.Properties)
        {
            var metadata = property.Metadata;
            if (metadata.IsPrimaryKey() || Skipped.Contains(metadata.Name)
                || metadata.PropertyInfo?.IsDefined(typeof(NotAuditedAttribute)) == true) continue;
            var from = entry.State == EntityState.Added ? null : property.OriginalValue;
            var to = entry.State == EntityState.Deleted ? null : property.CurrentValue;
            if (entry.State == EntityState.Modified && (!property.IsModified || metadata.GetValueComparer().Equals(from, to))) continue;
            changes[JsonNamingPolicy.CamelCase.ConvertName(metadata.Name)] = new AuditChange(from, to);
        }
        return changes;
    }
}

internal sealed record AuditChange(object? From, object? To);
