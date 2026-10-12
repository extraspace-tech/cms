using System.Text.Json;
using ExtraSpace.Api.Platform.Data;
using ExtraSpace.Api.Platform.Logging;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;

namespace ExtraSpace.Api.Tests.Platform;

public sealed class LoggingTests(ApiFactory api)
{
    private readonly HttpClient _client = api.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string? Str(JsonElement line, string name) =>
        line.TryGetProperty(name, out var value) ? value.ToString() : null;

    [Fact]
    public async Task Error_response_trace_id_matches_its_log_lines()
    {
        var response = await _client.GetAsync("/test/boom", Ct);
        var traceId = (await Problem.ReadAsync(response, 500, "INTERNAL_ERROR")).GetProperty("traceId").GetString()!;

        var lines = api.Logs.LinesFor(traceId);
        lines.Should().NotBeEmpty();
        lines.Should().AllSatisfy(line =>
        {
            Str(line, "correlationId").Should().Be(traceId);
            Str(line, "app").Should().Be("ExtraSpace");
            Str(line, "env").Should().Be("Testing");
            Str(line, "role").Should().Be("all");
            Str(line, "version").Should().Be("dev");
            Str(line, "method").Should().Be("GET");
            line.TryGetProperty("spanId", out _).Should().BeTrue();
        });

        var withException = lines.Where(l => l.TryGetProperty("exception", out _)).ToList();
        withException.Should().ContainSingle("an exception is logged exactly once");
        Str(withException[0], "level").Should().Be("Error");
        Str(withException[0], "exception").Should().Contain("secret-detail-12345");
        Str(withException[0], "route").Should().Be("test/boom");

        var summary = lines.Single(l => Str(l, "messageTemplate")!.StartsWith("HTTP "));
        Str(summary, "level").Should().Be("Error");
        Str(summary, "route").Should().Be("test/boom");
        Str(summary, "status").Should().Be("500");
        summary.TryGetProperty("elapsedMs", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Lines_are_json_with_plain_field_names()
    {
        var traceId = Problem.TraceIdOf(await _client.GetAsync("/test/ok", Ct));
        var raw = api.Logs.RawLinesFor(traceId).Should().ContainSingle().Subject;
        raw.Should().EndWith("\n");
        var line = JsonDocument.Parse(raw).RootElement;
        foreach (var field in new[] { "timestamp", "level", "message", "messageTemplate", "traceId", "spanId" })
            line.TryGetProperty(field, out _).Should().BeTrue(field);
        line.EnumerateObject().Should().NotContain(p => p.Name.StartsWith('@'));
        foreach (var field in new[] { "RequestPath", "RequestId", "ActionId", "ActionName" })
            line.TryGetProperty(field, out _).Should().BeFalse(field);
        Str(line, "messageTemplate").Should().Be("HTTP {method} {route} responded {status} in {elapsedMs} ms");
        Str(line, "message").Should().Contain("test/ok").And.Contain("200");
    }

    [Theory]
    [InlineData("/test/ok", "Information")]
    [InlineData("/test/status/403", "Warning")]
    [InlineData("/test/status/429", "Warning")]
    [InlineData("/test/status/404", "Information")]
    [InlineData("/test/boom", "Error")]
    public async Task Summary_level_follows_the_status(string path, string level)
    {
        var traceId = Problem.TraceIdOf(await _client.GetAsync(path, Ct));
        var summary = api.Logs.LinesFor(traceId).Single(l => Str(l, "messageTemplate")!.StartsWith("HTTP "));
        Str(summary, "level").Should().Be(level);
    }

    [Fact]
    public async Task Health_checks_are_not_logged_when_healthy()
    {
        var traceId = Problem.TraceIdOf(await _client.GetAsync("/health/live", Ct));
        api.Logs.LinesFor(traceId).Should().BeEmpty();
    }

    [Fact]
    public async Task Secret_named_properties_are_masked_everywhere()
    {
        var traceId = Problem.TraceIdOf(await _client.GetAsync("/test/log-secrets", Ct));
        var raw = api.Logs.RawLinesFor(traceId).Single(l => l.Contains("Sign-in check", StringComparison.Ordinal));
        raw.Should().NotContain("SENTINEL").And.NotContain("654321").And.NotContain("jane@");
        raw.Should().Contain("visible-value").And.Contain("pixel").And.Contain("j***@example.com").And.Contain("application/json");
        var line = JsonDocument.Parse(raw).RootElement;
        foreach (var name in new[] { "password", "refreshToken", "loginCode", "authorization" })
            Str(line, name).Should().Be("***", name);
        line.GetProperty("session").GetProperty("SessionToken").GetString().Should().Be("***");
        line.GetProperty("headers").GetProperty("Authorization").GetString().Should().Be("***");
    }

    [Fact]
    public async Task Request_properties_reach_later_lines_and_the_summary()
    {
        var traceId = Problem.TraceIdOf(await _client.GetAsync("/test/customer-line", Ct));
        var lines = api.Logs.LinesFor(traceId);
        Str(lines.Single(l => Str(l, "messageTemplate") == "Customer did a thing"), "customerId").Should().Be("c-123");
        Str(lines.Single(l => Str(l, "messageTemplate")!.StartsWith("HTTP ")), "customerId").Should().Be("c-123");
        Str(lines.Single(l => Str(l, "messageTemplate") == "Customer did a thing"), "route").Should().Be("test/customer-line");
    }

    [Fact]
    public async Task Unique_violation_conflict_logs_no_error_line()
    {
        var response = await _client.GetAsync("/test/unique", Ct);
        var traceId = (await Problem.ReadAsync(response, 409, "CONFLICT")).GetProperty("traceId").GetString()!;
        var lines = api.Logs.LinesFor(traceId);
        lines.Should().NotContain(l => Str(l, "level") == "Error" || Str(l, "level") == "Fatal");
        Str(lines.Single(l => Str(l, "messageTemplate")!.StartsWith("HTTP ")), "level").Should().Be("Information");
    }

    [Fact]
    public async Task Unique_violation_on_save_changes_logs_no_error_line()
    {
        var response = await _client.GetAsync("/test/unique-save", Ct);
        var traceId = (await Problem.ReadAsync(response, 409, "CONFLICT")).GetProperty("traceId").GetString()!;
        var lines = api.Logs.LinesFor(traceId);
        lines.Should().NotContain(l => Str(l, "level") == "Error" || Str(l, "level") == "Fatal");
        Str(lines.Single(l => Str(l, "messageTemplate")!.StartsWith("HTTP ")), "level").Should().Be("Information");

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.DataProtectionKeys.AnyAsync(k => k.Id == 2147483000, Ct)).Should().BeFalse("the test row is rolled back");
    }

    [Fact]
    public void Feature_comes_from_the_namespace()
    {
        static LogEvent Event(string source) => new(DateTimeOffset.UnixEpoch, LogEventLevel.Information, null,
            Serilog.Events.MessageTemplate.Empty, [new LogEventProperty("SourceContext", new ScalarValue(source))]);

        var feature = Event("ExtraSpace.Api.Features.Identity.LoginService");
        new FeatureEnricher().Enrich(feature, null!);
        feature.Properties["feature"].ToString().Should().Be("\"Identity\"");

        var platform = Event("ExtraSpace.Api.Platform.Errors.AppExceptionHandler");
        new FeatureEnricher().Enrich(platform, null!);
        platform.Properties.Should().NotContainKey("feature");
    }
}
