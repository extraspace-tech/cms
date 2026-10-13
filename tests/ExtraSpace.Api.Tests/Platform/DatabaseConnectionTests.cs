using ExtraSpace.Api.Platform.Data;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace ExtraSpace.Api.Tests.Platform;

public sealed class DatabaseConnectionTests
{
    private static ConfigurationManager Config(params (string Key, string Value)[] values)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value));
        return configuration;
    }

    [Fact]
    public void Parts_become_the_connection_string()
    {
        var configuration = Config(("Database:Host", "db.internal.test"), ("Database:Username", "es_app"), ("Database:Password", "p;w='d"));

        DatabaseConnection.Compose(configuration);

        var built = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("Default"));
        built.Host.Should().Be("db.internal.test");
        built.Port.Should().Be(5432);
        built.Database.Should().Be("extraspace");
        built.Username.Should().Be("es_app");
        built.Password.Should().Be("p;w='d");
        built.SslMode.Should().Be(SslMode.Require);
        built.MaxPoolSize.Should().Be(50);
    }

    [Fact]
    public void Without_a_host_the_given_connection_string_stays()
    {
        var configuration = Config(("ConnectionStrings:Default", "Host=localhost;Port=5433;Database=extraspace;Username=postgres"));

        DatabaseConnection.Compose(configuration);

        configuration.GetConnectionString("Default").Should().Be("Host=localhost;Port=5433;Database=extraspace;Username=postgres");
    }

    [Theory]
    [InlineData("Database:Username")]
    [InlineData("Database:Password")]
    public void A_host_without_credentials_fails_and_names_the_missing_key(string missing)
    {
        var values = new List<(string, string)> { ("Database:Host", "db.internal.test"), ("Database:Username", "es_app"), ("Database:Password", "secret-value") };
        values.RemoveAll(v => v.Item1 == missing);

        var compose = () => DatabaseConnection.Compose(Config([.. values]));

        compose.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain(missing).And.NotContain("secret-value");
    }
}
