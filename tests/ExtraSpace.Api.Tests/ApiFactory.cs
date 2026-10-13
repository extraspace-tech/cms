using ExtraSpace.Api.Integrations.Email;
using ExtraSpace.Api.Platform.Data;
using ExtraSpace.Api.Platform.Hosting;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace ExtraSpace.Api.Tests;

/// <summary>One PostgreSQL 18.6 container and one app host for the whole test run (xUnit assembly fixture).</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6").Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString).UseSnakeCaseNamingConvention().Options))
        {
            await db.Database.MigrateAsync();
        }
        _ = Services; // start the host only after the schema exists
    }

    public LogCapture Logs { get; } = new();

    public FakeEmailTransport Emails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("App:MinVersion:ios", "2.0.0");
        builder.UseSetting("App:MinVersion:android", "2.0.0");
        builder.UseSetting("Cors:AllowedOrigins:0", "https://app.example.test");
        builder.UseSetting("Jobs:QueuePollInterval", "00:00:00.200");
        builder.UseSetting("Email:Transport", "None");
        builder.UseSetting("CustomerAuth:SigningKey", IdentityTestData.SigningKey);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IEndpointMapper, TestEndpoints>();
            services.AddTransient<TestJobs>();
            services.AddSingleton<IEmailTransport>(Emails);
            // The validation generator only sees endpoint types of the compilation that calls AddValidation (TestBody lives here).
            services.AddValidation();
            services.AddSingleton<Serilog.Core.ILogEventSink>(Logs);
            services.Configure<RateLimiterOptions>(o => o.AddFixedWindowLimiter("test-tight", w =>
            {
                w.PermitLimit = 1;
                w.Window = TimeSpan.FromMinutes(1);
            }));
        });
    }

    private static int _ip;

    /// <summary>A client with its own X-Forwarded-For address, so per-IP limits of one test never hit another.</summary>
    public HttpClient ClientWithIp()
    {
        var n = Interlocked.Increment(ref _ip);
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.{(n >> 16) & 255}.{(n >> 8) & 255}.{n & 255}");
        return client;
    }

    /// <summary>A fresh empty database in the same container, for tests that need their own tables.</summary>
    public async Task<string> CreateDatabaseAsync(string prefix)
    {
        var name = $"{prefix}_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
        await command.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
