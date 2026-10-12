namespace ExtraSpace.Api.Platform.Logging;

public static class Pii
{
    /// <summary><c>jane@gmail.com</c> → <c>j***@gmail.com</c>. The only way an email may appear in a log (S0 spec §4.4).</summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "***";
        var trimmed = email.Trim();
        var at = trimmed.LastIndexOf('@');
        if (at <= 0 || at == trimmed.Length - 1) return "***";
        return string.Concat(trimmed.AsSpan(0, 1), "***", trimmed.AsSpan(at));
    }
}
