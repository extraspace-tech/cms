using System.Globalization;
using System.Text.RegularExpressions;

namespace ExtraSpace.Api.Integrations.SiteLink;

/// <summary>
/// Strict readers for SiteLink's text values. A value that does not parse throws <see cref="FormatException"/> naming the column
/// and the expected kind, never the value: the message reaches logs and the sync state.
/// </summary>
internal static partial class SiteLinkValues
{
    /// <summary>SiteLink's null date: any year before 1800 (API PDF, wire rules).</summary>
    public const string NullDate = "1700-01-01T00:00:00";

    /// <summary>Strict: "true"/"1" and "false"/"0", any case. The demo's truthy-string bug made every flag true.</summary>
    public static bool ParseBool(string value, string? column = null) => value.Trim().ToLowerInvariant() switch
    {
        "true" or "1" => true,
        "false" or "0" => false,
        _ => throw Unreadable(column, "a boolean"),
    };

    public static int ParseInt(string value, string? column = null) =>
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : throw Unreadable(column, "an integer");

    public static long ParseLong(string value, string? column = null) =>
        long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : throw Unreadable(column, "an integer");

    public static decimal ParseDecimal(string value, string? column = null) =>
        decimal.TryParse(value.Trim(), NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var number)
            ? number : throw Unreadable(column, "a number");

    /// <summary>
    /// SiteLink sends site-local times without an offset; a value with an offset is taken as sent. Returns UTC, or null for a
    /// blank value or a year before 1800.
    /// </summary>
    public static DateTime? ParseDate(string? value, TimeZoneInfo zone, string? column = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (HasOffset().IsMatch(text))
        {
            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant)) throw Unreadable(column, "a date");
            return instant.Year < 1800 ? null : instant.UtcDateTime;
        }
        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) throw Unreadable(column, "a date");
        return local.Year < 1800 ? null : TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);
    }

    private static FormatException Unreadable(string? column, string kind) =>
        new(column is null ? $"The value is not {kind}." : $"Column {column} is not {kind}.");

    [GeneratedRegex(@"(Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex HasOffset();
}
