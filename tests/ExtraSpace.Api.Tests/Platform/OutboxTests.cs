using System.Text.Json;
using ExtraSpace.Api.Integrations.Email;
using ExtraSpace.Api.Platform.Data;
using ExtraSpace.Api.Platform.Outbox;
using ExtraSpace.Api.Platform.Tracing;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ExtraSpace.Api.Tests.Platform;

public sealed class OutboxTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewKey() => "test:" + Guid.NewGuid().ToString("N");

    private static string? Str(JsonElement line, string name) =>
        line.TryGetProperty(name, out var value) ? value.ToString() : null;

    private async Task<OutboxMessage?> FindAsync(string key)
    {
        OutboxMessage? row = null;
        await api.WithDbAsync(async db => row = await db.Set<OutboxMessage>().AsNoTracking().SingleOrDefaultAsync(m => m.IdempotencyKey == key, Ct));
        return row;
    }

    private async Task WaitUntilAsync(Func<Task<bool>> done)
    {
        for (var i = 0; i < 100 && !await done(); i++) await Task.Delay(100, Ct);
        (await done()).Should().BeTrue();
    }

    [Fact]
    public async Task A_step_saved_with_its_change_runs_once_soon()
    {
        var key = NewKey();
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(Ct);
            scope.ServiceProvider.GetRequiredService<IOutbox>().Enqueue(TestOutboxHandler.KindName, new { note = "hello" }, key);
            await db.SaveChangesAsync(Ct);
            await transaction.CommitAsync(Ct);
        }

        // The nudge after commit runs it well before the minute sweep.
        await WaitUntilAsync(async () => (await FindAsync(key))?.ProcessedAt is not null);
        TestOutboxHandler.Runs[key].Should().Be(1);
        (await FindAsync(key))!.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task A_rolled_back_transaction_leaves_no_step()
    {
        var key = NewKey();
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(Ct);
            scope.ServiceProvider.GetRequiredService<IOutbox>().Enqueue(TestOutboxHandler.KindName, new { }, key);
            await db.SaveChangesAsync(Ct);
            await transaction.RollbackAsync(Ct);
        }

        await Task.Delay(1500, Ct);
        (await FindAsync(key)).Should().BeNull();
        TestOutboxHandler.Runs.ContainsKey(key).Should().BeFalse();
    }

    [Fact]
    public async Task The_same_idempotency_key_cannot_be_saved_twice()
    {
        var key = NewKey();
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutbox>();
        outbox.Enqueue(TestOutboxHandler.KindName, new { }, key);
        await db.SaveChangesAsync(Ct);
        outbox.Enqueue(TestOutboxHandler.KindName, new { }, key);

        var save = () => db.SaveChangesAsync(Ct);
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task A_failing_step_backs_off_then_is_marked_failed()
    {
        // Fake time a day ahead: the host's own dispatcher (real clock) never sees these rows as due.
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow.AddDays(1));
        var key = NewKey();
        await api.WithDbAsync(async db =>
        {
            db.Add(new OutboxMessage
            {
                Kind = TestOutboxHandler.KindName, IdempotencyKey = key,
                Payload = JsonSerializer.SerializeToDocument(new { failTimes = 99 }),
                CreatedAt = time.GetUtcNow().UtcDateTime, AvailableAt = time.GetUtcNow().UtcDateTime,
            });
            await db.SaveChangesAsync(Ct);
        });

        var expectedDelays = new[] { 1, 5, 15, 60 };
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await using (var scope = api.Services.CreateAsyncScope())
                await ActivatorUtilities.CreateInstance<OutboxDispatchJob>(scope.ServiceProvider, time).RunAsync(Ct);
            var row = (await FindAsync(key))!;
            row.Attempts.Should().Be(attempt);
            row.LastError.Should().Be("InvalidOperationException: test-outbox-failure");
            if (attempt < 5)
            {
                row.FailedAt.Should().BeNull();
                row.AvailableAt.Should().BeCloseTo(time.GetUtcNow().UtcDateTime.AddMinutes(expectedDelays[attempt - 1]), TimeSpan.FromSeconds(1));
                time.Advance(TimeSpan.FromMinutes(expectedDelays[attempt - 1]));
            }
            else
            {
                row.FailedAt.Should().NotBeNull();
            }
        }
        var failed = (await FindAsync(key))!;
        var lines = await api.Logs.WaitForAsync(l =>
            Str(l, "messageTemplate") == "Outbox message {outboxId} of kind {kind} failed permanently after {attempt} attempts"
            && Str(l, "outboxId") == failed.Id.ToString());
        Str(lines.Single(), "level").Should().Be("Error");
    }

    [Fact]
    public async Task Parallel_dispatchers_run_each_step_once()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow.AddDays(2));
        var keys = Enumerable.Range(0, 20).Select(_ => NewKey()).ToList();
        await api.WithDbAsync(async db =>
        {
            foreach (var key in keys)
                db.Add(new OutboxMessage
                {
                    Kind = TestOutboxHandler.KindName, IdempotencyKey = key, Payload = JsonSerializer.SerializeToDocument(new { }),
                    CreatedAt = time.GetUtcNow().UtcDateTime, AvailableAt = time.GetUtcNow().UtcDateTime,
                });
            await db.SaveChangesAsync(Ct);
        });

        await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
        {
            await using var scope = api.Services.CreateAsyncScope();
            await ActivatorUtilities.CreateInstance<OutboxDispatchJob>(scope.ServiceProvider, time).RunAsync(Ct);
        }));

        keys.Should().AllSatisfy(key => TestOutboxHandler.Runs[key].Should().Be(1));
    }

    [Fact]
    public async Task The_login_code_email_goes_through_the_outbox_sealed()
    {
        var client = api.ClientWithIp();
        var email = IdentityTestData.NewEmail("outbox");

        var response = await client.RequestCodeAsync(email);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Accepted);
        var traceId = response.Headers.GetValues(RequestTrace.HeaderName).Single();
        var code = await api.Emails.CodeForAsync(email);

        // The row carries this request's trace id as its correlationId, so it is this request's row and no other test's.
        List<string> payloads = [];
        await api.WithDbAsync(async db => payloads = await db.Database
            .SqlQuery<string>($"SELECT payload::text AS \"Value\" FROM outbox_message WHERE kind = 'Email' AND correlation_id = {traceId}")
            .ToListAsync(Ct));
        payloads.Should().ContainSingle().Which.Should().NotContain(email).And.NotContain(code);
    }

    [Fact]
    public async Task A_processed_email_step_no_longer_carries_the_sealed_blob()
    {
        var key = "";
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(Ct);
            scope.ServiceProvider.GetRequiredService<IEmailSender>().Enqueue(new EmailMessage(
                "login-code", IdentityTestData.NewEmail("outbox-clear"), new Dictionary<string, string> { ["code"] = "123456", ["minutes"] = "10" },
                TimeSpan.FromMinutes(10)));
            await db.SaveChangesAsync(Ct);
            var row = db.ChangeTracker.Entries<OutboxMessage>().Single().Entity;
            key = row.IdempotencyKey;
            row.Payload.RootElement.GetProperty("payload").GetString().Should().NotBeNullOrEmpty();   // sealed while it waits
            await transaction.CommitAsync(Ct);
        }

        await WaitUntilAsync(async () => (await FindAsync(key))?.ProcessedAt is not null);
        string? stored = null;
        await api.WithDbAsync(async db => stored = await db.Database
            .SqlQuery<string>($"SELECT payload::text AS \"Value\" FROM outbox_message WHERE idempotency_key = {key}").SingleAsync(Ct));
        stored.Should().Be("{}");
    }
}
