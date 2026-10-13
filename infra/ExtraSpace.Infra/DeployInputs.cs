using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExtraSpace.Infra;

/// <summary>
/// Per-environment values that must not sit in the public repo (host, zone id, addresses).
/// They live as &lt;env&gt;.json in the folder named by ES_DEPLOY_INPUTS (the private context repo).
/// </summary>
public sealed record DeployInputs
{
    public required string Host { get; init; }
    public required string ZoneName { get; init; }
    public required string ZoneId { get; init; }
    public required string EmailFrom { get; init; }
    public required string AlarmEmail { get; init; }
    public string[] AdminEmailDomains { get; init; } = [];
    public string[] BootstrapEmails { get; init; } = [];
    public string[] EmailAllowedRecipients { get; init; } = [];
    public string[] CorsOrigins { get; init; } = [];

    // As written: the task role pins ses:FromAddress to this text and the app sends EmailFrom unchanged, so the two must match.
    public string EmailFromAddress => EmailFrom.Contains('<')
        ? EmailFrom[(EmailFrom.IndexOf('<') + 1)..].Trim().TrimEnd('>').Trim()
        : EmailFrom.Trim();

    public string EmailDomain => EmailFromAddress[(EmailFromAddress.IndexOf('@') + 1)..];

    // An unknown key fails: a misspelt optional key would otherwise be ignored in silence.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static DeployInputs? Load(string? directory, string envName)
    {
        if (string.IsNullOrWhiteSpace(directory)) return null;
        var path = Path.Combine(directory, $"{envName}.json");
        if (!File.Exists(path)) return null;
        try
        {
            var inputs = JsonSerializer.Deserialize<DeployInputs>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidOperationException($"{path} is empty.");
            return inputs.WithLists();
        }
        catch (JsonException e)
        {
            // Covers a misspelt key and a missing required one; the message names the key, never a value.
            throw new InvalidOperationException($"Deploy inputs file {envName}.json is not valid: {e.Message}", e);
        }
    }

    public DeployInputs Validated(EnvConfig config) => WithLists().Checked(config);

    // A JSON null list gets past the `= []` default.
    private DeployInputs WithLists() => this with
    {
        AdminEmailDomains = AdminEmailDomains ?? [],
        BootstrapEmails = BootstrapEmails ?? [],
        EmailAllowedRecipients = EmailAllowedRecipients ?? [],
        CorsOrigins = CorsOrigins ?? [],
    };

    private DeployInputs Checked(EnvConfig config)
    {
        // JSON null gets past `required`, so these also guard the members read below.
        if (string.IsNullOrWhiteSpace(Host)) Fail(config, "host", "must be set");
        if (string.IsNullOrWhiteSpace(ZoneName)) Fail(config, "zoneName", "must be set");
        if (string.IsNullOrWhiteSpace(ZoneId)) Fail(config, "zoneId", "must be set");
        if (string.IsNullOrWhiteSpace(EmailFrom)) Fail(config, "emailFrom", "must be set");
        if (string.IsNullOrWhiteSpace(AlarmEmail)) Fail(config, "alarmEmail", "must be set");
        if (!InZone(Host)) Fail(config, "host", $"must be {ZoneName} or a name inside it");
        if (EmailFromAddress.Count(c => c == '@') != 1 || !InZone(EmailDomain)) Fail(config, "emailFrom", $"must be an address inside {ZoneName}");
        // Staging and production share one zone: the apex is production's, and each environment
        // writes mail.<its domain> and _dmarc.<its domain>, so no host or sender may sit on those names.
        if (!config.IsProduction && IsApex(Host)) Fail(config, "host", "must not be the zone itself outside production");
        if (!config.IsProduction && IsApex(EmailDomain)) Fail(config, "emailFrom", "must not use the zone itself outside production");
        if (HasReservedLabel(Host)) Fail(config, "host", "must not begin with mail. or _dmarc. (both are written by the email domain)");
        if (HasReservedLabel(EmailDomain)) Fail(config, "emailFrom", "must not use a domain that begins with mail. or _dmarc. (both are written by the email domain)");
        // Not normalised: the sender is compared by IAM exactly as the app sends it, and the names become DNS records.
        if (HasUpper(Host)) Fail(config, "host", "must be lower case");
        if (HasUpper(ZoneName)) Fail(config, "zoneName", "must be lower case");
        if (HasUpper(EmailFromAddress)) Fail(config, "emailFrom", "must be lower case in the address (the display name may keep capitals)");
        if (!AlarmEmail.Contains('@')) Fail(config, "alarmEmail", "must be an email address");
        // Empty would let any address be added as staff and receive a sign-in code.
        if (NoneOrBlank(AdminEmailDomains)) Fail(config, "adminEmailDomains", "must list the company's email domains");
        // Once SES leaves the sandbox, staging could email anyone who types an address into it.
        if (!config.IsProduction && NoneOrBlank(EmailAllowedRecipients)) Fail(config, "emailAllowedRecipients", "must be set outside production");
        if (EmailAllowedRecipients.Any(entry => !entry.Contains('@'))) Fail(config, "emailAllowedRecipients", "must list addresses or @domain entries");
        if (EmailAllowedRecipients.Any(entry => entry != entry.Trim())) Fail(config, "emailAllowedRecipients", "must not have spaces around an entry");
        if (EmailAllowedRecipients.Any(HasUpper)) Fail(config, "emailAllowedRecipients", "must be lower case");
        if (BootstrapEmails.Any(string.IsNullOrWhiteSpace)) Fail(config, "bootstrapEmails", "must not have a blank entry");
        if (CorsOrigins.Any(string.IsNullOrWhiteSpace)) Fail(config, "corsOrigins", "must not have a blank entry");
        // Otherwise the first Owner on staging asks for a sign-in code and the email is never sent.
        if (!config.IsProduction && !BootstrapEmails.All(Allowed)) Fail(config, "bootstrapEmails", "has an address that is not on the staging allow-list (emailAllowedRecipients)");
        return this with { Host = Host.Trim() };
    }

    private static bool NoneOrBlank(string[] values) => values.Length == 0 || values.Any(string.IsNullOrWhiteSpace);

    // The same rule as EmailOptions.Allows in the Api project.
    private bool Allowed(string to) => EmailAllowedRecipients.Any(entry =>
        entry.StartsWith('@')
            ? to.EndsWith(entry, StringComparison.OrdinalIgnoreCase)
            : to.Equals(entry, StringComparison.OrdinalIgnoreCase));

    private bool InZone(string name) =>
        name.Equals(ZoneName, StringComparison.OrdinalIgnoreCase) || name.EndsWith("." + ZoneName, StringComparison.OrdinalIgnoreCase);

    private static bool HasUpper(string value) => value.Any(char.IsUpper);

    private bool IsApex(string name) => name.Trim().Equals(ZoneName.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool HasReservedLabel(string name) =>
        name.TrimStart().StartsWith("mail.", StringComparison.OrdinalIgnoreCase) || name.TrimStart().StartsWith("_dmarc.", StringComparison.OrdinalIgnoreCase);

    private static void Fail(EnvConfig config, string key, string rule) =>
        throw new InvalidOperationException($"Deploy inputs for {config.Name}: {key} {rule}.");
}
