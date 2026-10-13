using System.Security.Claims;
using System.Text.Json;
using ExtraSpace.Api.Features.Admin;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace ExtraSpace.Api.Tests.Features.Admin;

/// <summary>The OIDC events, called with hand-built contexts: a real Entra round trip needs Microsoft.</summary>
public sealed class StaffAuthEventsTests(ApiFactory api)
{
    private static readonly AuthenticationScheme Entra = new(StaffAuth.EntraScheme, null, typeof(OpenIdConnectHandler));

    private static DefaultHttpContext NewHttp(IServiceProvider services) =>
        new() { RequestServices = services, Response = { Body = new MemoryStream() } };

    private static ClaimsPrincipal EntraUser(string? objectId, string email, string name = "Entra Name")
    {
        var claims = new List<Claim> { new("preferred_username", email), new("name", name) };
        if (objectId is not null) claims.Add(new Claim("oid", objectId));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test-entra"));
    }

    private static async Task<JsonElement> ProblemAsync(HttpContext http, int status, string code)
    {
        http.Response.StatusCode.Should().Be(status);
        http.Response.Body.Position = 0;
        var body = (await JsonDocument.ParseAsync(http.Response.Body)).RootElement;
        body.GetProperty("code").GetString().Should().Be(code);
        return body;
    }

    private async Task<StaffUser> LoadAsync(Guid id)
    {
        StaffUser? staff = null;
        await api.WithDbAsync(async db => staff = await db.Set<StaffUser>().SingleAsync(s => s.Id == id));
        return staff!;
    }

    [Fact]
    public async Task A_known_staff_member_gets_a_principal_with_only_the_id()
    {
        var staff = await api.CreateStaffAsync((await api.CreateRoleAsync()).Id);
        await using var scope = api.Services.CreateAsyncScope();
        var http = NewHttp(scope.ServiceProvider);
        var context = new TokenValidatedContext(http, Entra, new OpenIdConnectOptions(),
            EntraUser($"test-oid-{Guid.NewGuid():N}", staff.Email), new AuthenticationProperties());

        await StaffAuth.OnTokenValidatedAsync(context);

        context.Result.Should().BeNull();
        context.Principal!.Claims.Should().ContainSingle().Which.Should().Match<Claim>(c =>
            c.Type == ClaimTypes.NameIdentifier && c.Value == staff.Id.ToString());
        context.Principal.StaffId().Should().Be(staff.Id);
        context.Properties!.RedirectUri.Should().Be("/admin");
    }

    [Fact]
    public async Task A_refused_person_gets_403_and_no_cookie()
    {
        await using var scope = api.Services.CreateAsyncScope();
        var http = NewHttp(scope.ServiceProvider);
        var user = EntraUser($"test-oid-{Guid.NewGuid():N}", IdentityTestData.NewEmail("s"));
        var context = new TokenValidatedContext(http, Entra, new OpenIdConnectOptions(), user, new AuthenticationProperties());

        await StaffAuth.OnTokenValidatedAsync(context);

        context.Result!.Handled.Should().BeTrue();
        context.Principal.Should().BeSameAs(user);
        context.Principal!.StaffId().Should().BeNull();
        http.Response.Headers.SetCookie.Should().BeEmpty();
        await ProblemAsync(http, 403, "FORBIDDEN");
    }

    [Fact]
    public async Task A_token_without_an_object_id_is_refused_before_lookup()
    {
        var staff = await api.CreateStaffAsync((await api.CreateRoleAsync()).Id);
        await using var scope = api.Services.CreateAsyncScope();
        var http = NewHttp(scope.ServiceProvider);
        var context = new TokenValidatedContext(http, Entra, new OpenIdConnectOptions(), EntraUser(null, staff.Email), new AuthenticationProperties());

        await StaffAuth.OnTokenValidatedAsync(context);

        context.Result!.Handled.Should().BeTrue();
        await ProblemAsync(http, 403, "FORBIDDEN");
        (await LoadAsync(staff.Id)).LastSignInAt.Should().BeNull("the lookup never ran");
    }

    [Fact]
    public async Task A_remote_failure_answers_401_and_logs_no_token()
    {
        var marker = Guid.NewGuid().ToString("N");
        var idToken = $"eyJhbGciOiJSUzI1NiJ9.eyJ0ZXN0IjoidGVzdC1vbmx5In0.{marker}{marker}";
        await using var scope = api.Services.CreateAsyncScope();
        var http = NewHttp(scope.ServiceProvider);
        var context = new RemoteFailureContext(http, Entra, new OpenIdConnectOptions(),
            new SecurityTokenException($"Unable to validate the 'id_token', no suitable TokenHandler was found for: '{idToken}'. case {marker}"));

        await StaffAuth.OnRemoteFailureAsync(context);

        context.Result!.Handled.Should().BeTrue();
        await ProblemAsync(http, 401, "UNAUTHENTICATED");
        var line = api.Logs.All().Where(l => l.TryGetProperty("failureMessage", out var m) && m.GetString()!.Contains($"case {marker}"))
            .Should().ContainSingle().Subject;
        line.GetProperty("level").GetString().Should().Be("Warning");
        line.GetProperty("failureType").GetString().Should().Be(nameof(SecurityTokenException));
        line.GetProperty("failureMessage").GetString().Should().Contain("[redacted]");
        line.GetRawText().Should().NotContain(idToken).And.NotContain(marker + marker);
    }
}
