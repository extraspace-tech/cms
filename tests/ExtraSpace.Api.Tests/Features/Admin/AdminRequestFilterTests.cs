using ExtraSpace.Api.Features.Admin;
using ExtraSpace.Api.Platform.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ExtraSpace.Api.Tests.Features.Admin;

public sealed class AdminRequestFilterTests(ApiFactory api)
{
    private async Task<(bool Called, Exception? Error)> RunAsync(params object[] metadata)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(metadata), "test"));
        var called = false;
        try
        {
            await new AdminRequestFilter().InvokeAsync(new DefaultEndpointFilterInvocationContext(http),
                _ => { called = true; return ValueTask.FromResult<object?>(null); });
            return (called, null);
        }
        catch (Exception e) { return (called, e); }
    }

    [Fact]
    public async Task A_route_that_needs_staff_fails_closed_without_a_staff_id()
    {
        var (called, error) = await RunAsync();
        called.Should().BeFalse();
        error.Should().BeOfType<AppException>().Which.Status.Should().Be(401);
    }

    [Fact]
    public async Task An_anonymous_route_runs_without_a_staff_id()
    {
        var (called, error) = await RunAsync(new AllowAnonymousAttribute());
        error.Should().BeNull();
        called.Should().BeTrue();
    }
}
