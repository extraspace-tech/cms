using Xunit;

namespace ExtraSpace.Infra.Tests;

public class DeployInputsTests
{
    [Fact]
    public void The_sender_gives_the_address_and_the_domain()
    {
        var inputs = TestInputs.For("staging");

        Assert.Equal("no-reply@staging.app.example.test", inputs.EmailFromAddress);
        Assert.Equal("staging.app.example.test", inputs.EmailDomain);
        Assert.Equal("plain@app.example.test", (inputs with { EmailFrom = " plain@app.example.test " }).EmailFromAddress);
    }

    [Fact]
    public void Valid_inputs_pass()
    {
        Assert.NotNull(TestInputs.For("staging").Validated(EnvConfig.Staging));
        Assert.NotNull(TestInputs.For("production").Validated(EnvConfig.Production));
    }

    [Fact]
    public void Staging_needs_an_email_allow_list()
    {
        var inputs = TestInputs.For("staging") with { EmailAllowedRecipients = [] };

        var error = Assert.Throws<InvalidOperationException>(() => inputs.Validated(EnvConfig.Staging));
        Assert.Contains("emailAllowedRecipients", error.Message);
    }

    [Fact]
    public void Staff_domains_are_required_everywhere()
    {
        var inputs = TestInputs.For("production") with { AdminEmailDomains = [] };

        var error = Assert.Throws<InvalidOperationException>(() => inputs.Validated(EnvConfig.Production));
        Assert.Contains("adminEmailDomains", error.Message);
    }

    [Theory]
    [InlineData("staging.other.example.test", "ExtraSpace <no-reply@staging.app.example.test>", "host")]
    [InlineData("staging.app.example.test", "ExtraSpace <no-reply@other.example.test>", "emailFrom")]
    [InlineData("evilapp.example.test", "ExtraSpace <no-reply@staging.app.example.test>", "host")]
    public void Host_and_sender_must_be_inside_the_zone(string host, string from, string expectedKey)
    {
        var inputs = TestInputs.For("staging") with { Host = host, EmailFrom = from };

        var error = Assert.Throws<InvalidOperationException>(() => inputs.Validated(EnvConfig.Staging));
        Assert.Contains(expectedKey, error.Message);
    }

    [Theory]
    [InlineData("app.example.test", "ExtraSpace <no-reply@staging.app.example.test>", "host")]
    [InlineData("APP.example.test", "ExtraSpace <no-reply@staging.app.example.test>", "host")]
    [InlineData("staging.app.example.test", "ExtraSpace <no-reply@app.example.test>", "emailFrom")]
    [InlineData("staging.app.example.test", "ExtraSpace <no-reply@App.Example.Test>", "emailFrom")]
    public void Outside_production_the_zone_itself_is_not_allowed(string host, string from, string expectedKey)
    {
        var inputs = TestInputs.For("staging") with { Host = host, EmailFrom = from };

        var error = Assert.Throws<InvalidOperationException>(() => inputs.Validated(EnvConfig.Staging));
        Assert.Contains($"{expectedKey} must not", error.Message);
        Assert.Contains("zone itself", error.Message);
    }

    [Fact]
    public void Production_may_use_the_zone_itself()
    {
        var inputs = TestInputs.For("production").Validated(EnvConfig.Production);

        Assert.Equal(TestInputs.Zone, inputs.Host);
        Assert.Equal(TestInputs.Zone, inputs.EmailDomain);
    }

