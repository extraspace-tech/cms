using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExtraSpace.Api.Features.Admin;
using ExtraSpace.Api.Platform.Data;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExtraSpace.Api.Tests.Features.Admin;

[Collection("Owners")]
public sealed class OwnerRulesTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid id, object body) =>
        client.PatchAsJsonAsync($"/admin/api/staff/{id}", body, Ct);

    private async Task<(HttpClient Client, StaffUser Staff)> OwnerAsync()
    {
        var owner = await api.CreateStaffAsync(Role.OwnerId);
        return (await api.SignedInAdminAsync(owner), owner);
    }

    private async Task<StaffUser> LoadAsync(Guid id)
    {
        StaffUser? staff = null;
        await api.WithDbAsync(async db => staff = await db.Set<StaffUser>().SingleAsync(s => s.Id == id));
        return staff!;
    }

    private async Task<int> ActiveOwnersAsync()
    {
        var count = 0;
        await api.WithDbAsync(async db => count = await db.Set<StaffUser>().CountAsync(s => s.Active && s.RoleId == Role.OwnerId));
        return count;
    }

    /// <summary>
    /// Holds staff-owners in a test transaction, sends the request, waits until it queues on the lock, runs
    /// <paramref name="underLock"/> in the same transaction, then commits (releasing the lock) and returns the response.
    /// </summary>
    private async Task<HttpResponseMessage> WhileOwnersLockedAsync(Func<Task<HttpResponseMessage>> request, Func<AppDbContext, Task> underLock)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(Ct);
        await StaffLocks.OwnersAsync(db, Ct);

        var pending = request();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!pending.IsCompleted && await AdvisoryLockWaitersAsync() == 0 && DateTime.UtcNow < deadline) await Task.Delay(20, Ct);
        pending.IsCompleted.Should().BeFalse("the request must wait on the staff-owners lock");
        (await AdvisoryLockWaitersAsync()).Should().Be(1);

        await underLock(db);
        await transaction.CommitAsync(Ct);
        return await pending;
    }

    // The Owners collection runs alone, so the only advisory-lock waiter is the request under test.
    private async Task<int> AdvisoryLockWaitersAsync()
    {
        var count = 0;
        await api.WithDbAsync(async db => count = await db.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_locks WHERE locktype = 'advisory' AND NOT granted").SingleAsync());
        return count;
    }

    private static async Task DemoteAsync(AppDbContext db, Guid staffId, Guid roleId)
    {
        (await db.Set<StaffUser>().SingleAsync(s => s.Id == staffId)).RoleId = roleId;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task An_owner_demoted_while_waiting_for_the_lock_cannot_demote_another_owner()
    {
        await api.ClearOwnersAsync();
        await api.CreateStaffAsync(Role.OwnerId);   // A: with C gone, an Owner would still remain, so only the actor re-check can refuse
        var (clientB, b) = await OwnerAsync();
        var c = await api.CreateStaffAsync(Role.OwnerId);
        var plain = await api.CreateRoleAsync(AdminPermissions.StaffManage);

        var response = await WhileOwnersLockedAsync(
            () => PatchAsync(clientB, c.Id, new { roleId = plain.Id, version = c.Version }),
            db => DemoteAsync(db, b.Id, plain.Id));

        await Problem.ReadAsync(response, 403, "FORBIDDEN");
        (await LoadAsync(c.Id)).RoleId.Should().Be(Role.OwnerId);
        (await api.AuditRowsForAsync(c.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task An_owner_demoted_while_waiting_for_the_lock_cannot_add_an_owner()
    {
        await api.ClearOwnersAsync();
        await api.CreateStaffAsync(Role.OwnerId);
        var (clientB, b) = await OwnerAsync();
        var plain = await api.CreateRoleAsync(AdminPermissions.StaffManage);
        var email = IdentityTestData.NewEmail("owner");

        var response = await WhileOwnersLockedAsync(
            () => clientB.PostAsJsonAsync("/admin/api/staff", new { email, roleId = Role.OwnerId }, Ct),
            db => DemoteAsync(db, b.Id, plain.Id));

        await Problem.ReadAsync(response, 403, "FORBIDDEN");
        var added = true;
        await api.WithDbAsync(async db => added = await db.Set<StaffUser>().AnyAsync(s => s.Email == email));
        added.Should().BeFalse();
    }

    [Fact]
    public async Task An_owner_can_add_an_owner()
    {
        await api.ClearOwnersAsync();
        var (client, _) = await OwnerAsync();

        var response = await client.PostAsJsonAsync("/admin/api/staff", new { email = IdentityTestData.NewEmail("owner"), roleId = Role.OwnerId }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await ActiveOwnersAsync()).Should().Be(2);
    }

    [Fact]
    public async Task The_only_owner_cannot_demote_themself()
    {
        await api.ClearOwnersAsync();
        var (client, owner) = await OwnerAsync();
        var plain = await api.CreateRoleAsync(AdminPermissions.StaffManage);

        await Problem.ReadAsync(await PatchAsync(client, owner.Id, new { roleId = plain.Id, version = owner.Version }), 409, "LAST_OWNER");

        var reloaded = await LoadAsync(owner.Id);
        reloaded.RoleId.Should().Be(Role.OwnerId);
        reloaded.Active.Should().BeTrue();
        (await api.AuditRowsForAsync(owner.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Only_an_owner_can_change_an_owner()
    {
        await api.ClearOwnersAsync();
        var owner = await api.CreateStaffAsync(Role.OwnerId);
        var plain = await api.CreateRoleAsync();
        var (client, _) = await api.StaffClientAsync(AdminPermissions.StaffManage);

        await Problem.ReadAsync(await PatchAsync(client, owner.Id, new { active = false, version = owner.Version }), 403, "FORBIDDEN");
        await Problem.ReadAsync(await PatchAsync(client, owner.Id, new { roleId = plain.Id, version = owner.Version }), 403, "FORBIDDEN");

        var reloaded = await LoadAsync(owner.Id);
        reloaded.RoleId.Should().Be(Role.OwnerId);
        reloaded.Active.Should().BeTrue();
        (await api.AuditRowsForAsync(owner.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task An_owner_can_demote_another_owner()
    {
        await api.ClearOwnersAsync();
        var (client, _) = await OwnerAsync();
        var other = await api.CreateStaffAsync(Role.OwnerId);
        var plain = await api.CreateRoleAsync();

        (await PatchAsync(client, other.Id, new { roleId = plain.Id, version = other.Version })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await LoadAsync(other.Id)).RoleId.Should().Be(plain.Id);
        (await api.AuditRowsForAsync(other.Id)).Should().ContainSingle();
        (await ActiveOwnersAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Two_owners_demoting_each_other_at_once_leave_one_owner()
    {
        // The plain role keeps staff.manage, so the loser passes the permission policy and only the lock-guarded re-check can stop it.
        var plain = await api.CreateRoleAsync(AdminPermissions.StaffManage);
        for (var round = 0; round < 5; round++)
        {
            await api.ClearOwnersAsync();
            var (clientA, a) = await OwnerAsync();
            var (clientB, b) = await OwnerAsync();

            var responses = await Task.WhenAll(
                PatchAsync(clientA, b.Id, new { roleId = plain.Id, version = b.Version }),
                PatchAsync(clientB, a.Id, new { roleId = plain.Id, version = a.Version }));

            responses.Select(r => r.StatusCode).Should().ContainSingle(s => s == HttpStatusCode.OK);
            responses.Single(r => r.StatusCode != HttpStatusCode.OK).StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Conflict);
            (await ActiveOwnersAsync()).Should().Be(1);
            ((await api.AuditRowsForAsync(a.Id)).Count + (await api.AuditRowsForAsync(b.Id)).Count).Should().Be(1);
        }
    }

    [Fact]
    public async Task An_owner_has_every_permission()
    {
        await api.ClearOwnersAsync();
        var (client, _) = await OwnerAsync();

        var me = await client.GetFromJsonAsync<JsonElement>("/admin/api/me", Ct);

        var permissions = me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToList();
        permissions.Should().Equal(api.Services.GetRequiredService<PermissionCatalog>().Names);
        permissions.Should().Contain([AdminPermissions.StaffManage, AdminPermissions.RolesManage, AdminPermissions.AuditRead, AdminPermissions.JobsManage]);
    }

    [Fact]
    public async Task Me_marks_an_owner()
    {
        await api.ClearOwnersAsync();
        var (client, _) = await OwnerAsync();

        var me = await client.GetFromJsonAsync<JsonElement>("/admin/api/me", Ct);

        me.GetProperty("isOwner").GetBoolean().Should().BeTrue();
    }
}
