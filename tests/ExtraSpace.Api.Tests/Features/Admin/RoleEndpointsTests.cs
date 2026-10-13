using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExtraSpace.Api.Features.Admin;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExtraSpace.Api.Tests.Features.Admin;

public sealed class RoleEndpointsTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewName() => $"Ops-{Guid.NewGuid():N}";

    private Task<(HttpClient Client, StaffUser Staff)> ManagerAsync() => api.StaffClientAsync(AdminPermissions.RolesManage);

    private async Task<Role?> FindAsync(Guid id)
    {
        Role? role = null;
        await api.WithDbAsync(async db => role = await db.Set<Role>().SingleOrDefaultAsync(r => r.Id == id));
        return role;
    }

    private async Task<int> AuditRowsByAsync(Guid actorId)
    {
        var count = 0;
        await api.WithDbAsync(async db => count = await db.Set<AuditLog>().CountAsync(a => a.StaffUserId == actorId));
        return count;
    }

    [Fact]
    public async Task Creating_a_role_writes_an_audit_row()
    {
        var (client, actor) = await ManagerAsync();
        var name = NewName();

        var response = await client.PostAsJsonAsync("/admin/api/roles",
            new { name = $" {name} ", permissions = new[] { AdminPermissions.StaffManage, AdminPermissions.AuditRead, AdminPermissions.StaffManage } }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = body.GetProperty("id").GetGuid();
        body.GetProperty("name").GetString().Should().Be(name);
        body.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).Should().Equal(AdminPermissions.AuditRead, AdminPermissions.StaffManage);
        body.GetProperty("isSystem").GetBoolean().Should().BeFalse();
        body.GetProperty("staffCount").GetInt32().Should().Be(0);
        body.GetProperty("version").GetUInt32().Should().BeGreaterThan(0);

        var row = (await api.AuditRowsForAsync(id)).Should().ContainSingle().Subject;
        row.Action.Should().Be("POST /admin/api/roles");
        row.EntityType.Should().Be("role");
        row.StaffUserId.Should().Be(actor.Id);
        row.Changes.RootElement.GetProperty("name").GetProperty("to").GetString().Should().Be(name);
    }

    [Fact]
    public async Task An_unknown_permission_gets_400()
    {
        var (client, actor) = await ManagerAsync();

        var response = await client.PostAsJsonAsync("/admin/api/roles", new { name = NewName(), permissions = new[] { "test.unknown" } }, Ct);

        await Problem.ReadAsync(response, 400, "VALIDATION_FAILED");
        (await AuditRowsByAsync(actor.Id)).Should().Be(0);
    }

    [Fact]
    public async Task A_null_permission_gets_400()
    {
        var (client, actor) = await ManagerAsync();

        var response = await client.PostAsJsonAsync("/admin/api/roles", new { name = NewName(), permissions = new[] { AdminPermissions.AuditRead, null } }, Ct);

        await Problem.ReadAsync(response, 400, "VALIDATION_FAILED");
        (await AuditRowsByAsync(actor.Id)).Should().Be(0);
    }

    [Fact]
    public async Task A_retired_permission_is_not_listed_and_the_listed_ones_can_be_saved_back()
    {
        var (client, _) = await ManagerAsync();
        var role = await api.CreateRoleAsync(AdminPermissions.AuditRead, "test.retired");

        var listed = (await client.GetFromJsonAsync<JsonElement>("/admin/api/roles", Ct)).EnumerateArray()
            .Single(r => r.GetProperty("id").GetGuid() == role.Id);
        var permissions = listed.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToArray();
        permissions.Should().Equal(AdminPermissions.AuditRead);

        (await client.PatchAsJsonAsync($"/admin/api/roles/{role.Id}", new { permissions, version = listed.GetProperty("version").GetUInt32() }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await FindAsync(role.Id))!.Permissions.Should().Equal(AdminPermissions.AuditRead);
    }

    [Fact]
    public async Task Creating_with_a_taken_name_ignoring_case_and_spaces_gets_409_role_name_taken()
    {
        var (client, actor) = await ManagerAsync();
        var name = NewName();
        (await client.PostAsJsonAsync("/admin/api/roles", new { name, permissions = Array.Empty<string>() }, Ct)).StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await client.PostAsJsonAsync("/admin/api/roles", new { name = $"  {name.ToUpperInvariant()} ", permissions = Array.Empty<string>() }, Ct);

        await Problem.ReadAsync(response, 409, "ROLE_NAME_TAKEN");
        (await AuditRowsByAsync(actor.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Renaming_to_a_taken_name_gets_409_role_name_taken()
    {
        var (client, _) = await ManagerAsync();
        var taken = await api.CreateRoleAsync();
        var role = await api.CreateRoleAsync();

        var response = await client.PatchAsJsonAsync($"/admin/api/roles/{role.Id}", new { name = $" {taken.Name.ToUpperInvariant()} ", version = role.Version }, Ct);

        await Problem.ReadAsync(response, 409, "ROLE_NAME_TAKEN");
        (await FindAsync(role.Id))!.Name.Should().Be(role.Name);
        (await api.AuditRowsForAsync(role.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_role_can_be_renamed_to_its_own_name_in_another_case()
    {
        var (client, _) = await ManagerAsync();
        var role = await api.CreateRoleAsync();
        var renamed = role.Name.ToUpperInvariant();
        renamed.Should().NotBe(role.Name);

        var response = await client.PatchAsJsonAsync($"/admin/api/roles/{role.Id}", new { name = renamed, version = role.Version }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await FindAsync(role.Id))!.Name.Should().Be(renamed);
    }

    [Fact]
    public async Task Renaming_with_a_stale_version_gets_409_conflict()
    {
        var (client, _) = await ManagerAsync();
        var role = await api.CreateRoleAsync();
        var first = NewName();
        (await client.PatchAsJsonAsync($"/admin/api/roles/{role.Id}", new { name = first, version = role.Version }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await Problem.ReadAsync(await client.PatchAsJsonAsync($"/admin/api/roles/{role.Id}", new { name = NewName(), version = role.Version }, Ct), 409, "CONFLICT");

        (await FindAsync(role.Id))!.Name.Should().Be(first);
    }

    [Fact]
    public async Task The_owner_role_cannot_be_edited_or_deleted()
    {
        var (client, actor) = await ManagerAsync();
        var owner = (await FindAsync(Role.OwnerId))!;

        await Problem.ReadAsync(await client.PatchAsJsonAsync($"/admin/api/roles/{Role.OwnerId}", new { name = "Boss", version = owner.Version }, Ct), 409, "SYSTEM_ROLE");
        await Problem.ReadAsync(await client.DeleteAsync($"/admin/api/roles/{Role.OwnerId}?version={owner.Version}", Ct), 409, "SYSTEM_ROLE");

        (await FindAsync(Role.OwnerId))!.Name.Should().Be("Owner");
        (await AuditRowsByAsync(actor.Id)).Should().Be(0);
    }

    [Fact]
    public async Task A_role_in_use_cannot_be_deleted()
    {
        var (client, actor) = await ManagerAsync();
        var role = await api.CreateRoleAsync();
        await api.CreateStaffAsync(role.Id, active: false);

        await Problem.ReadAsync(await client.DeleteAsync($"/admin/api/roles/{role.Id}?version={role.Version}", Ct), 409, "ROLE_IN_USE");

        (await FindAsync(role.Id)).Should().NotBeNull();
        (await AuditRowsByAsync(actor.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Deleting_with_a_stale_version_gets_409()
    {
        var (client, _) = await ManagerAsync();
        var role = await api.CreateRoleAsync();
        (await client.PatchAsJsonAsync($"/admin/api/roles/{role.Id}", new { name = NewName(), version = role.Version }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await Problem.ReadAsync(await client.DeleteAsync($"/admin/api/roles/{role.Id}?version={role.Version}", Ct), 409, "CONFLICT");

        (await FindAsync(role.Id)).Should().NotBeNull();
        (await api.AuditRowsForAsync(role.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_missing_delete_version_gets_400()
    {
        var (client, _) = await ManagerAsync();
        var role = await api.CreateRoleAsync();

        await Problem.ReadAsync(await client.DeleteAsync($"/admin/api/roles/{role.Id}", Ct), 400, "VALIDATION_FAILED");

        (await FindAsync(role.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task Deleting_a_role_records_its_old_values()
    {
        var (client, actor) = await ManagerAsync();
        var role = await api.CreateRoleAsync(AdminPermissions.AuditRead);

        (await client.DeleteAsync($"/admin/api/roles/{role.Id}?version={role.Version}", Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await FindAsync(role.Id)).Should().BeNull();
        var row = (await api.AuditRowsForAsync(role.Id)).Should().ContainSingle().Subject;
        row.Action.Should().Be("DELETE /admin/api/roles/{id:guid}");
        row.StaffUserId.Should().Be(actor.Id);
        var name = row.Changes.RootElement.GetProperty("name");
        name.GetProperty("from").GetString().Should().Be(role.Name);
        name.GetProperty("to").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Any_signed_in_staff_member_can_list_roles()
    {
        var (client, staff) = await api.StaffClientAsync();

        var response = await client.GetAsync("/admin/api/roles", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var roles = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).EnumerateArray().ToList();
        var owner = roles[0];
        owner.GetProperty("id").GetGuid().Should().Be(Role.OwnerId);
        owner.GetProperty("isSystem").GetBoolean().Should().BeTrue();
        owner.GetProperty("permissions").EnumerateArray().Select(p => p.GetString())
            .Should().Equal(api.Services.GetRequiredService<PermissionCatalog>().Names);
        var own = roles.Single(r => r.GetProperty("id").GetGuid() == staff.RoleId);
        own.GetProperty("staffCount").GetInt32().Should().Be(1);
        own.GetProperty("permissions").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Role_writes_need_roles_manage()
    {
        var (client, actor) = await api.StaffClientAsync(AdminPermissions.StaffManage, AdminPermissions.AuditRead);
        var role = await api.CreateRoleAsync();

        await Problem.ReadAsync(await client.PostAsJsonAsync("/admin/api/roles", new { name = NewName(), permissions = Array.Empty<string>() }, Ct), 403, "FORBIDDEN");
        await Problem.ReadAsync(await client.PatchAsJsonAsync($"/admin/api/roles/{role.Id}", new { name = NewName(), version = role.Version }, Ct), 403, "FORBIDDEN");
        await Problem.ReadAsync(await client.DeleteAsync($"/admin/api/roles/{role.Id}?version={role.Version}", Ct), 403, "FORBIDDEN");

        (await FindAsync(role.Id))!.Name.Should().Be(role.Name);
        (await AuditRowsByAsync(actor.Id)).Should().Be(0);
    }
}
