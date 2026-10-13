using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace ExtraSpace.Api.Integrations.Email;

/// <summary>Templates are embedded files <c>Templates/&lt;name&gt;.html</c> and <c>.txt</c> with <c>{{value}}</c> placeholders.</summary>
internal static partial class EmailTemplates
{
    private static readonly Dictionary<string, string> Subjects = new(StringComparer.Ordinal)
    {
        ["login-code"] = "Your ExtraSpace sign-in code",
    };

    public static void EnsureKnown(string template)
    {
        if (!Subjects.ContainsKey(template)) throw new InvalidOperationException($"Unknown email template '{template}'.");
    }

    public static RenderedEmail Render(string template, string to, IReadOnlyDictionary<string, string> values)
    {
        EnsureKnown(template);
        return new RenderedEmail(to, Subjects[template],
            Fill(Load(template + ".html"), values, HtmlEncoder.Default.Encode),
            Fill(Load(template + ".txt"), values, v => v));
    }

    private static string Load(string file)
    {
        using var stream = typeof(EmailTemplates).Assembly.GetManifestResourceStream("email/" + file)
            ?? throw new InvalidOperationException($"Email template file '{file}' is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string Fill(string text, IReadOnlyDictionary<string, string> values, Func<string, string> encode) =>
        Placeholder().Replace(text, m => values.TryGetValue(m.Groups[1].Value, out var value)
            ? encode(value)
            : throw new InvalidOperationException($"Email value '{m.Groups[1].Value}' is missing."));

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex Placeholder();
}
