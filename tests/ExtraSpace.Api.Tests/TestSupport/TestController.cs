using System.ComponentModel.DataAnnotations;
using ExtraSpace.Api.Platform.Data;
using ExtraSpace.Api.Platform.Errors;
using ExtraSpace.Api.Platform.Logging;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExtraSpace.Api.Tests.TestSupport;

/// <summary>Test-only endpoints, added to the app by <see cref="ApiFactory"/>. Never part of the shipped API.</summary>
[ApiController]
[Route("test")]
public sealed class TestController(AppDbContext db, ILogger<TestController> logger) : ControllerBase
{
    [HttpGet("ok")]
    public IActionResult Ok200() => Ok(new { ok = true });

    [HttpGet("ip")]
    public IActionResult Ip() => Ok(new { ip = HttpContext.Connection.RemoteIpAddress?.ToString(), https = Request.IsHttps });

    [HttpGet("limited")]
    [EnableRateLimiting("test-tight")]
    public IActionResult Limited() => Ok();

    [HttpGet("app-error")]
    public IActionResult AppError() => throw new AppException(400, ErrorCodes.InvalidCode, "The code is wrong or has expired");

    [HttpGet("boom")]
    public IActionResult Boom() => throw new InvalidOperationException("secret-detail-12345");

    [HttpGet("concurrency")]
    public IActionResult Concurrency() => throw new DbUpdateConcurrencyException("row changed");

    [HttpGet("unique")]
    public async Task<IActionResult> Unique()
    {
        await db.Database.ExecuteSqlRawAsync(
            "create temp table if not exists t_unique (x int unique); truncate t_unique; insert into t_unique values (1), (1);");
        return Ok();
    }

    // The insert and the save collide inside a transaction that is never committed (the save always throws),
    // so no row ever reaches the real key ring.
    [HttpGet("unique-save")]
    public async Task<IActionResult> UniqueSave()
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync(
            "insert into data_protection_keys (id, friendly_name, xml) values (2147483000, 'test', '<test/>')");
        db.DataProtectionKeys.Add(new DataProtectionKey { Id = 2147483000, FriendlyName = "test", Xml = "<test/>" });
        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("validate")]
    public IActionResult Validate(TestBody body) => Ok(body);

    // Declared before `/test/enum` so the document generator meets the nullable use of TestStatus first.
    [HttpGet("enum-filter")]
    public IActionResult EnumFilter([FromQuery] TestStatus? filter) => Ok(new { filter });

    [HttpGet("enum")]
    public ActionResult<TestEnumBody> Enum() => new TestEnumBody { Status = TestStatus.Archived };

    [HttpGet("status/{status:int}")]
    public IActionResult Status(int status) => StatusCode(status);

    [HttpGet("log-secrets")]
    public IActionResult LogSecrets()
    {
        logger.LogInformation(
            "Sign-in check {password} {refreshToken} {loginCode} {authorization} {email} {safe} {@session} {@headers}",
            "pw-SENTINEL", "rt-SENTINEL", "654321", "Bearer ah-SENTINEL", Pii.MaskEmail("jane@example.com"), "visible-value",
            new { SessionToken = "st-SENTINEL", Device = "pixel" },
            new Dictionary<string, string> { ["Authorization"] = "Bearer dh-SENTINEL", ["Accept"] = "application/json" });
        return Ok();
    }

    [HttpGet("customer-line")]
    public IActionResult CustomerLine()
    {
        RequestLog.Add(HttpContext, "customerId", "c-123");
        logger.LogInformation("Customer did a thing");
        return Ok();
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
