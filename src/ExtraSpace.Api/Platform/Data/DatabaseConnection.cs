using Npgsql;

namespace ExtraSpace.Api.Platform.Data;

internal static class DatabaseConnection
{
    /// <summary>
    /// On AWS the task gets the host and the Secrets Manager user and password as separate values,
    /// so a password rotation never means editing a connection string. Locally ConnectionStrings:Default is set whole.
    /// </summary>
    public static void Compose(IConfigurationManager configuration)
    {
        var database = configuration.GetSection("Database");
        if (database["Host"] is not { Length: > 0 } host) return;

        configuration["ConnectionStrings:Default"] = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = database.GetValue("Port", 5432),
            Database = database["Name"] is { Length: > 0 } name ? name : "extraspace",
            Username = Required(database, "Username"),
            Password = Required(database, "Password"),
            SslMode = SslMode.Require,
            MaxPoolSize = database.GetValue("MaxPoolSize", 50),
        }.ConnectionString;
    }

    private static string Required(IConfigurationSection database, string key) =>
        database[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"Database:{key} must be set when Database:Host is set.");
}
