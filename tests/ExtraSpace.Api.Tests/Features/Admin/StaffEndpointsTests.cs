using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExtraSpace.Api.Features.Admin;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace ExtraSpace.Api.Tests.Features.Admin;

public sealed class StaffEndpointsTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid id, object body) =>
        client.PatchAsJsonAsync($"/admin/api/staff/{id}", body, Ct);

    private async Task<StaffUser> LoadAsync(Guid id)
    {
        StaffUser? staff = null;
        await api.WithDbAsync(async db => staff = await db.Set<StaffUser>().SingleAsync(s => s.Id == id));
        return staff!;
    }

    private async Task<int> AuditRowsByAsync(Guid actorId)
    {
        var count = 0;
        await api.WithDbAsync(async db => count = await db.Set<AuditLog>().CountAsync(a => a.StaffUserId == actorId));
        return count;
    }

    [Fact]
    public async Task Staff_routes_need_staff_manage()
    {
        var (client, staff) = await api.StaffClientAsync(AdminPermissions.RolesManage, AdminPermissions.AuditRead);
        var role = await api.CreateRoleAsync();

        await Problem.ReadAsync(await client.GetAsync("/admin/api/staff", Ct), 403, "FORBIDDEN");
        await Problem.ReadAsync(await client.PostAsJsonAsync("/admin/api/staff",
            new { email = IdentityTestData.NewEmail("added"), roleId = role.Id }, Ct), 403, "FORBIDDEN");
        await Problem.ReadAsync(await PatchAsync(client, staff.Id, new { roleId = role.Id, version = staff.Version }), 403, "FORBIDDEN");
        (await AuditRowsByAsync(staff.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Adding_staff_writes_an_audit_row()
    {
        var (client, actor) = await api.StaffClientAsync(AdminPermissions.StaffManage);
        var role = await api.CreateRoleAsync();
        var email = IdentityTestData.NewEmail("added");

        var response = await client.PostAsJsonAsync("/admin/api/staff", new { email = $" {email.ToUpperInvariant()} ", roleId = role.Id }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = body.GetProperty("id").GetGuid();
        body.GetProperty("email").GetString().Should().Be(email);
        body.GetProperty("roleId").GetGuid().Should().Be(role.Id);
        body.GetProperty("active").GetBoolean().Should().BeTrue();
        body.GetProperty("version").GetUInt32().Should().BeGreaterThan(0);

        var row = (await api.AuditRowsForAsync(id)).Should().ContainSingle().Subject;
        row.Action.Should().Be("POST /admin/api/staff");
        row.EntityType.Should().Be("staff_user");
        row.StaffUserId.Should().Be(actor.Id);
        row.Changes.RootElement.GetProperty("email").GetProperty("to").GetString().Should().Be(email);
        (await LoadAsync(id)).CreatedByStaffId.Should().Be(actor.Id);
    }

    [Fact]
    public async Task An_email_already_used_in_another_case_gets_409()
    {
        var (client, actor) = await api.StaffClientAsync(AdminPermissions.StaffManage);
        var role = await api.CreateRoleAsync();
        var existing = await api.CreateStaffAsync(role.Id);

        var response = await client.PostAsJsonAsync("/admin/api/staff", new { email = existing.Email.ToUpperInvariant(), roleId = role.Id }, Ct);

        await Problem.ReadAsync(response, 409, "CONFLICT");
        (await AuditRowsByAsync(actor.Id)).Should().Be(0);
        var warning = api.Logs.LinesFor(Problem.TraceIdOf(response)).Should()
            .ContainSingle(l => l.GetProperty("messageTemplate").GetString() == "Database constraint conflict {sqlState} on {constraint}").Subject;
        warning.GetProperty("level").GetString().Should().Be("Warning");
        warning.GetProperty("constraint").GetString().Should().Be("ix_staff_user_email");
        warning.GetRawText().Should().NotContain(existing.Email);
    }

    [Fact]
    public async Task Patching_to_an_unknown_role_gets_400()
    {
        var (client, actor) = await api.StaffClientAsync(AdminPermissions.StaffManage);
        var target = await api.CreateStaffAsync((await api.CreateRoleAsync()).Id);

        await Problem.ReadAsync(await PatchAsync(client, target.Id, new { roleId = Guid.NewGuid(), version = target.Version }), 400, "VALIDATION_FAILED");

        (await LoadAsync(target.Id)).RoleId.Should().Be(target.RoleId);
        (await AuditRowsByAsync(actor.Id)).Should().Be(0);
    }

    [Fact]
    public async Task An_unknown_role_gets_400()
    {
        var (client, actor) = await api.StaffClientAsync(AdminPermissions.StaffManage);

        var response = await client.PostAsJsonAsync("/admin/api/staff", new { email = IdentityTestData.NewEmail("added"), roleId = Guid.NewGuid() }, Ct);

        await Problem.ReadAsync(response, 400, "VALIDATION_FAILED");
        (await AuditRowsByAsync(actor.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Changing_the_role_records_only_that_field()
    {
        var (client, actor) = await api.StaffClientAsync(AdminPermissions.StaffManage);
        var from = await api.CreateRoleAsync();
        var to = await api.CreateRoleAsync();
        var target = await api.CreateStaffAsync(from.Id);

        var response = await PatchAsync(client, target.Id, new { roleId = to.Id, version = target.Version });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("roleId").GetGuid().Should().Be(to.Id);
        body.GetProperty("roleName").GetString().Should().Be(to.Name);
        body.GetProperty("version").GetUInt32().Should().NotBe(target.Version);

        var row = (await api.AuditRowsForAsync(target.Id)).Should().ContainSingle().Subject;
        row.Action.Should().Be("PATCH /admin/api/staff/{id:guid}");
        row.StaffUserId.Should().Be(actor.Id);
        var changes = row.Changes.RootElement;
        changes.EnumerateObject().Select(p => p.Name).Should().Equal("roleId");
        changes.GetProperty("roleId").GetProperty("from").GetGuid().Should().Be(from.Id);
        changes.GetProperty("roleId").GetProperty("to").GetGuid().Should().Be(to.Id);
    }

    [Fact]
    public async Task A_stale_version_gets_409_and_changes_nothing()
    {
        var (client, _) = await api.StaffClientAsync(AdminPermissions.StaffManage);
        var first = await api.CreateRoleAsync();
        var second = await api.CreateRoleAsync();
        var target = await api.CreateStaffAsync((await api.CreateRoleAsync()).Id);

        (await PatchAsync(client, target.Id, new { roleId = first.Id, version = target.Version })).StatusCode.Should().Be(HttpStatusCode.OK);
        await Problem.ReadAsync(await PatchAsync(client, target.Id, new { roleId = second.Id, version = target.Version }), 409, "CONFLICT");

        (await LoadAsync(target.Id)).RoleId.Should().Be(first.Id);
        (await api.AuditRowsForAsync(target.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_missing_version_gets_400()
    {
        var (client, _) = await api.StaffClientAsync(AdminPermissions.StaffManage);
        var target = await api.CreateStaffAsync((await api.CreateRoleAsync()).Id);

        var body = await Problem.ReadAsync(await PatchAsync(client, target.Id, new { active = false }), 400, "VALIDATION_FAILED");

        body.GetProperty("errors").TryGetProperty("version", out _).Should().BeTrue();
        (await LoadAsync(target.Id)).Active.Should().BeTrue();
    }

    [Fact]
    public async Task Nobody_can_deactivate_themself()
    {
        var (client, actor) = await api.StaffClientAsync(AdminPermissions.StaffManage);

        await Problem.ReadAsync(await PatchAsync(client, actor.Id, new { active = false, version = actor.Version }), 409, "CANNOT_DEACTIVATE_SELF");

        (await LoadAsync(actor.Id)).Active.Should().BeTrue();
        (await AuditRowsByAsync(actor.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Only_an_owner_can_grant_the_owner_role()
    {
        var (client, actor) = await api.StaffClientAsync(AdminPermissions.StaffManage);
        var email = IdentityTestData.NewEmail("owner");

        await Problem.ReadAsync(await client.PostAsJsonAsync("/admin/api/staff", new { email, roleId = Role.OwnerId }, Ct), 403, "FORBIDDEN");
        await Problem.ReadAsync(await PatchAsync(client, actor.Id, new { roleId = Role.OwnerId, version = actor.Version }), 403, "FORBIDDEN");

        (await LoadAsync(actor.Id)).RoleId.Should().Be(actor.RoleId);
        var added = true;
        await api.WithDbAsync(async db => added = await db.Set<StaffUser>().AnyAsync(s => s.Email == email));
        added.Should().BeFalse();
        (await AuditRowsByAsync(actor.Id)).Should().Be(0);
    }

    [Fact]
    public async Task The_list_shows_role_name_and_last_sign_in()
    {
        var (client, _) = await api.StaffClientAsync(AdminPermissions.StaffManage);
        var role = await api.CreateRoleAsync();
        var target = await api.CreateStaffAsync(role.Id);
        await api.WithDbAsync(async db =>
        {
            (await db.Set<StaffUser>().SingleAsync(s => s.Id == target.Id)).LastSignInAt = new DateTime(2026, 10, 1, 2, 3, 4, DateTimeKind.Utc);
            await db.SaveChangesAsync();
        });

        var list = await client.GetFromJsonAsync<JsonElement>("/admin/api/staff", Ct);

        var item = list.EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == target.Id);
        item.GetProperty("email").GetString().Should().Be(target.Email);
        item.GetProperty("roleId").GetGuid().Should().Be(role.Id);
        item.GetProperty("roleName").GetString().Should().Be(role.Name);
        item.GetProperty("active").GetBoolean().Should().BeTrue();
        item.GetProperty("lastSignInAt").GetString().Should().Be("2026-10-01T02:03:04Z");
        item.GetProperty("version").GetUInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Adding_staff_records_the_display_name()
    {
        var (client, _) = await api.StaffClientAsync(AdminPermissions.StaffManage);
        var role = await api.CreateRoleAsync();

        var named = await client.PostAsJsonAsync("/admin/api/staff",
            new { email = IdentityTestData.NewEmail("added"), roleId = role.Id, displayName = "  Jane Staff  " }, Ct);
        var blank = await client.PostAsJsonAsync("/admin/api/staff",
            new { email = IdentityTestData.NewEmail("added"), roleId = role.Id, displayName = "   " }, Ct);

        named.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await named.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("displayName").GetString().Should().Be("Jane Staff");
        var row = (await api.AuditRowsForAsync(body.GetProperty("id").GetGuid())).Should().ContainSingle().Subject;
        row.Changes.RootElement.GetProperty("displayName").GetProperty("to").GetString().Should().Be("Jane Staff");
        blank.StatusCode.Should().Be(HttpStatusCode.Created);
        (await blank.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("displayName").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Changing_the_display_name_records_only_that_field()
    {
        var (client, actor) = await api.StaffClientAsync(AdminPermissions.StaffManage);
        var other = await api.CreateRoleAsync();
        var target = await api.CreateStaffAsync((await api.CreateRoleAsync()).Id);

        var renamed = await PatchAsync(client, target.Id, new { displayName = " Jane Staff ", version = target.Version });

        renamed.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await renamed.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("displayName").GetString().Should().Be("Jane Staff");
        var row = (await api.AuditRowsForAsync(target.Id)).Should().ContainSingle().Subject;
        row.StaffUserId.Should().Be(actor.Id);
        var changes = row.Changes.RootElement;
        changes.EnumerateObject().Select(p => p.Name).Should().Equal("displayName");
        changes.GetProperty("displayName").GetProperty("from").ValueKind.Should().Be(JsonValueKind.Null);
        changes.GetProperty("displayName").GetProperty("to").GetString().Should().Be("Jane Staff");

        // A PATCH without the field leaves the name; an empty one clears it.
        var moved = await PatchAsync(client, target.Id, new { roleId = other.Id, version = body.GetProperty("version").GetUInt32() });
        var movedBody = await moved.Content.ReadFromJsonAsync<JsonElement>(Ct);
        movedBody.GetProperty("displayName").GetString().Should().Be("Jane Staff");
        var cleared = await PatchAsync(client, target.Id, new { displayName = "", version = movedBody.GetProperty("version").GetUInt32() });
        cleared.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoadAsync(target.Id)).DisplayName.Should().BeNull();
    }

    [Fact]
    public async Task A_display_name_over_200_characters_gets_400()
    {
        var (client, _) = await api.StaffClientAsync(AdminPermissions.StaffManage);
        var role = await api.CreateRoleAsync();
        var target = await api.CreateStaffAsync(role.Id);
        var tooLong = new string('n', 201);

        var add = await Problem.ReadAsync(await client.PostAsJsonAsync("/admin/api/staff",
            new { email = IdentityTestData.NewEmail("added"), roleId = role.Id, displayName = tooLong }, Ct), 400, "VALIDATION_FAILED");
        var patch = await Problem.ReadAsync(await PatchAsync(client, target.Id, new { displayName = tooLong, version = target.Version }), 400, "VALIDATION_FAILED");

        add.GetProperty("errors").TryGetProperty("displayName", out _).Should().BeTrue();
        patch.GetProperty("errors").TryGetProperty("displayName", out _).Should().BeTrue();
        (await LoadAsync(target.Id)).DisplayName.Should().BeNull();
    }

    [Fact]
    public async Task An_email_outside_the_allowed_domains_gets_400()
    {
        await using var host = api.WithWebHostBuilder(b => b.UseSetting("App:Role", "Api").UseSetting("Admin:AllowedEmailDomains:0", "example.test"));
        var role = await api.CreateRoleAsync(AdminPermissions.StaffManage);
        var actor = await api.CreateStaffAsync(role.Id);
        var client = host.AdminClient();
        (await client.PostAsync($"/test/admin/sign-in/{actor.Id}", null, Ct)).EnsureSuccessStatusCode();
        await client.AddCsrfAsync();
        var outside = $"added-{Guid.NewGuid():N}@other.test";

        var refused = await client.PostAsJsonAsync("/admin/api/staff", new { email = outside, roleId = role.Id }, Ct);
        var allowed = await client.PostAsJsonAsync("/admin/api/staff", new { email = IdentityTestData.NewEmail("added").ToUpperInvariant(), roleId = role.Id }, Ct);

        (await Problem.ReadAsync(refused, 400, "VALIDATION_FAILED")).GetProperty("title").GetString().Should().Be("This email domain is not allowed.");
        var added = true;
        await api.WithDbAsync(async db => added = await db.Set<StaffUser>().AnyAsync(s => s.Email == outside));
        added.Should().BeFalse();
        (await AuditRowsByAsync(actor.Id)).Should().Be(1);
        allowed.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
