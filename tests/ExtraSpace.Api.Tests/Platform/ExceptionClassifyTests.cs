using ExtraSpace.Api.Integrations.SiteLink;
using ExtraSpace.Api.Platform.Errors;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExtraSpace.Api.Tests.Platform;

public sealed class ExceptionClassifyTests
{
    [Fact]
    public void Wrapped_unique_violation_is_409_conflict()
    {
        var exception = new DbUpdateException("x", new PostgresException("dup", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation));
        var (status, code, _) = AppExceptionHandler.Classify(exception);
        status.Should().Be(409);
        code.Should().Be("CONFLICT");
    }

    [Fact]
    public void Wrapped_foreign_key_violation_is_409_conflict()
    {
        var exception = new DbUpdateException("x", new PostgresException("fk", "ERROR", "ERROR", PostgresErrorCodes.ForeignKeyViolation));
        var (status, code, _) = AppExceptionHandler.Classify(exception);
        status.Should().Be(409);
        code.Should().Be("CONFLICT");
    }

    [Fact]
    public void Unknown_exception_is_500_internal_error()
    {
        var (status, code, _) = AppExceptionHandler.Classify(new InvalidOperationException("x"));
        status.Should().Be(500);
        code.Should().Be("INTERNAL_ERROR");
    }

    [Fact]
    public void A_problem_exception_keeps_its_status_and_code()
    {
        AppExceptionHandler.Classify(new SiteLinkException("UnitsInformation_v3", -1, "x")).Should()
            .Be((502, "SITELINK_ERROR", "The storage system refused the request. Try again later or contact the facility."));
        AppExceptionHandler.Classify(new SiteLinkUnavailableException("UnitsInformation_v3", "HTTP 503")).Status.Should().Be(503);
    }
}
