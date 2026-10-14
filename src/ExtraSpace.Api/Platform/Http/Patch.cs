using ExtraSpace.Api.Platform.Errors;

namespace ExtraSpace.Api.Platform.Http;

/// <summary>
/// Admin PATCH rules (S1-1 decision 12, extending S0's UpdateStaffBody): null = unchanged; for text "" clears; a nullable number
/// or link is cleared by naming it in <c>clear</c>.
/// </summary>
public static class Patch
{
    public static ISet<string> Clears(string[]? clear, params string[] clearable)
    {
        var names = new HashSet<string>(clear ?? [], StringComparer.Ordinal);
        foreach (var name in names)
            if (name is null || !clearable.Contains(name)) throw Invalid($"'{name ?? "null"}' cannot be cleared.");
        return names;
    }

    public static void Text(string? value, Action<string?> set)
    {
        if (value is not null) set(Blank(value));
    }

    public static void Value<T>(T? value, string name, ISet<string> clear, Action<T?> set) where T : struct
    {
        if (value is not null && clear.Contains(name)) throw Invalid($"'{name}' cannot be set and cleared at once.");
        if (value is not null) set(value);
        else if (clear.Contains(name)) set(null);
    }

    /// <summary>Trimmed text, or null when blank.</summary>
    public static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Trimmed text; blank is 400.</summary>
    public static string Required(string value, string name) => Blank(value) ?? throw Invalid($"{name} cannot be blank.");

    private static AppException Invalid(string title) => new(400, ErrorCodes.ValidationFailed, title);
}
