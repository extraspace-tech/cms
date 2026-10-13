using System.Net.Http.Json;
using System.Text.Json;
using ExtraSpace.Api.Features.Admin;
using ExtraSpace.Api.Platform.Data;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace ExtraSpace.Api.Tests.TestSupport;

internal static class AdminTestData
{
    public static async Task<Role> CreateRoleAsync(this ApiFactory api, params string[] permissions)
    {
        var role = new Role { Name = $"role-{Guid.NewGuid():N}", Permissions = permissions };
        await api.WithDbAsync(async db => { db.Add(role); await db.SaveChangesAsync(); });
        return role;
    }

    public static async Task<StaffUser> CreateStaffAsync(this ApiFactory api, Guid roleId, bool active = true, string? email = null)
    {
        var staff = new StaffUser { Email = email ?? IdentityTestData.NewEmail("s"), RoleId = roleId, Active = active };
        await api.WithDbAsync(async db => { db.Add(staff); await db.SaveChangesAsync(); });
        return staff;
    }

    /// <summary>Runs <paramref name="action"/> in a scope whose saves are audited as <paramref name="staffId"/>.</summary>
    public static async Task AsStaffAsync(this ApiFactory api, Guid staffId, Func<AppDbContext, Task> action)
    {
        await using var scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<AuditContext>().Begin(staffId, "TEST", null);
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public static async Task<List<AuditLog>> AuditRowsForAsync(this ApiFactory api, Guid entityId)
    {
        List<AuditLog> rows = [];
        await api.WithDbAsync(async db => rows = await db.Set<AuditLog>().Where(a => a.EntityId == entityId).OrderBy(a => a.Id).ToListAsync());
        return rows;
    }

    /// <summary>Owners collection only: deactivates every Owner so the test starts from "no active Owner".</summary>
    public static Task ClearOwnersAsync(this ApiFactory api) =>
        api.WithDbAsync(db => db.Set<StaffUser>().Where(s => s.RoleId == Role.OwnerId).ExecuteUpdateAsync(u => u.SetProperty(s => s.Active, false)));

    /// <summary>__Host- cookies are Secure, and the cookie container sends them over https only.</summary>
    public static HttpClient AdminClient(this WebApplicationFactory<Program> api) =>
        api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    public static async Task<HttpClient> SignedInAdminAsync(this ApiFactory api, StaffUser staff, bool withCsrf = true)
    {
        var client = api.AdminClient();
        (await client.PostAsync($"/test/admin/sign-in/{staff.Id}", null, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        if (withCsrf) await client.AddCsrfAsync();
        return client;
    }

    /// <summary>Fetches a CSRF token for the client's current sign-in and sends it on every later request.</summary>
    public static async Task<string> AddCsrfAsync(this HttpClient client)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/admin/api/auth/csrf", TestContext.Current.CancellationToken);
        var token = csrf.GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Remove(StaffAuth.CsrfHeader);
        client.DefaultRequestHeaders.Add(StaffAuth.CsrfHeader, token);
        return token;
    }

    public static async Task<(HttpClient Client, StaffUser Staff)> StaffClientAsync(this ApiFactory api, params string[] permissions)
    {
        var role = await api.CreateRoleAsync(permissions);
        var staff = await api.CreateStaffAsync(role.Id);
        return (await api.SignedInAdminAsync(staff), staff);
    }

    /// <summary>
    /// Test-only Entra settings (Production refuses to start without them). The OIDC handler gets a static configuration,
    /// and its backchannel throws, so a metadata or token call fails the test instead of reaching Microsoft.
    /// </summary>
    public static IWebHostBuilder UseTestEntra(this IWebHostBuilder builder) => builder
        .UseSetting("Admin:Entra:TenantId", "test-tenant")
        .UseSetting("Admin:Entra:ClientId", "test-client")
        .UseSetting("Admin:Entra:ClientSecret", "test-client-secret")
        .ConfigureTestServices(services =>
        {
            // Configure runs before the OIDC post-configure, which builds the backchannel from this handler.
            services.Configure<OpenIdConnectOptions>(StaffAuth.EntraScheme, o => o.BackchannelHttpHandler = new NoNetworkHandler());
            // The OIDC post-configure has already built a metadata ConfigurationManager by now: replace it, not only Configuration.
            services.PostConfigure<OpenIdConnectOptions>(StaffAuth.EntraScheme, o =>
            {
                o.Configuration = new OpenIdConnectConfiguration
                {
                    AuthorizationEndpoint = "https://login.example.test/authorize",
                    Issuer = "https://login.example.test/test-tenant/v2.0",
                };
                o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(o.Configuration);
            });
        });

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException($"A test host tried to call {request.RequestUri}.");
    }
}

[CollectionDefinition("Owners", DisableParallelization = true)]
public sealed class OwnersCollection;
