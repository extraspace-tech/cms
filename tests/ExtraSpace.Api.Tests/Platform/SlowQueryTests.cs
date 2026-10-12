using ExtraSpace.Api.Platform.Data;
using ExtraSpace.Api.Platform.Logging;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ExtraSpace.Api.Tests.Platform;

public sealed class SlowQueryTests(ApiFactory api)
{
    [Fact]
    public async Task Slow_commands_log_a_warning_with_the_sql_but_never_parameter_values()
    {
        var ct = TestContext.Current.CancellationToken;
        var logger = new FakeLogger<SlowQueryInterceptor>();
        var interceptor = new SlowQueryInterceptor(Options.Create(new LoggingOptions { SlowQueryMs = 100 }), logger);
        var connection = await api.CreateDatabaseAsync("slow");
        await using var db = new StampedDbContext(new DbContextOptionsBuilder<StampedDbContext>()
            .UseNpgsql(connection).AddInterceptors(interceptor).Options);

        await db.Database.ExecuteSqlRawAsync("select 1", ct);
        logger.Collector.Count.Should().Be(0);

        await db.Database.ExecuteSqlRawAsync("select pg_sleep(0.3) where @p is not null", [new NpgsqlParameter("p", "SENTINEL-VALUE")], ct);
        var record = logger.Collector.GetSnapshot().Should().ContainSingle().Subject;
        record.Level.Should().Be(Microsoft.Extensions.Logging.LogLevel.Warning);
        record.Message.Should().Contain("pg_sleep").And.NotContain("SENTINEL-VALUE");
    }
}