    [Theory]
    [InlineData("staging", "mail.app.example.test", null, "host")]
    [InlineData("production", "mail.app.example.test", null, "host")]
    [InlineData("staging", "_dmarc.staging.app.example.test", null, "host")]
    [InlineData("production", "_DMARC.app.example.test", null, "host")]
    [InlineData("staging", null, "no-reply@mail.app.example.test", "emailFrom")]
    [InlineData("production", null, "no-reply@mail.app.example.test", "emailFrom")]
    [InlineData("staging", null, "ExtraSpace <no-reply@Mail.staging.app.example.test>", "emailFrom")]
    [InlineData("production", null, "no-reply@_dmarc.app.example.test", "emailFrom")]
    public void Names_another_environment_writes_records_on_are_refused(string env, string? host, string? from, string expectedKey)
    {
        var valid = TestInputs.For(env);
        var inputs = valid with { Host = host ?? valid.Host, EmailFrom = from ?? valid.EmailFrom };

        var error = Assert.Throws<InvalidOperationException>(() => inputs.Validated(env == "production" ? EnvConfig.Production : EnvConfig.Staging));
        Assert.Contains($"{expectedKey} must not", error.Message);
        Assert.Contains("mail. or _dmarc.", error.Message);
    }

    // Not normalised: the task role pins ses:FromAddress to the address as written, and the app sends EmailFrom as written.
    [Theory]
    [InlineData("Staging.app.example.test", null, null, "host")]
    [InlineData(null, "App.example.test", null, "zoneName")]
    [InlineData(null, null, "ExtraSpace <No-Reply@staging.app.example.test>", "emailFrom")]
    [InlineData(null, null, "ExtraSpace <no-reply@Staging.app.example.test>", "emailFrom")]
    [InlineData(null, null, "No-Reply@staging.app.example.test", "emailFrom")]
    public void Upper_case_in_the_host_the_zone_or_the_sender_address_is_refused(string? host, string? zone, string? from, string expectedKey)
    {
        var valid = TestInputs.For("staging");
        var inputs = valid with { Host = host ?? valid.Host, ZoneName = zone ?? valid.ZoneName, EmailFrom = from ?? valid.EmailFrom };

        var error = Assert.Throws<InvalidOperationException>(() => inputs.Validated(EnvConfig.Staging));
        Assert.Contains($"{expectedKey} must be lower case", error.Message);
    }

    [Fact]
    public void The_display_name_may_keep_capitals_and_the_address_is_read_as_written()
    {
        const string from = "ExtraSpace SG <no-reply@staging.app.example.test>";
        var inputs = (TestInputs.For("staging") with { EmailFrom = from }).Validated(EnvConfig.Staging);

        Assert.Equal(from, inputs.EmailFrom);
        Assert.Equal("no-reply@staging.app.example.test", inputs.EmailFromAddress);
        Assert.Equal("staging.app.example.test", inputs.EmailDomain);
        Assert.Equal("No-Reply@staging.app.example.test", (inputs with { EmailFrom = "X <No-Reply@staging.app.example.test>" }).EmailFromAddress);
    }

    [Theory]
    [InlineData("no-reply@x@staging.app.example.test")]
    [InlineData("ExtraSpace <a@b@staging.app.example.test>")]
    public void A_sender_with_two_at_signs_is_refused(string from)
    {
        var inputs = TestInputs.For("staging") with { EmailFrom = from };

        var error = Assert.Throws<InvalidOperationException>(() => inputs.Validated(EnvConfig.Staging));
        Assert.Contains("emailFrom must be an address", error.Message);
    }

    [Theory]
    [InlineData("mail.app.example.test", null)]
    [InlineData(null, "no-reply@_dmarc.app.example.test")]
    public void The_reserved_label_message_has_no_double_dot(string? host, string? from)
    {
        var valid = TestInputs.For("production");
        var inputs = valid with { Host = host ?? valid.Host, EmailFrom = from ?? valid.EmailFrom };

        var error = Assert.Throws<InvalidOperationException>(() => inputs.Validated(EnvConfig.Production));
        Assert.DoesNotContain("..", error.Message);
        Assert.EndsWith(".", error.Message);
    }

