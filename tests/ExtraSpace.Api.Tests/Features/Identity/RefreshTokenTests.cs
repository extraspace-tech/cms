using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExtraSpace.Api.Features.Identity;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExtraSpace.Api.Tests.Features.Identity;

public sealed class RefreshTokenTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string? Str(JsonElement line, string name) =>
        line.TryGetProperty(name, out var value) ? value.ToString() : null;

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken }, Ct);

    private static Task<HttpResponseMessage> LogoutAsync(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken }, Ct);

    private static string Refresh(JsonElement tokens) => tokens.GetProperty("refreshToken").GetString()!;

    // timestamptz keeps microseconds; trimming the ticks keeps a ">= since" check exact.
    private static DateTime UtcNowMicroseconds()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
    }

    private async Task<Guid> FamilyOfAsync(string refreshToken)
    {
        var familyId = Guid.Empty;
        await api.WithDbAsync(async db => familyId = await db.Set<CustomerRefreshToken>()
            .Where(t => t.TokenHash == TokenService.Hash(refreshToken)).Select(t => t.FamilyId).SingleAsync(Ct));
        return familyId;
    }

    private Task FamilyIsRevokedSinceAsync(Guid familyId, DateTime since, int rows) =>
        api.WithDbAsync(async db =>
        {
            var family = await db.Set<CustomerRefreshToken>().Where(t => t.FamilyId == familyId).ToListAsync(Ct);
            family.Should().HaveCount(rows);
            family.Should().OnlyContain(t => t.RevokedAt != null && t.RevokedAt >= since && t.UpdatedAt == t.RevokedAt);
        });

    /// <summary>Holds the family's advisory lock on its own connection until the transaction ends.</summary>
    private async Task<(NpgsqlConnection Connection, NpgsqlTransaction Transaction)> HoldFamilyLockAsync(Guid familyId)
    {
        var connection = new NpgsqlConnection(api.ConnectionString);
        try
        {
            await connection.OpenAsync(Ct);
            var transaction = await connection.BeginTransactionAsync(Ct);
            await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))", connection, transaction);
            command.Parameters.AddWithValue("key", TokenService.FamilyLockKey(familyId));
            await command.ExecuteNonQueryAsync(Ct);
            return (connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync();   // closing the connection also rolls the transaction back
            throw;
        }
    }

    [Fact]
    public async Task A_refresh_rotates_both_tokens()
    {
        var client = api.ClientWithIp();
        var first = await api.SignInAsync(client, IdentityTestData.NewEmail());
        var response = await RefreshAsync(client, Refresh(first));
        response.EnsureSuccessStatusCode();
        var second = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        Refresh(second).Should().NotBe(Refresh(first));
        await api.WithDbAsync(async db =>
        {
            var old = await db.Set<CustomerRefreshToken>().SingleAsync(t => t.TokenHash == TokenService.Hash(Refresh(first)), Ct);
            var next = await db.Set<CustomerRefreshToken>().SingleAsync(t => t.TokenHash == TokenService.Hash(Refresh(second)), Ct);
            old.ReplacedById.Should().Be(next.Id);
            next.FamilyId.Should().Be(old.FamilyId);
            next.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(60), TimeSpan.FromMinutes(1));
        });
    }

    [Fact]
    public async Task Replaying_a_used_refresh_token_revokes_the_family()
    {
        var client = api.ClientWithIp();
        var first = await api.SignInAsync(client, IdentityTestData.NewEmail());
        var second = await (await RefreshAsync(client, Refresh(first))).Content.ReadFromJsonAsync<JsonElement>(Ct);

        var since = UtcNowMicroseconds();
        var replay = await RefreshAsync(client, Refresh(first));
        await Problem.ReadAsync(replay, 401, "UNAUTHENTICATED");
        await Problem.ReadAsync(await RefreshAsync(client, Refresh(second)), 401, "UNAUTHENTICATED");

        var warning = api.Logs.LinesFor(Problem.TraceIdOf(replay))
            .Where(l => Str(l, "messageTemplate") == "Refresh token family {familyId} revoked: reuse detected for customer {customerId}").ToList();
        warning.Should().ContainSingle();
        Str(warning[0], "level").Should().Be("Warning");
        await FamilyIsRevokedSinceAsync(await FamilyOfAsync(Refresh(first)), since, rows: 2);
    }

    [Fact]
    public async Task An_expired_refresh_token_is_401()
    {
        var client = api.ClientWithIp();
        var tokens = await api.SignInAsync(client, IdentityTestData.NewEmail());
        var hash = TokenService.Hash(Refresh(tokens));
        await api.WithDbAsync(db => db.Set<CustomerRefreshToken>().Where(t => t.TokenHash == hash)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAt, DateTime.UtcNow.AddSeconds(-1)), Ct));
        await Problem.ReadAsync(await RefreshAsync(client, Refresh(tokens)), 401, "UNAUTHENTICATED");
    }

    [Fact]
    public async Task An_unknown_refresh_token_is_401() =>
        await Problem.ReadAsync(await RefreshAsync(api.ClientWithIp(), "not-a-real-token"), 401, "UNAUTHENTICATED");

    [Fact]
    public async Task Logout_revokes_the_session_and_always_answers_204()
    {
        var client = api.ClientWithIp();
        var first = await api.SignInAsync(client, IdentityTestData.NewEmail());
        var second = await (await RefreshAsync(client, Refresh(first))).Content.ReadFromJsonAsync<JsonElement>(Ct);

        var since = UtcNowMicroseconds();
        (await LogoutAsync(client, Refresh(second))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await FamilyIsRevokedSinceAsync(await FamilyOfAsync(Refresh(second)), since, rows: 2);
        await Problem.ReadAsync(await RefreshAsync(client, Refresh(second)), 401, "UNAUTHENTICATED");
        (await LogoutAsync(client, "not-a-real-token")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // Every writer of a family takes its advisory lock first, so a revoke cannot miss a token another refresh is inserting.
    [Fact]
    public async Task Refresh_and_logout_wait_for_the_family_lock()
    {
        var client = api.ClientWithIp();
        var first = await api.SignInAsync(client, IdentityTestData.NewEmail());
        var familyId = await FamilyOfAsync(Refresh(first));

        JsonElement second;
        var (connection, transaction) = await HoldFamilyLockAsync(familyId);
        await using (connection)
        await using (transaction)
        {
            var refresh = RefreshAsync(client, Refresh(first));
            await Task.Delay(TimeSpan.FromMilliseconds(500), Ct);
            refresh.IsCompleted.Should().BeFalse("a refresh waits while another writer holds the family lock");
            await transaction.CommitAsync(Ct);
            var response = await refresh;
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            second = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        }

        (connection, transaction) = await HoldFamilyLockAsync(familyId);
        await using (connection)
        await using (transaction)
        {
            var logout = LogoutAsync(client, Refresh(second));
            await Task.Delay(TimeSpan.FromMilliseconds(500), Ct);
            logout.IsCompleted.Should().BeFalse("a logout waits while another writer holds the family lock");
            await transaction.CommitAsync(Ct);
            (await logout).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
    }
}
