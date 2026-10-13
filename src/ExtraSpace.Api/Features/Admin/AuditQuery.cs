using System.Globalization;
using ExtraSpace.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace ExtraSpace.Api.Features.Admin;

internal sealed class AuditQuery(AppDbContext db)
{
    public async Task<AuditPage> SearchAsync(Guid? staffId, string? entityType, Guid? entityId, string? fromText, string? toText,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        if (pageSize is < 1 or > 100) throw AdminErrors.Invalid("The page size must be between 1 and 100.");
        if (page < 1 || page > int.MaxValue / pageSize) throw AdminErrors.Invalid("The page is out of range.");
        var from = Instant(fromText, "from");
        var to = Instant(toText, "to");
        if (from > to) throw AdminErrors.Invalid("'from' must not be after 'to'.");

        var rows = db.Set<AuditLog>().AsNoTracking();
        if (staffId is { } s) rows = rows.Where(a => a.StaffUserId == s);
        if (entityType is not null) rows = rows.Where(a => a.EntityType == entityType);
        if (entityId is { } e) rows = rows.Where(a => a.EntityId == e);
        if (from is { } f) rows = rows.Where(a => a.OccurredAt >= f.UtcDateTime);
        if (to is { } t) rows = rows.Where(a => a.OccurredAt < t.UtcDateTime);

        var total = await rows.CountAsync(cancellationToken);
        var items = await rows
            .Join(db.Set<StaffUser>(), a => a.StaffUserId, u => u.Id, (a, u) => new { Row = a, u.Email })
            .OrderByDescending(x => x.Row.OccurredAt).ThenByDescending(x => x.Row.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);

        return new AuditPage(
            items.Select(x => new AuditItem(x.Row.Id, x.Row.OccurredAt, x.Row.StaffUserId, x.Email, x.Row.Action, x.Row.EntityType,
                x.Row.EntityId, x.Row.Changes.RootElement.Clone(), x.Row.Ip?.ToString())).ToList(),
            total, page, pageSize);
    }

    // Without an offset the server's own time zone would decide the instant.
    private static DateTimeOffset? Instant(string? value, string name) =>
        value is null ? null
        : DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) && parsed.Kind != DateTimeKind.Unspecified
            ? DateTimeOffset.Parse(value, CultureInfo.InvariantCulture)
            : throw AdminErrors.Invalid($"'{name}' must be a date and time with an offset, such as 2026-10-13T10:00:00+08:00.");
}