    [Fact]
    public void Load_reads_a_camel_case_file_and_returns_null_without_one()
    {
        var directory = Directory.CreateTempSubdirectory("es-inputs-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "staging.json"), """
                {
                  "host": "staging.app.example.test",
                  "zoneName": "app.example.test",
                  "zoneId": "Z0000000TEST",
                  "emailFrom": "ExtraSpace <no-reply@staging.app.example.test>",
                  "alarmEmail": "alarms@example.test",
                  "adminEmailDomains": ["example.test"],
                  "emailAllowedRecipients": ["qa@example.test"]
                }
                """);

            var loaded = DeployInputs.Load(directory, "staging");

            Assert.Equal("staging.app.example.test", loaded!.Host);
            Assert.Equal(new[] { "qa@example.test" }, loaded.EmailAllowedRecipients);
            Assert.Empty(loaded.BootstrapEmails);
            Assert.Null(DeployInputs.Load(directory, "production"));
            Assert.Null(DeployInputs.Load(null, "staging"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("adminEmailDomains")]
    [InlineData("emailAllowedRecipients")]
    public void A_blank_list_entry_counts_as_not_set(string key)
    {
        var inputs = key == "adminEmailDomains"
            ? TestInputs.For("staging") with { AdminEmailDomains = ["example.test", " "] }
            : TestInputs.For("staging") with { EmailAllowedRecipients = [" "] };

        var error = Assert.Throws<InvalidOperationException>(() => inputs.Validated(EnvConfig.Staging));
        Assert.Contains(key, error.Message);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("zoneName")]
    [InlineData("zoneId")]
    public void Blank_required_values_are_rejected(string key)
    {
        var inputs = key switch
        {
            "host" => TestInputs.For("staging") with { Host = " " },
            "zoneName" => TestInputs.For("staging") with { ZoneName = "" },
            _ => TestInputs.For("staging") with { ZoneId = " " },
        };

        var error = Assert.Throws<InvalidOperationException>(() => inputs.Validated(EnvConfig.Staging));
        Assert.Contains($"{key} must be set", error.Message);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("zoneName")]
    [InlineData("zoneId")]
    [InlineData("emailFrom")]
    [InlineData("alarmEmail")]
    public void A_null_required_value_names_its_key(string key)
    {
        var loaded = LoadStaging(System.Text.RegularExpressions.Regex.Replace(ValidFile, $"\"{key}\": \"[^\"]*\"", $"\"{key}\": null"));

        var error = Assert.Throws<InvalidOperationException>(() => loaded.Validated(EnvConfig.Staging));
        Assert.Contains($"{key} must be set", error.Message);
    }

    [Fact]
    public void A_null_list_names_its_key()
    {
        var loaded = LoadStaging(ValidFile.Replace("\"adminEmailDomains\": [\"example.test\"]", "\"adminEmailDomains\": null"));

        var error = Assert.Throws<InvalidOperationException>(() => loaded.Validated(EnvConfig.Staging));
        Assert.Contains("adminEmailDomains", error.Message);
    }

    [Theory]
    [InlineData("bootstrapEmails")]
    [InlineData("corsOrigins")]
    public void A_null_optional_list_loads_as_empty(string key)
    {
        var loaded = LoadStaging(ValidFile.Replace("\"alarmEmail\"", $"\"{key}\": null, \"alarmEmail\""));

        Assert.Empty(loaded.BootstrapEmails);
        Assert.Empty(loaded.CorsOrigins);
        var valid = loaded.Validated(EnvConfig.Staging);
        Assert.Empty(valid.BootstrapEmails);
        Assert.Empty(valid.CorsOrigins);
    }

    [Fact]
    public void A_null_allow_list_names_its_key_on_staging_and_is_empty_in_production()
    {
        var staging = TestInputs.For("staging") with { EmailAllowedRecipients = null! };
        var production = TestInputs.For("production") with { EmailAllowedRecipients = null! };

        var error = Assert.Throws<InvalidOperationException>(() => staging.Validated(EnvConfig.Staging));
        Assert.Contains("emailAllowedRecipients must be set", error.Message);
        Assert.Empty(production.Validated(EnvConfig.Production).EmailAllowedRecipients);
    }

    [Theory]
    [InlineData("staging", "bootstrapEmails")]
    [InlineData("production", "bootstrapEmails")]
    [InlineData("staging", "corsOrigins")]
    [InlineData("production", "corsOrigins")]
    public void A_blank_bootstrap_or_cors_entry_is_refused(string env, string key)
    {
        var valid = TestInputs.For(env);
        var inputs = key == "bootstrapEmails"
            ? valid with { BootstrapEmails = [.. valid.BootstrapEmails, " "] }
            : valid with { CorsOrigins = [.. valid.CorsOrigins, ""] };

        var error = Assert.Throws<InvalidOperationException>(() => inputs.Validated(env == "production" ? EnvConfig.Production : EnvConfig.Staging));
        Assert.Contains($"{key} must not have a blank entry", error.Message);
    }

    [Theory]
    [InlineData("example.test", "must list addresses or @domain entries")]
    [InlineData("QA@example.test", "must be lower case")]
    [InlineData("@Team.example.test", "must be lower case")]
    [InlineData(" qa@example.test", "must not have spaces")]
    [InlineData("@team.example.test ", "must not have spaces")]
    public void A_malformed_allow_list_entry_is_refused(string entry, string expectedRule)
    {
        var inputs = TestInputs.For("staging") with { EmailAllowedRecipients = ["qa@example.test", entry] };

        var error = Assert.Throws<InvalidOperationException>(() => inputs.Validated(EnvConfig.Staging));
        Assert.Contains($"emailAllowedRecipients {expectedRule}", error.Message);
    }

    [Theory]
    [InlineData("owner@example.test")]
    [InlineData("owner@other.example.test")]
    [InlineData("owner@notteam.example.test")]
    public void Staging_refuses_a_bootstrap_email_off_the_allow_list(string email)
    {
        var inputs = TestInputs.For("staging") with { BootstrapEmails = ["qa@example.test", email] };

        var error = Assert.Throws<InvalidOperationException>(() => inputs.Validated(EnvConfig.Staging));
        Assert.Contains("bootstrapEmails", error.Message);
        Assert.Contains("not on the staging allow-list", error.Message);
        Assert.DoesNotContain(email, error.Message);
    }

    [Theory]
    [InlineData("owner@team.example.test")]
    [InlineData("Owner@Team.Example.Test")]
    [InlineData("QA@example.test")]
    public void Staging_accepts_a_bootstrap_email_the_allow_list_covers(string email)
    {
        var inputs = TestInputs.For("staging") with { BootstrapEmails = [email] };

        Assert.Equal(new[] { email }, inputs.Validated(EnvConfig.Staging).BootstrapEmails);
    }

    [Fact]
    public void Production_has_no_allow_list_so_any_bootstrap_email_passes()
    {
        var inputs = TestInputs.For("production") with { BootstrapEmails = ["owner@example.test"] };

        Assert.Empty(inputs.EmailAllowedRecipients);
        Assert.Equal(new[] { "owner@example.test" }, inputs.Validated(EnvConfig.Production).BootstrapEmails);
    }

    [Fact]
    public void An_unknown_key_stops_the_load()
    {
        var error = Assert.Throws<InvalidOperationException>(() => LoadStaging(ValidFile.Replace("\"alarmEmail\"", "\"corsOrigin\": [], \"alarmEmail\"")));

        Assert.Contains("corsOrigin", error.Message);
    }

    [Fact]
    public void A_missing_required_key_stops_the_load()
    {
        var error = Assert.Throws<InvalidOperationException>(() => LoadStaging(ValidFile.Replace("\"zoneId\": \"Z0000000TEST\",", "")));

        Assert.Contains("zoneId", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private const string ValidFile = """
        {
          "host": "staging.app.example.test",
          "zoneName": "app.example.test",
          "zoneId": "Z0000000TEST",
          "emailFrom": "ExtraSpace <no-reply@staging.app.example.test>",
          "alarmEmail": "alarms@example.test",
          "adminEmailDomains": ["example.test"],
          "emailAllowedRecipients": ["qa@example.test"]
        }
        """;

    private static DeployInputs LoadStaging(string json)
    {
        var directory = Directory.CreateTempSubdirectory("es-inputs-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "staging.json"), json);
            return DeployInputs.Load(directory, "staging")!;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
