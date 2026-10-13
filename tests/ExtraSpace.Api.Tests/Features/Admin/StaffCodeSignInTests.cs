using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ExtraSpace.Api.Features.Admin;
using ExtraSpace.Api.Features.Identity;
using ExtraSpace.Api.Features.Identity.Contracts;
using ExtraSpace.Api.Platform.Logging;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace ExtraSpace.Api.Tests.Features.Admin;

/// <summary>Staff sign in with an emailed code (S0-4b). The code is read from the fake email transport.</summary>
[Collection("Owners")]
public sealed class StaffCodeSignInTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<StaffUser> NewStaffAsync(bool active = true, string? email = null) =>
        await api.CreateStaffAsync((await api.CreateRoleAsync()).Id, active, email);

    private async Task<StaffUser?> FindAsync(string email)
    {
        StaffUser? staff = null;
        await api.WithDbAsync(async db => staff = await db.Set<StaffUser>().SingleOrDefaultAsync(s => s.Email == email));
        return staff;
    }

    private async Task<bool> HasStaffCodeAsync(string email)
    {
        var exists = false;
        await api.WithDbAsync(async db => exists = await db.Set<LoginCode>().AnyAsync(c => c.Purpose == LoginCodePurpose.Staff && c.Email == email));
        return exists;
    }

    private Task SetActiveAsync(Guid staffId, bool active) =>
        api.WithDbAsync(async db =>
        {
            (await db.Set<StaffUser>().SingleAsync(s => s.Id == staffId)).Active = active;
            await db.SaveChangesAsync();
        });

    /// <summary>Asks for a code and reads it from the email; fails when no email arrives.</summary>
    private async Task<string> RequestCodeAsync(HttpClient client, string email)
    {
        var sentBefore = api.Emails.SentTo(email).Count;
        (await client.RequestStaffCodeAsync(email)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        return await api.Emails.CodeForAsync(email, sentBefore + 1);
    }

    /// <summary>202 with an empty body, no stored code and no email: the same answer a staff address gets, with nothing behind it.</summary>
    private async Task ShouldGetNoCodeAsync(HttpClient client, string email)
    {
        var response = await client.RequestStaffCodeAsync(email);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await response.Content.ReadAsStringAsync(Ct)).Should().BeEmpty();
        (await HasStaffCodeAsync(email)).Should().BeFalse();
        api.Emails.SentTo(email).Should().BeEmpty();
        api.Logs.LinesFor(Problem.TraceIdOf(response)).Select(l => l.GetProperty("messageTemplate").GetString())
            .Should().NotContain("Login code {codeId} requested for {email}");
    }

    private static string WrongCode(string code) => code == "000000" ? "111111" : "000000";

    private static string CookieValue(HttpResponseMessage response, string name) =>
        response.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]).Single(c => c.StartsWith(name + "=", StringComparison.Ordinal))[(name.Length + 1)..];

    private WebApplicationFactory<Program> HostWith(params (string Key, string Value)[] settings) =>
        api.WithWebHostBuilder(b =>
        {
            b.UseSetting("App:Role", "Api");
            foreach (var (key, value) in settings) b.UseSetting(key, value);
        });

    [Fact]
    public async Task A_staff_member_signs_in_with_an_emailed_code()
    {
        var staff = await NewStaffAsync();
        var client = api.AdminClientWithIp();

        var request = await client.RequestStaffCodeAsync(staff.Email.ToUpperInvariant());
        request.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await request.Content.ReadAsStringAsync(Ct)).Should().BeEmpty();
        await Problem.ReadAsync(await client.GetAsync("/admin/api/me", Ct), 401, "UNAUTHENTICATED");

        var code = await api.Emails.CodeForAsync(staff.Email);
        var verify = await client.VerifyStaffCodeAsync($" {staff.Email.ToUpperInvariant()} ", code);

        verify.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var cookie = verify.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-es-admin=", StringComparison.Ordinal));
        var attributes = cookie.Split(';').Skip(1).Select(a => a.Trim().ToLowerInvariant()).ToList();
        attributes.Should().Contain(["path=/", "secure", "httponly", "samesite=lax"]);
        attributes.Should().NotContain(a => a.StartsWith("domain", StringComparison.Ordinal));

        var me = await client.GetFromJsonAsync<JsonElement>("/admin/api/me", Ct);
        me.GetProperty("id").GetGuid().Should().Be(staff.Id);
        me.GetProperty("email").GetString().Should().Be(staff.Email);
        (await FindAsync(staff.Email))!.LastSignInAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        var summary = api.Logs.LinesFor(Problem.TraceIdOf(verify))
            .Single(l => l.GetProperty("messageTemplate").GetString() == "HTTP {method} {route} responded {status} in {elapsedMs} ms");
        summary.GetProperty("staffId").GetString().Should().Be(staff.Id.ToString());

        // A code works once.
        await Problem.ReadAsync(await api.AdminClientWithIp().VerifyStaffCodeAsync(staff.Email, code), 400, "INVALID_CODE");
    }

    [Fact]
    public async Task An_unknown_email_gets_202_and_no_email()
    {
        var email = IdentityTestData.NewEmail("nobody");
        var client = api.AdminClientWithIp();

        await ShouldGetNoCodeAsync(client, email);

        await Problem.ReadAsync(await client.VerifyStaffCodeAsync(email, "123456"), 400, "INVALID_CODE");
        (await FindAsync(email)).Should().BeNull();
    }

    [Fact]
    public async Task An_inactive_staff_member_gets_202_and_no_email()
    {
        var staff = await NewStaffAsync(active: false);
        await ShouldGetNoCodeAsync(api.AdminClientWithIp(), staff.Email);
    }

    [Fact]
    public async Task A_customer_code_does_not_sign_in_staff()
    {
        var staff = await NewStaffAsync();
        (await api.ClientWithIp().RequestCodeAsync(staff.Email)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var customerCode = await api.Emails.CodeForAsync(staff.Email);
        var client = api.AdminClientWithIp();

        await Problem.ReadAsync(await client.VerifyStaffCodeAsync(staff.Email, customerCode), 400, "INVALID_CODE");

        await Problem.ReadAsync(await client.GetAsync("/admin/api/me", Ct), 401, "UNAUTHENTICATED");
        (await FindAsync(staff.Email))!.LastSignInAt.Should().BeNull();
        // The customer code is untouched: it still signs the customer in.
        (await api.ClientWithIp().VerifyAsync(staff.Email, customerCode)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_customer_code_hash_planted_in_the_staff_row_does_not_sign_in_staff()
    {
        var staff = await NewStaffAsync();
        var client = api.AdminClientWithIp();
        await RequestCodeAsync(client, staff.Email);
        (await api.ClientWithIp().RequestCodeAsync(staff.Email)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var customerCode = await api.Emails.CodeForAsync(staff.Email, 2);

        // The purpose is part of the hash input: the same address and code under the other purpose is another hash.
        await api.WithDbAsync(async db =>
        {
            var rows = await db.Set<LoginCode>().Where(c => c.Email == staff.Email).ToListAsync(Ct);
            var customerRow = rows.Single(c => c.Purpose == LoginCodePurpose.Customer);
            var staffRow = rows.Single(c => c.Purpose == LoginCodePurpose.Staff);
            staffRow.CodeHash = customerRow.CodeHash;
            staffRow.ExpiresAt = customerRow.ExpiresAt;
            await db.SaveChangesAsync(Ct);
        });

        var refused = await client.VerifyStaffCodeAsync(staff.Email, customerCode);

        await Problem.ReadAsync(refused, 400, "INVALID_CODE");
        refused.Headers.Contains("Set-Cookie").Should().BeFalse();
        await Problem.ReadAsync(await client.GetAsync("/admin/api/me", Ct), 401, "UNAUTHENTICATED");
        (await FindAsync(staff.Email))!.LastSignInAt.Should().BeNull();
    }

    [Fact]
    public async Task A_wrong_code_five_times_locks_the_code()
    {
        var staff = await NewStaffAsync();
        var client = api.AdminClientWithIp();
        var code = await RequestCodeAsync(client, staff.Email);

        for (var attempt = 1; attempt <= 4; attempt++)
            await Problem.ReadAsync(await client.VerifyStaffCodeAsync(staff.Email, WrongCode(code)), 400, "INVALID_CODE");
        await Problem.ReadAsync(await client.VerifyStaffCodeAsync(staff.Email, WrongCode(code)), 400, "TOO_MANY_ATTEMPTS");

        await Problem.ReadAsync(await client.VerifyStaffCodeAsync(staff.Email, code), 400, "TOO_MANY_ATTEMPTS");
        await Problem.ReadAsync(await client.GetAsync("/admin/api/me", Ct), 401, "UNAUTHENTICATED");
    }

    [Fact]
    public async Task A_staff_member_deactivated_after_the_code_was_sent_is_refused()
    {
        var staff = await NewStaffAsync();
        var client = api.AdminClientWithIp();
        var code = await RequestCodeAsync(client, staff.Email);
        await SetActiveAsync(staff.Id, false);

        var refused = await client.VerifyStaffCodeAsync(staff.Email, code);

        await Problem.ReadAsync(refused, 403, "FORBIDDEN");
        refused.Headers.Contains("Set-Cookie").Should().BeFalse();
        await Problem.ReadAsync(await client.GetAsync("/admin/api/me", Ct), 401, "UNAUTHENTICATED");
        (await FindAsync(staff.Email))!.LastSignInAt.Should().BeNull();

        // The refusal did not use the code up.
        await SetActiveAsync(staff.Id, true);
        (await client.VerifyStaffCodeAsync(staff.Email, code)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync("/admin/api/me", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_bootstrap_address_becomes_owner_at_first_sign_in()
    {
        await api.ClearOwnersAsync();
        var email = IdentityTestData.NewEmail("boot");
        await using var host = HostWith(("Admin:BootstrapEmails:0", email.ToUpperInvariant()));
        var client = host.AdminClientWithIp();

        var code = await RequestCodeAsync(client, email);
        (await FindAsync(email)).Should().BeNull("asking for a code creates nobody");
        var verify = await client.VerifyStaffCodeAsync(email, code);

        verify.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var staff = await FindAsync(email);
        staff!.RoleId.Should().Be(Role.OwnerId);
        staff.Active.Should().BeTrue();
        var me = await client.GetFromJsonAsync<JsonElement>("/admin/api/me", Ct);
        me.GetProperty("id").GetGuid().Should().Be(staff.Id);
        me.GetProperty("roleId").GetGuid().Should().Be(Role.OwnerId);
        var created = api.Logs.LinesFor(Problem.TraceIdOf(verify)).Should()
            .ContainSingle(l => l.GetProperty("messageTemplate").GetString() == "Bootstrap Owner {staffId} created").Subject;
        created.GetProperty("level").GetString().Should().Be("Warning");
        created.GetProperty("staffId").GetString().Should().Be(staff.Id.ToString());
        (await api.AuditRowsForAsync(staff.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Bootstrap_gets_no_code_once_an_owner_exists()
    {
        await api.ClearOwnersAsync();
        await api.CreateStaffAsync(Role.OwnerId);
        var email = IdentityTestData.NewEmail("boot");
        await using var host = HostWith(("Admin:BootstrapEmails:0", email));

        await ShouldGetNoCodeAsync(host.AdminClientWithIp(), email);

        (await FindAsync(email)).Should().BeNull();
    }

    [Fact]
    public async Task A_bootstrap_code_is_refused_when_an_owner_appeared_meanwhile()
    {
        await api.ClearOwnersAsync();
        var email = IdentityTestData.NewEmail("boot");
        await using var host = HostWith(("Admin:BootstrapEmails:0", email));
        var client = host.AdminClientWithIp();
        var code = await RequestCodeAsync(client, email);
        await api.CreateStaffAsync(Role.OwnerId);

        await Problem.ReadAsync(await client.VerifyStaffCodeAsync(email, code), 403, "FORBIDDEN");

        (await FindAsync(email)).Should().BeNull();
    }

    [Fact]
    public async Task A_disallowed_domain_gets_no_code()
    {
        await api.ClearOwnersAsync();
        var outside = await NewStaffAsync(email: $"s-{Guid.NewGuid():N}@other.test");
        var bootstrap = $"boot-{Guid.NewGuid():N}@other.test";
        var inside = await NewStaffAsync();
        await using var host = HostWith(("Admin:AllowedEmailDomains:0", "example.test"), ("Admin:BootstrapEmails:0", bootstrap));
        var client = host.AdminClientWithIp();

        await ShouldGetNoCodeAsync(client, outside.Email);
        await ShouldGetNoCodeAsync(client, bootstrap);
        (await FindAsync(bootstrap)).Should().BeNull();

        var code = await RequestCodeAsync(client, inside.Email);
        (await client.VerifyStaffCodeAsync(inside.Email, code)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Sign_in_writes_no_audit_row()
    {
        var first = await NewStaffAsync();
        var second = await NewStaffAsync();
        var client = api.AdminClientWithIp();
        (await client.VerifyStaffCodeAsync(first.Email, await RequestCodeAsync(client, first.Email))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Signed in as the first, no CSRF header: the anonymous routes skip the CSRF check and the audit context.
        var code = await RequestCodeAsync(client, second.Email);
        (await client.VerifyStaffCodeAsync(second.Email, code)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.GetFromJsonAsync<JsonElement>("/admin/api/me", Ct)).GetProperty("id").GetGuid().Should().Be(second.Id);
        (await api.AuditRowsForAsync(first.Id)).Should().BeEmpty();
        (await api.AuditRowsForAsync(second.Id)).Should().BeEmpty();
        var byFirst = 0;
        await api.WithDbAsync(async db => byFirst = await db.Set<AuditLog>().CountAsync(a => a.StaffUserId == first.Id));
        byFirst.Should().Be(0);
    }

    [Fact]
    public async Task The_sign_in_flow_logs_no_code_cookie_or_raw_email()
    {
        var staff = await NewStaffAsync(email: IdentityTestData.NewEmail("stafflog"));
        var client = api.AdminClientWithIp();
        var traceIds = new List<string>();

        var request = await client.RequestStaffCodeAsync(staff.Email);
        traceIds.Add(Problem.TraceIdOf(request));
        var code = await api.Emails.CodeForAsync(staff.Email);
        await api.Logs.WaitForAsync(l => l.TryGetProperty("correlationId", out var c) && c.GetString() == traceIds[0]
            && l.GetProperty("messageTemplate").GetString() == "Email {template} sent to {email}");

        var wrong = await client.VerifyStaffCodeAsync(staff.Email, WrongCode(code));
        traceIds.Add(Problem.TraceIdOf(wrong));
        var verify = await client.VerifyStaffCodeAsync(staff.Email, code);
        verify.StatusCode.Should().Be(HttpStatusCode.NoContent);
        traceIds.Add(Problem.TraceIdOf(verify));
        traceIds.Add(Problem.TraceIdOf(await client.GetAsync("/admin/api/me", Ct)));
        var csrf = await client.GetAsync("/admin/api/auth/csrf", Ct);
        traceIds.Add(Problem.TraceIdOf(csrf));
        var csrfToken = (await csrf.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Add(StaffAuth.CsrfHeader, csrfToken);
        var logout = await client.PostAsync("/admin/api/auth/logout", null, Ct);
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        traceIds.Add(Problem.TraceIdOf(logout));

        var lines = traceIds.SelectMany(api.Logs.RawLinesFor).ToList();   // includes the email job: its correlationId is the request's traceId
        lines.Should().HaveCountGreaterThan(8);
        string[] secrets = [staff.Email, CookieValue(verify, StaffAuth.CookieName), CookieValue(csrf, StaffAuth.CsrfCookieName), csrfToken];
        foreach (var secret in secrets) lines.Should().NotContain(l => l.Contains(secret));
        // Digit boundaries: six digits can occur by chance inside a timestamp fraction or an id.
        lines.Should().NotContain(l => Regex.IsMatch(l, $"(?<![0-9]){code}(?![0-9])"));
        lines.Should().Contain(l => l.Contains(Pii.MaskEmail(staff.Email)));
        lines.Should().Contain(l => l.Contains("\"purpose\":\"Staff\""));
        lines.Should().Contain(l => l.Contains("Staff {staffId} signed in") && l.Contains(staff.Id.ToString()));
    }

    [Fact]
    public async Task A_malformed_body_gets_400()
    {
        var client = api.AdminClientWithIp();

        var noEmail = await Problem.ReadAsync(await client.RequestStaffCodeAsync("not-an-email"), 400, "VALIDATION_FAILED");
        noEmail.GetProperty("errors").TryGetProperty("email", out _).Should().BeTrue();
        var shortCode = await Problem.ReadAsync(await client.VerifyStaffCodeAsync(IdentityTestData.NewEmail("s"), "12345"), 400, "VALIDATION_FAILED");
        shortCode.GetProperty("errors").TryGetProperty("code", out _).Should().BeTrue();
    }

    [Fact]
    public async Task The_code_routes_are_limited_per_ip()
    {
        var email = IdentityTestData.NewEmail("nobody");
        var client = api.AdminClientWithIp();

        for (var i = 0; i < 20; i++) (await client.RequestStaffCodeAsync(email)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await Problem.ReadAsync(await client.RequestStaffCodeAsync(email), 429, "RATE_LIMITED");

        for (var i = 0; i < 30; i++) await Problem.ReadAsync(await client.VerifyStaffCodeAsync(email, "123456"), 400, "INVALID_CODE");
        await Problem.ReadAsync(await client.VerifyStaffCodeAsync(email, "123456"), 429, "RATE_LIMITED");

        // Another address is not affected.
        (await api.AdminClientWithIp().RequestStaffCodeAsync(email)).StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task A_second_code_within_a_minute_is_rate_limited_for_a_staff_address()
    {
        var staff = await NewStaffAsync();
        var client = api.AdminClientWithIp();
        await RequestCodeAsync(client, staff.Email);

        var again = await client.RequestStaffCodeAsync(staff.Email);

        await Problem.ReadAsync(again, 429, "RATE_LIMITED");
        again.Headers.Contains("Retry-After").Should().BeTrue();
    }
}
