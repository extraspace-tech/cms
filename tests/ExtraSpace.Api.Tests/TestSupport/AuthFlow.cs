using System.Net.Http.Json;
using System.Text.Json;

namespace ExtraSpace.Api.Tests.TestSupport;

public static class AuthFlow
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static Task<HttpResponseMessage> RequestCodeAsync(this HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/v1/auth/code", new { email }, Ct);

    public static Task<HttpResponseMessage> VerifyAsync(this HttpClient client, string email, string code) =>
        client.PostAsJsonAsync("/api/v1/auth/code/verify", new { email, code }, Ct);

    /// <summary>Request a code, read it from the fake email, verify it; returns the token response body.</summary>
    public static async Task<JsonElement> SignInAsync(this ApiFactory api, HttpClient client, string email)
    {
        var sentBefore = api.Emails.SentTo(email.Trim().ToLowerInvariant()).Count;
        (await client.RequestCodeAsync(email)).StatusCode.Should().Be(System.Net.HttpStatusCode.Accepted);
        var code = await api.Emails.CodeForAsync(email.Trim().ToLowerInvariant(), sentBefore + 1);
        var response = await client.VerifyAsync(email, code);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }
}
