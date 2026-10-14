namespace ExtraSpace.Engines;

/// <summary>Business dates and wall-clock times in a site's own time zone (house rule 2); storage stays UTC.</summary>
public static class SiteClock
{
    public static TimeZoneInfo Zone(string timeZoneId) => TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

    public static DateTime ToLocal(DateTimeOffset instant, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(instant, zone).DateTime;

    public static DateOnly Today(DateTimeOffset now, TimeZoneInfo zone) => DateOnly.FromDateTime(ToLocal(now, zone));

    /// <summary>A local date + minutes after midnight (1440 = the next midnight) as a UTC instant.</summary>
    public static DateTime LocalToUtc(DateOnly date, int minutes, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified).AddMinutes(minutes), zone);
}
