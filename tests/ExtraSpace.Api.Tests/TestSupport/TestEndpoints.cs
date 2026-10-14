using System.ComponentModel.DataAnnotations;
using ExtraSpace.Api.Features.Admin;
using ExtraSpace.Api.Integrations.Email;
using ExtraSpace.Api.Platform.Data;
using ExtraSpace.Api.Platform.Errors;
using ExtraSpace.Api.Platform.Hosting;
using ExtraSpace.Api.Platform.Http;
using ExtraSpace.Api.Platform.Logging;
using Hangfire;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExtraSpace.Api.Tests.TestSupport;

/// <summary>Test-only endpoints, added to the app by <see cref="ApiFactory"/>. Never part of the shipped API.</summary>
internal sealed class TestEndpoints : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        var test = endpoints.MapGroup("/test").WithEnumGuard();
        test.MapGet("/ok", () => TypedResults.Ok(new { ok = true }));
        test.MapGet("/ip", (HttpContext http) =>
            TypedResults.Ok(new { ip = http.Connection.RemoteIpAddress?.ToString(), https = http.Request.IsHttps }));
        test.MapGet("/limited", () => TypedResults.Ok()).RequireRateLimiting("test-tight");
        test.MapGet("/app-error", Ok () => throw new AppException(400, ErrorCodes.InvalidCode, "The code is wrong or has expired"));
        test.MapGet("/boom", Ok () => throw new InvalidOperationException("secret-detail-12345"));
        test.MapGet("/concurrency", Ok () => throw new DbUpdateConcurrencyException("row changed"));
        test.MapGet("/unique", Unique);
        test.MapGet("/unique-save", UniqueSave);
        test.MapPost("/validate", (TestBody body) => TypedResults.Ok(body));
        // Declared before `/test/enum` so the document generator meets the nullable use of TestStatus first.
        test.MapGet("/enum-filter", ([FromQuery] TestStatus? filter) => TypedResults.Ok(new { filter }));
        test.MapGet("/enum", () => TypedResults.Ok(new TestEnumBody { Status = TestStatus.Archived }));
        test.MapPost("/enum", (TestEnumBody body) => TypedResults.Ok(body));
        test.MapPost("/required", (TestOuterBody body) => TypedResults.Ok());
        test.MapGet("/status/{status:int}", (int status) => Results.StatusCode(status));
        test.MapGet("/log-secrets", LogSecrets);
        test.MapGet("/customer-line", (HttpContext http, ILogger<TestEndpoints> logger) =>
        {
            RequestLog.Add(http, "customerId", "c-123");
            logger.LogInformation("Customer did a thing");
            return TypedResults.Ok();
        });
        test.MapPost("/jobs/{kind}", (string kind, IBackgroundJobClient jobs) =>
        {
            var marker = Guid.NewGuid().ToString("N");
            var jobId = kind == "fail" ? jobs.Enqueue<TestJobs>(j => j.Fail(marker)) : jobs.Enqueue<TestJobs>(j => j.Ok(marker));
            return TypedResults.Ok(new { jobId, marker });
        });
        test.MapPost("/email", (string to, string code, int? validForMs, EmailSealer sealer, EmailJobQueue queue) =>
        {
            var jobId = queue.Enqueue(sealer.Seal(new EmailMessage("login-code", to,
                new Dictionary<string, string> { ["code"] = code, ["minutes"] = "10" },
                TimeSpan.FromMilliseconds(validForMs ?? 600_000))));
            return TypedResults.Ok(new { jobId });
        });
        test.MapPost("/admin/sign-in/{staffId:guid}", async (Guid staffId, HttpContext http) =>
        {
            await http.SignInAsync(StaffAuth.CookieScheme, StaffAuth.PrincipalFor(staffId));
            return TypedResults.NoContent();
        });
        test.MapGet("/admin/needs-audit", () => TypedResults.Ok()).RequireAuthorization(AdminPermissions.AuditRead);
    }

    private static async Task<Ok> Unique(AppDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(
            "create temp table if not exists t_unique (x int unique); truncate t_unique; insert into t_unique values (1), (1);");
        return TypedResults.Ok();
    }

    // The insert and the save collide inside a transaction that is never committed (the save always throws),
    // so no row ever reaches the real key ring.
    private static async Task<Ok> UniqueSave(AppDbContext db)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync(
            "insert into data_protection_keys (id, friendly_name, xml) values (2147483000, 'test', '<test/>')");
        db.DataProtectionKeys.Add(new DataProtectionKey { Id = 2147483000, FriendlyName = "test", Xml = "<test/>" });
        await db.SaveChangesAsync();
        return TypedResults.Ok();
    }

    private static Ok LogSecrets(ILogger<TestEndpoints> logger)
    {
        logger.LogInformation(
            "Sign-in check {password} {refreshToken} {loginCode} {authorization} {email} {safe} {@session} {@headers}",
            "pw-SENTINEL", "rt-SENTINEL", "654321", "Bearer ah-SENTINEL", Pii.MaskEmail("jane@example.com"), "visible-value",
            new { SessionToken = "st-SENTINEL", Device = "pixel" },
            new Dictionary<string, string> { ["Authorization"] = "Bearer dh-SENTINEL", ["Accept"] = "application/json" });
        return TypedResults.Ok();
    }
}

public sealed class TestBody
{
    [Required, EmailAddress]
    public string? Email { get; set; }

    [Range(1, 10)]
    public int Count { get; set; }
}

public enum TestStatus { Active, Archived }

public sealed class TestEnumBody
{
    public TestStatus Status { get; set; }
}

/// <summary>[Required] on a nested body and on a nullable enum: the two uses the document writes as a reference.</summary>
public sealed class TestOuterBody
{
    [Required]
    public TestInnerBody? Inner { get; set; }

    [Required]
    public TestStatus? Status { get; set; }

    public TestInnerBody? OptionalInner { get; set; }
    public TestStatus? OptionalStatus { get; set; }
}

/// <summary>Reached only as a property of <see cref="TestOuterBody"/>.</summary>
public sealed class TestInnerBody
{
    [Required]
    public string? Name { get; set; }

    public string? Note { get; set; }
}
