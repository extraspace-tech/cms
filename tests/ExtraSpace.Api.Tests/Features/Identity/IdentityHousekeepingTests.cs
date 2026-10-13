using ExtraSpace.Api.Features.Identity;
using ExtraSpace.Api.Tests.TestSupport;
using Hangfire;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExtraSpace.Api.Tests.Features.Identity;

public sealed class IdentityHousekeepingTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task It_deletes_only_old_rows_and_can_run_twice()
    {
        var customer = await api.CreateCustomerAsync();
        var now = DateTime.UtcNow;
        var oldCode = IdentityTestData.NewEmail("old");
        var freshCode = IdentityTestData.NewEmail("fresh");
        await api.WithDbAsync(async db =>
        {
            db.AddRange(
                new CustomerLoginCode { Email = oldCode, CodeHash = "x", ExpiresAt = now.AddDays(-2), LastSentAt = now.AddDays(-2), WindowStartedAt = now.AddDays(-2) },
                new CustomerLoginCode { Email = freshCode, CodeHash = "x", ExpiresAt = now.AddHours(-1), LastSentAt = now.AddHours(-1), WindowStartedAt = now.AddHours(-1) },
                new CustomerRefreshToken { CustomerId = customer.Id, FamilyId = Guid.CreateVersion7(), TokenHash = "old-" + Guid.NewGuid().ToString("N"), ExpiresAt = now.AddDays(-31) },
                new CustomerRefreshToken { CustomerId = customer.Id, FamilyId = Guid.CreateVersion7(), TokenHash = "recent-" + Guid.NewGuid().ToString("N"), ExpiresAt = now.AddDays(-29) });
            await db.SaveChangesAsync(Ct);
        });

        for (var run = 0; run < 2; run++)
        {
            await using var scope = api.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IdentityHousekeepingJob>().RunAsync(Ct);
        }

        await api.WithDbAsync(async db =>
        {
            (await db.Set<CustomerLoginCode>().AnyAsync(c => c.Email == oldCode, Ct)).Should().BeFalse();
            (await db.Set<CustomerLoginCode>().AnyAsync(c => c.Email == freshCode, Ct)).Should().BeTrue();
            var tokens = await db.Set<CustomerRefreshToken>().Where(t => t.CustomerId == customer.Id).Select(t => t.TokenHash).ToListAsync(Ct);
            tokens.Should().ContainSingle().Which.Should().StartWith("recent-");
        });
    }

    [Fact]
    public void It_is_scheduled_daily_at_3am_singapore_time()
    {
        using var connection = api.Services.GetRequiredService<JobStorage>().GetConnection();
        var job = connection.GetRecurringJobs().Single(j => j.Id == "IdentityHousekeeping");
        job.Cron.Should().Be("0 3 * * *");
        job.TimeZoneId.Should().Be("Asia/Singapore");
    }
}
