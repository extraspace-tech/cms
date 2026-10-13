using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ExtraSpace.Api.Platform.Logging;
using ExtraSpace.Api.Tests.TestSupport;

namespace ExtraSpace.Api.Tests.Features.Identity;

public sealed class SignInLoggingTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Spec §10: "a full sign-in flow with captured log output contains no login code, token or unmasked email".
    [Fact]
    public async Task A_full_sign_in_flow_logs_no_code_token_or_raw_email()
    {
        var client = api.ClientWithIp();
        var email = IdentityTestData.NewEmail("flow");
        var traceIds = new List<string>();

        var request = await client.RequestCodeAsync(email);
        traceIds.Add(Problem.TraceIdOf(request));
        var code = await api.Emails.CodeForAsync(email);
        await api.Logs.WaitForAsync(l => l.TryGetProperty("correlationId", out var c) && c.GetString() == traceIds[0]
            && l.GetProperty("messageTemplate").GetString() == "Email {template} sent to {email}");

        var verify = await client.VerifyAsync(email, code);
        traceIds.Add(Problem.TraceIdOf(verify));
        var tokens = await verify.Content.ReadFromJsonAsync<JsonElement>(Ct);

        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        traceIds.Add(Problem.TraceIdOf(await client.SendAsync(me, Ct)));

        var refresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.GetProperty("refreshToken").GetString() }, Ct);
        traceIds.Add(Problem.TraceIdOf(refresh));
        var rotated = await refresh.Content.ReadFromJsonAsync<JsonElement>(Ct);

        var logout = await client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = rotated.GetProperty("refreshToken").GetString() }, Ct);
        traceIds.Add(Problem.TraceIdOf(logout));

        var lines = traceIds.SelectMany(api.Logs.RawLinesFor).ToList();   // includes the email job: its correlationId is the request's traceId
        lines.Should().HaveCountGreaterThan(8);
        string[] secrets =
        [
            email,
            tokens.GetProperty("accessToken").GetString()!, tokens.GetProperty("refreshToken").GetString()!,
            rotated.GetProperty("accessToken").GetString()!, rotated.GetProperty("refreshToken").GetString()!,
        ];
        foreach (var secret in secrets) lines.Should().NotContain(l => l.Contains(secret));
        // Digit boundaries: six digits can occur by chance inside a timestamp fraction or an id.
        lines.Should().NotContain(l => Regex.IsMatch(l, $"(?<![0-9]){code}(?![0-9])"));
        lines.Should().Contain(l => l.Contains(Pii.MaskEmail(email)));
    }
}
