using System.Net;
using System.Net.Http.Json;
using System.Text;
using ExtraSpace.Api.Tests.TestSupport;

namespace ExtraSpace.Api.Tests.Platform;

public sealed class ErrorTests(ApiFactory api)
{
    private readonly HttpClient _client = api.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task App_exception_keeps_its_status_code_and_title()
    {
        var body = await Problem.ReadAsync(await _client.GetAsync("/test/app-error", Ct), 400, "INVALID_CODE");
        body.GetProperty("title").GetString().Should().Be("The code is wrong or has expired");
    }

    [Fact]
    public async Task Unknown_exception_is_500_without_details()
    {
        var response = await _client.GetAsync("/test/boom", Ct);
        await Problem.ReadAsync(response, 500, "INTERNAL_ERROR");
        var raw = await response.Content.ReadAsStringAsync(Ct);
        raw.Should().NotContain("secret-detail-12345").And.NotContain("InvalidOperationException").And.NotContain("at ExtraSpace");
    }

    [Fact]
    public async Task Validation_errors_are_400_with_camel_case_fields()
    {
        var response = await _client.PostAsJsonAsync("/test/validate", new { email = "not-an-email", count = 99 }, Ct);
        var body = await Problem.ReadAsync(response, 400, "VALIDATION_FAILED");
        var errors = body.GetProperty("errors");
        errors.TryGetProperty("email", out _).Should().BeTrue();
        errors.TryGetProperty("count", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Malformed_json_is_a_validation_error()
    {
        using var content = new StringContent("{ \"email\": ", Encoding.UTF8, "application/json");
        await Problem.ReadAsync(await _client.PostAsync("/test/validate", content, Ct), 400, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Validation_error_is_json_for_a_non_json_client()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/test/validate") { Content = JsonContent.Create(new { email = "not-an-email", count = 99 }) };
        request.Headers.Accept.ParseAdd("text/html");
        var errors = (await Problem.ReadAsync(await _client.SendAsync(request, Ct), 400, "VALIDATION_FAILED")).GetProperty("errors");
        errors.TryGetProperty("email", out _).Should().BeTrue();
        errors.TryGetProperty("count", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Unknown_route_is_json_for_a_non_json_client()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/no/such/route");
        request.Headers.Accept.ParseAdd("text/html");
        var body = await Problem.ReadAsync(await _client.SendAsync(request, Ct), 404, "NOT_FOUND");
        body.GetProperty("title").GetString().Should().Be("Not Found");
    }

    [Fact]
    public async Task Undefined_enum_in_the_query_is_a_validation_error()
    {
        var body = await Problem.ReadAsync(await _client.GetAsync("/test/enum-filter?filter=5", Ct), 400, "VALIDATION_FAILED");
        body.GetProperty("errors").TryGetProperty("filter", out _).Should().BeTrue();
        (await _client.GetAsync("/test/enum-filter?filter=Archived", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Enum_number_in_the_body_is_a_validation_error()
    {
        using var number = new StringContent("{\"status\":5}", Encoding.UTF8, "application/json");
        await Problem.ReadAsync(await _client.PostAsync("/test/enum", number, Ct), 400, "VALIDATION_FAILED");
        using var name = new StringContent("{\"status\":\"Archived\"}", Encoding.UTF8, "application/json");
        (await _client.PostAsync("/test/enum", name, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unknown_route_is_404_problem() =>
        await Problem.ReadAsync(await _client.GetAsync("/no/such/route", Ct), 404, "NOT_FOUND");

    [Fact]
    public async Task Wrong_method_is_405_with_a_fallback_code() =>
        await Problem.ReadAsync(await _client.DeleteAsync("/test/ok", Ct), 405, "HTTP_405");

    [Theory]
    [InlineData(401, "UNAUTHENTICATED")]
    [InlineData(403, "FORBIDDEN")]
    [InlineData(418, "HTTP_418")]
    public async Task Empty_error_responses_become_problems(int status, string code) =>
        await Problem.ReadAsync(await _client.GetAsync($"/test/status/{status}", Ct), status, code);

    [Fact]
    public async Task Concurrency_failure_is_409_conflict() =>
        await Problem.ReadAsync(await _client.GetAsync("/test/concurrency", Ct), 409, "CONFLICT");

    [Fact]
    public async Task Unique_violation_is_409_conflict() =>
        await Problem.ReadAsync(await _client.GetAsync("/test/unique", Ct), 409, "CONFLICT");
}
