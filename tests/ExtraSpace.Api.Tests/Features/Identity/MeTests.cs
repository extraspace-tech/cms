using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExtraSpace.Api.Features.Identity;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ExtraSpace.Api.Tests.Features.Identity;

public sealed class MeTests(ApiFactory api)
{
    private readonly HttpClient _client = api.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<HttpResponseMessage> GetMeAsync(string? token, params (string Name, string Value)[] headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        foreach (var (name, value) in headers) request.Headers.Add(name, value);
        return _client.SendAsync(request, Ct);
    }

    [Fact]
    public async Task A_signed_in_customer_gets_their_profile()
    {
        var customer = await api.CreateCustomerAsync();
        var response = await GetMeAsync(api.AccessTokenFor(customer));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("id").GetGuid().Should().Be(customer.Id);
        body.GetProperty("email").GetString().Should().Be(customer.Email);
        body.GetProperty("firstName").GetString().Should().Be("Jane");
        body.GetProperty("lastName").GetString().Should().Be("Tan");
        body.GetProperty("phone").GetString().Should().Be("+6590000000");
    }

    [Fact]
    public async Task No_token_is_401() =>
        await Problem.ReadAsync(await GetMeAsync(null), 401, "UNAUTHENTICATED");

    [Fact]
    public async Task A_token_signed_with_another_key_is_401() =>
        await Problem.ReadAsync(await GetMeAsync(IdentityTestData.ForgedToken(Guid.CreateVersion7(),
            key: Convert.ToBase64String(new byte[48]))), 401, "UNAUTHENTICATED");

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("HS384")]
    [InlineData("HS512")]
    public async Task A_token_with_a_wrong_issuer_audience_or_algorithm_is_401(string wrong)
    {
        var customer = await api.CreateCustomerAsync();
        var token = wrong switch
        {
            "issuer" => IdentityTestData.ForgedToken(customer.Id, issuer: "someone-else"),
            "audience" => IdentityTestData.ForgedToken(customer.Id, audience: "another-app"),
            // HS384 is the real ValidAlgorithms check: IdentityModel would accept it with our 49-byte key. HS512 needs 64 bytes, so it fails earlier.
            "HS384" => IdentityTestData.ForgedToken(customer.Id, algorithm: SecurityAlgorithms.HmacSha384),
            _ => IdentityTestData.ForgedToken(customer.Id, algorithm: SecurityAlgorithms.HmacSha512),
        };
        await Problem.ReadAsync(await GetMeAsync(token), 401, "UNAUTHENTICATED");
    }

    // Control for the theory above: the same hand-made token with nothing wrong is accepted.
    [Fact]
    public async Task A_forged_token_with_the_right_issuer_audience_and_algorithm_is_accepted()
    {
        var customer = await api.CreateCustomerAsync();
        (await GetMeAsync(IdentityTestData.ForgedToken(customer.Id))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_signing_key_shorter_than_32_bytes_stops_start_up()
    {
        await using var host = api.WithWebHostBuilder(b => b.UseSetting("App:Role", "Api")
            .UseSetting("CustomerAuth:SigningKey", Convert.ToBase64String(new byte[16])));
        var start = () => host.CreateClient();
        start.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public async Task An_expired_token_is_401()
    {
        var customer = await api.CreateCustomerAsync();
        await Problem.ReadAsync(await GetMeAsync(IdentityTestData.ForgedToken(customer.Id, expiresInMinutes: -5)), 401, "UNAUTHENTICATED");
    }

    [Fact]
    public async Task A_token_for_a_deleted_customer_is_401() =>
        await Problem.ReadAsync(await GetMeAsync(IdentityTestData.ForgedToken(Guid.CreateVersion7())), 401, "UNAUTHENTICATED");

    [Fact]
    public async Task An_outdated_app_gets_426_before_sign_in_is_checked() =>
        await Problem.ReadAsync(await GetMeAsync(null, ("X-App-Platform", "ios"), ("X-App-Version", "1.0.0")), 426, "APP_UPDATE_REQUIRED");

    [Fact]
    public async Task Signed_in_requests_log_the_customer_id()
    {
        var customer = await api.CreateCustomerAsync();
        var response = await GetMeAsync(api.AccessTokenFor(customer));
        var summary = api.Logs.LinesFor(Problem.TraceIdOf(response)).Single(l => l.GetProperty("messageTemplate").GetString() == "HTTP {method} {route} responded {status} in {elapsedMs} ms");
        summary.GetProperty("customerId").GetString().Should().Be(customer.Id.ToString());
    }

    [Fact]
    public async Task The_access_token_has_the_spec_claims_and_lifetime()
    {
        var customer = await api.CreateCustomerAsync();
        var token = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(api.AccessTokenFor(customer));
        token.Subject.Should().Be(customer.Id.ToString());
        token.GetClaim("email").Value.Should().Be(customer.Email);
        token.Issuer.Should().Be("extraspace");
        token.Audiences.Should().Equal("extraspace-app");
        token.Alg.Should().Be("HS256");
        (token.ValidTo - token.IssuedAt).Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task The_reported_expiry_equals_the_token_exp()
    {
        var customer = await api.CreateCustomerAsync();
        var (token, expiresAt) = api.Services.GetRequiredService<AccessTokens>().Issue(customer.Id, customer.Email);
        new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(token).ValidTo.Should().Be(expiresAt);
    }
}
