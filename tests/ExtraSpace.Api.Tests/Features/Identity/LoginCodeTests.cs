using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using ExtraSpace.Api.Features.Identity;
using ExtraSpace.Api.Platform.Logging;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace ExtraSpace.Api.Tests.Features.Identity;

public sealed class LoginCodeTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string? Str(JsonElement line, string name) =>
        line.TryGetProperty(name, out var value) ? value.ToString() : null;

    private Task SetCodeRowAsync(string email, Action<CustomerLoginCode> change) =>
        api.WithDbAsync(async db =>
        {
            var row = await db.Set<CustomerLoginCode>().SingleAsync(c => c.Email == email, Ct);
            change(row);
            await db.SaveChangesAsync(Ct);
        });

    [Fact]
    public async Task A_code_signs_the_customer_in_and_the_token_works()
    {
        var client = api.ClientWithIp();
        var email = IdentityTestData.NewEmail();

        var request = await client.RequestCodeAsync(email);
        request.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await request.Content.ReadAsStringAsync(Ct)).Should().BeEmpty();

        var code = await api.Emails.CodeForAsync(email);
        var verify = await client.VerifyAsync(email, code);
        verify.EnsureSuccessStatusCode();
        var tokens = JsonDocument.Parse(await verify.Content.ReadAsStringAsync(Ct)).RootElement;
        tokens.GetProperty("refreshToken").GetString().Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
        tokens.GetProperty("accessTokenExpiresAt").GetString().Should().EndWith("Z");

        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        var profile = await client.SendAsync(me, Ct);
        profile.EnsureSuccessStatusCode();

        await api.WithDbAsync(async db => (await db.Set<Customer>().CountAsync(c => c.Email == email, Ct)).Should().Be(1));
    }

    [Fact]
    public async Task A_second_sign_in_reuses_the_customer()
    {
        var email = IdentityTestData.NewEmail();
        await api.SignInAsync(api.ClientWithIp(), email);
        await SetCodeRowAsync(email, r => r.LastSentAt = r.LastSentAt.AddMinutes(-2));
        await api.SignInAsync(api.ClientWithIp(), email);
        await api.WithDbAsync(async db => (await db.Set<Customer>().CountAsync(c => c.Email == email, Ct)).Should().Be(1));
    }

    [Fact]
    public async Task Email_case_and_spaces_do_not_matter()
    {
        var email = IdentityTestData.NewEmail();
        var client = api.ClientWithIp();
        (await client.RequestCodeAsync("  " + email.ToUpperInvariant() + " ")).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var code = await api.Emails.CodeForAsync(email);
        (await client.VerifyAsync(email, code)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Known_and_unknown_emails_get_the_same_answer()
    {
        var known = (await api.CreateCustomerAsync()).Email;
        var unknown = IdentityTestData.NewEmail();
        var client = api.ClientWithIp();
        var a = await client.RequestCodeAsync(known);
        var b = await client.RequestCodeAsync(unknown);
        a.StatusCode.Should().Be(HttpStatusCode.Accepted);
        b.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await a.Content.ReadAsStringAsync(Ct)).Should().Be(await b.Content.ReadAsStringAsync(Ct));
        await api.Emails.WaitForAsync(known);
        await api.Emails.WaitForAsync(unknown);
    }

    [Fact]
    public async Task Five_wrong_codes_lock_the_code()
    {
        var email = IdentityTestData.NewEmail();
        var client = api.ClientWithIp();
        await client.RequestCodeAsync(email);
        var code = await api.Emails.CodeForAsync(email);
        var wrong = code == "000000" ? "111111" : "000000";

        for (var i = 1; i <= 4; i++) await Problem.ReadAsync(await client.VerifyAsync(email, wrong), 400, "INVALID_CODE");
        await Problem.ReadAsync(await client.VerifyAsync(email, wrong), 400, "TOO_MANY_ATTEMPTS");
        await Problem.ReadAsync(await client.VerifyAsync(email, code), 400, "TOO_MANY_ATTEMPTS");
    }

    [Fact]
    public async Task An_expired_code_is_invalid()
    {
        var email = IdentityTestData.NewEmail();
        var client = api.ClientWithIp();
        await client.RequestCodeAsync(email);
        var code = await api.Emails.CodeForAsync(email);
        await SetCodeRowAsync(email, r => r.ExpiresAt = DateTime.UtcNow.AddSeconds(-1));
        await Problem.ReadAsync(await client.VerifyAsync(email, code), 400, "INVALID_CODE");
    }

    [Fact]
    public async Task A_code_works_once()
    {
        var email = IdentityTestData.NewEmail();
        var client = api.ClientWithIp();
        await client.RequestCodeAsync(email);
        var code = await api.Emails.CodeForAsync(email);
        (await client.VerifyAsync(email, code)).EnsureSuccessStatusCode();
        await Problem.ReadAsync(await client.VerifyAsync(email, code), 400, "INVALID_CODE");
    }

    [Fact]
    public async Task An_unknown_email_is_invalid_not_404() =>
        await Problem.ReadAsync(await api.ClientWithIp().VerifyAsync(IdentityTestData.NewEmail(), "123456"), 400, "INVALID_CODE");

    [Fact]
    public async Task A_new_request_replaces_the_old_code()
    {
        var email = IdentityTestData.NewEmail();
        var client = api.ClientWithIp();
        await client.RequestCodeAsync(email);
        var first = await api.Emails.CodeForAsync(email);
        await SetCodeRowAsync(email, r => r.LastSentAt = r.LastSentAt.AddSeconds(-61));
        (await client.RequestCodeAsync(email)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var second = await api.Emails.CodeForAsync(email, 2);
        if (first != second) await Problem.ReadAsync(await client.VerifyAsync(email, first), 400, "INVALID_CODE");
        (await client.VerifyAsync(email, second)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_second_request_within_60_seconds_is_429_with_retry_after()
    {
        var email = IdentityTestData.NewEmail();
        var client = api.ClientWithIp();
        await client.RequestCodeAsync(email);
        var response = await client.RequestCodeAsync(email);
        await Problem.ReadAsync(response, 429, "RATE_LIMITED");
        response.Headers.RetryAfter!.Delta!.Value.TotalSeconds.Should().BeInRange(1, 60);
    }

    [Fact]
    public async Task The_sixth_request_in_an_hour_is_429()
    {
        var email = IdentityTestData.NewEmail();
        var client = api.ClientWithIp();
        await client.RequestCodeAsync(email);
        await SetCodeRowAsync(email, r =>
        {
            r.SentInWindow = 5;
            r.WindowStartedAt = DateTime.UtcNow.AddMinutes(-10);
            r.LastSentAt = DateTime.UtcNow.AddMinutes(-2);
        });
        var response = await client.RequestCodeAsync(email);
        await Problem.ReadAsync(response, 429, "RATE_LIMITED");
        response.Headers.RetryAfter!.Delta!.Value.TotalMinutes.Should().BeInRange(49, 51);
    }

    [Fact]
    public async Task Gap_and_hourly_cap_together_answer_with_the_longer_wait()
    {
        var email = IdentityTestData.NewEmail();
        var client = api.ClientWithIp();
        await client.RequestCodeAsync(email);
        await SetCodeRowAsync(email, r =>
        {
            r.SentInWindow = 5;
            r.WindowStartedAt = DateTime.UtcNow.AddMinutes(-10);
            r.LastSentAt = DateTime.UtcNow.AddSeconds(-10);
        });
        var response = await client.RequestCodeAsync(email);
        await Problem.ReadAsync(response, 429, "RATE_LIMITED");
        response.Headers.RetryAfter!.Delta!.Value.TotalSeconds.Should().BeGreaterThan(60);
    }

    [Fact]
    public async Task The_hourly_window_resets()
    {
        var email = IdentityTestData.NewEmail();
        var client = api.ClientWithIp();
        await client.RequestCodeAsync(email);
        await SetCodeRowAsync(email, r =>
        {
            r.SentInWindow = 5;
            r.WindowStartedAt = DateTime.UtcNow.AddMinutes(-61);
            r.LastSentAt = DateTime.UtcNow.AddMinutes(-2);
        });
        (await client.RequestCodeAsync(email)).StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task One_ip_may_request_20_codes_an_hour()
    {
        var client = api.ClientWithIp();
        for (var i = 0; i < 20; i++)
            (await client.RequestCodeAsync(IdentityTestData.NewEmail("ip"))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await Problem.ReadAsync(await client.RequestCodeAsync(IdentityTestData.NewEmail("ip")), 429, "RATE_LIMITED");
    }

    [Fact]
    public async Task One_ip_may_check_30_codes_an_hour()
    {
        var client = api.ClientWithIp();
        for (var i = 0; i < 30; i++)
            await Problem.ReadAsync(await client.VerifyAsync(IdentityTestData.NewEmail("guess"), "123456"), 400, "INVALID_CODE");
        await Problem.ReadAsync(await client.VerifyAsync(IdentityTestData.NewEmail("guess"), "123456"), 429, "RATE_LIMITED");
    }

    [Fact]
    public async Task Parallel_code_requests_for_one_email_send_one_code()
    {
        var email = IdentityTestData.NewEmail();
        var client = api.ClientWithIp();
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.RequestCodeAsync(email)));
        responses.Count(r => r.StatusCode == HttpStatusCode.Accepted).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests).Should().Be(4);
        await api.Emails.WaitForAsync(email);
        await Task.Delay(1000, Ct);
        api.Emails.SentTo(email).Should().HaveCount(1);
    }

    [Fact]
    public async Task A_new_request_unlocks_a_locked_code()
    {
        var email = IdentityTestData.NewEmail();
        var client = api.ClientWithIp();
        await client.RequestCodeAsync(email);
        var first = await api.Emails.CodeForAsync(email);
        var wrong = first == "000000" ? "111111" : "000000";
        for (var i = 1; i <= 4; i++) await Problem.ReadAsync(await client.VerifyAsync(email, wrong), 400, "INVALID_CODE");
        await Problem.ReadAsync(await client.VerifyAsync(email, wrong), 400, "TOO_MANY_ATTEMPTS");

        await SetCodeRowAsync(email, r => r.LastSentAt = r.LastSentAt.AddSeconds(-61));
        (await client.RequestCodeAsync(email)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var second = await api.Emails.CodeForAsync(email, 2);
        (await client.VerifyAsync(email, second)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Parallel_verifies_for_one_email_sign_in_once()
    {
        var email = IdentityTestData.NewEmail();
        var client = api.ClientWithIp();
        await client.RequestCodeAsync(email);
        var code = await api.Emails.CodeForAsync(email);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.VerifyAsync(email, code)));
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        foreach (var rejected in responses.Where(r => r.StatusCode != HttpStatusCode.OK))
            await Problem.ReadAsync(rejected, 400, "INVALID_CODE");
        await api.WithDbAsync(async db => (await db.Set<Customer>().CountAsync(c => c.Email == email, Ct)).Should().Be(1));
    }

    [Theory]
    [InlineData("a@b")]
    [InlineData("a b@c.d")]
    [InlineData("Jane <jane@example.test>")]
    [InlineData("jane@.example.test")]
    public async Task A_loose_email_is_a_validation_error(string email)
    {
        var client = api.ClientWithIp();
        var request = await Problem.ReadAsync(await client.RequestCodeAsync(email), 400, "VALIDATION_FAILED");
        request.GetProperty("errors").TryGetProperty("email", out _).Should().BeTrue();
        var verify = await Problem.ReadAsync(await client.VerifyAsync(email, "123456"), 400, "VALIDATION_FAILED");
        verify.GetProperty("errors").TryGetProperty("email", out _).Should().BeTrue();
    }

    [Fact]
    public async Task A_mixed_case_email_with_spaces_is_accepted()
    {
        var id = Guid.NewGuid().ToString("N");
        var typed = $" Jane-{id}@Example.TEST ";
        var client = api.ClientWithIp();
        (await client.RequestCodeAsync(typed)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var code = await api.Emails.CodeForAsync($"jane-{id}@example.test");
        (await client.VerifyAsync(typed, code)).EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData("not-an-email", "123456", "email")]
    [InlineData("a@example.test", "12ab56", "code")]
    [InlineData("a@example.test", "1234567", "code")]
    public async Task Bad_input_is_a_validation_error(string email, string code, string field)
    {
        var body = await Problem.ReadAsync(await api.ClientWithIp().VerifyAsync(email, code), 400, "VALIDATION_FAILED");
        body.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }

    [Fact]
    public async Task The_request_logs_a_masked_email_and_never_the_code()
    {
        var email = IdentityTestData.NewEmail();
        var response = await api.ClientWithIp().RequestCodeAsync(email);
        var traceId = Problem.TraceIdOf(response);
        var code = await api.Emails.CodeForAsync(email);

        var requested = api.Logs.LinesFor(traceId).Single(l => Str(l, "messageTemplate") == "Login code {codeId} requested for {email}");
        Str(requested, "email").Should().Be(Pii.MaskEmail(email));
        api.Logs.RawLinesFor(traceId).Should().NotContain(l => Regex.IsMatch(l, $"(?<![0-9]){code}(?![0-9])") || l.Contains(email));
    }
}
