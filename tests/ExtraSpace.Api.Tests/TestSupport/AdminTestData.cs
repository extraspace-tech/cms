using System.Net.Http.Json;
using System.Text.Json;
using ExtraSpace.Api.Features.Admin;
using ExtraSpace.Api.Platform.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

    private static int _ip;

    /// <summary>An admin client with its own X-Forwarded-For address, so the per-IP sign-in limits of one test never hit another.</summary>
    public static HttpClient AdminClientWithIp(this WebApplicationFactory<Program> api)
    {
        var n = Interlocked.Increment(ref _ip);
        var client = api.AdminClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"11.{(n >> 16) & 255}.{(n >> 8) & 255}.{n & 255}");
        return client;
    }

    public static Task<HttpResponseMessage> RequestStaffCodeAsync(this HttpClient client, string email) =>
        client.PostAsJsonAsync("/admin/api/auth/code", new { email }, TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> VerifyStaffCodeAsync(this HttpClient client, string email, string code) =>
        client.PostAsJsonAsync("/admin/api/auth/code/verify", new { email, code }, TestContext.Current.CancellationToken);

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
}

[CollectionDefinition("Owners", DisableParallelization = true)]
public sealed class OwnersCollection;
