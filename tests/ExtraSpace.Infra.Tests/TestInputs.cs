namespace ExtraSpace.Infra.Tests;

internal static class TestInputs
{
    public const string Zone = "app.example.test";

    public static DeployInputs For(string envName) => new()
    {
        Host = envName == "production" ? Zone : $"staging.{Zone}",
        ZoneName = Zone,
        ZoneId = "Z0000000TEST",
        EmailFrom = envName == "production" ? $"ExtraSpace <no-reply@{Zone}>" : $"ExtraSpace <no-reply@staging.{Zone}>",
        AlarmEmail = "alarms@example.test",
        AdminEmailDomains = ["example.test"],
        BootstrapEmails = ["qa@example.test"],
        EmailAllowedRecipients = envName == "production" ? [] : ["qa@example.test", "@team.example.test"],
        CorsOrigins = ["https://web.example.test"],
    };
}
