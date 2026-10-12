using ExtraSpace.Api.Platform.Data;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace ExtraSpace.Api.Tests.Platform;

public sealed class DataTests(ApiFactory api)
{
    [Fact]
    public async Task Migrations_create_data_protection_keys_in_snake_case()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new NpgsqlConnection(api.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "select column_name from information_schema.columns where table_name = 'data_protection_keys' order by column_name", connection);
        var columns = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) columns.Add(reader.GetString(0));
        columns.Should().Equal("friendly_name", "id", "xml");
    }

    [Fact]
    public async Task Data_protection_keys_are_stored_in_postgres()
    {
        var protector = api.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test");
        protector.Unprotect(protector.Protect("hello")).Should().Be("hello");

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.DataProtectionKeys.CountAsync(TestContext.Current.CancellationToken)).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Timestamps_are_utc_and_created_at_never_changes()
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 1, 2, 3, TimeSpan.Zero));
        var connection = await api.CreateDatabaseAsync("stamps");
        StampedDbContext Open() => new(new DbContextOptionsBuilder<StampedDbContext>()
            .UseNpgsql(connection).UseSnakeCaseNamingConvention()
            .AddInterceptors(new TimestampsInterceptor(time)).Options);

        var thing = new StampedThing { Name = "first" };
        await using (var db = Open())
        {
            await db.Database.EnsureCreatedAsync(ct);
            db.Things.Add(thing);
            await db.SaveChangesAsync(ct);
        }

        time.Advance(TimeSpan.FromMinutes(5));
        await using (var db = Open())
        {
            var loaded = await db.Things.SingleAsync(x => x.Id == thing.Id, ct);
            loaded.Name = "renamed";
            loaded.CreatedAt = DateTime.UnixEpoch;
            await db.SaveChangesAsync(ct);
        }

        await using (var db = Open())
        {
            var saved = await db.Things.AsNoTracking().SingleAsync(x => x.Id == thing.Id, ct);
            saved.CreatedAt.Should().Be(new DateTime(2026, 10, 12, 1, 2, 3, DateTimeKind.Utc));
            saved.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
            saved.UpdatedAt.Should().Be(new DateTime(2026, 10, 12, 1, 7, 3, DateTimeKind.Utc));
        }
    }
}
