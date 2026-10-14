using ExtraSpace.Api.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;

namespace ExtraSpace.Api.Integrations.SiteLink;

/// <summary>Calls per UTC month and operation; the row with operation <c>*</c> is the month's total (S1 spec §6.4).</summary>
internal sealed class SiteLinkCallMonth
{
    public DateOnly Month { get; init; }
    public required string Operation { get; init; }
    public long Calls { get; set; }
}

internal sealed class SiteLinkCallMonthConfiguration : IEntityTypeConfiguration<SiteLinkCallMonth>
{
    public void Configure(EntityTypeBuilder<SiteLinkCallMonth> builder)
    {
        builder.ToTable("sitelink_call_month");
        builder.HasKey(m => new { m.Month, m.Operation });
        builder.Property(m => m.Operation).HasMaxLength(100);
    }
}

internal sealed partial class SiteLinkCallCounter(
    IServiceScopeFactory scopes, IOptionsMonitor<SiteLinkOptions> options, TimeProvider time, ILogger<SiteLinkCallCounter> logger)
{
    public const string Total = "*";

    /// <summary>Adds the attempts to this month's rows. Its own scope and connection, so a caller's rollback never undoes it; never throws.</summary>
    public async Task CountAsync(string operation, int calls)
    {
        if (calls <= 0) return;
        try
        {
            var now = time.GetUtcNow();
            var month = new DateOnly(now.Year, now.Month, 1);
            long added = calls;
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var totals = await db.Database.SqlQuery<long>($"""
                INSERT INTO sitelink_call_month AS m (month, operation, calls)
                VALUES ({month}, {operation}, {added}), ({month}, {Total}, {added})
                ON CONFLICT (month, operation) DO UPDATE SET calls = m.calls + excluded.calls
                RETURNING CASE WHEN m.operation = {Total} THEN m.calls ELSE 0 END AS "Value"
                """).ToListAsync();
            var total = totals.Max();
            var alarm = options.CurrentValue.MonthlyCallAlarm;
            if (CrossedAlarm(total, calls, alarm)) LogAlarm(logger, total, alarm);
        }
        catch (Exception exception)
        {
            LogCountFailed(logger, exception, operation);
        }
    }

    /// <summary>True only for the call that moves the total from below the alarm to at or above it: one line per month.</summary>
    internal static bool CrossedAlarm(long total, int added, long alarm) => alarm > 0 && total >= alarm && total - added < alarm;

    // Task 8's alarm matches this template text exactly.
    [LoggerMessage(Level = LogLevel.Error, Message = "SiteLink calls this month reached {total}, past the alarm level {alarm}")]
    private static partial void LogAlarm(ILogger logger, long total, long alarm);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not count a SiteLink call to {operation}")]
    private static partial void LogCountFailed(ILogger logger, Exception exception, string operation);
}
