using System.Text.Json;
using ExtraSpace.Api.Features.Admin;
using ExtraSpace.Api.Features.Admin.Contracts;
using ExtraSpace.Api.Platform.Data;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ExtraSpace.Api.Tests.Features.Admin;

public sealed class AdminDataTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_owner_role_is_seeded()
    {
        Role? owner = null;
        await api.WithDbAsync(async db => owner = await db.Set<Role>().SingleOrDefaultAsync(r => r.Id == Role.OwnerId));
        owner.Should().NotBeNull();
        owner!.Name.Should().Be("Owner");
        owner.IsSystem.Should().BeTrue();
    }

    [Fact]
    public async Task Staff_emails_are_unique()
    {
        var role = await api.CreateRoleAsync();
        var email = IdentityTestData.NewEmail("s");
        await api.CreateStaffAsync(role.Id, email: email);
        var duplicate = () => api.CreateStaffAsync(role.Id, email: email);
        await duplicate.Should().ThrowAsync<DbUpdateException>().Where(e => IsUniqueViolation(e));
    }

    [Fact]
    public async Task Role_names_are_unique_ignoring_case()
    {
        var suffix = Guid.NewGuid().ToString("N");
        await api.WithDbAsync(async db => { db.Add(new Role { Name = $"Ops-{suffix}" }); await db.SaveChangesAsync(); });
        var duplicate = () => api.WithDbAsync(async db => { db.Add(new Role { Name = $"ops-{suffix}" }); await db.SaveChangesAsync(); });
        await duplicate.Should().ThrowAsync<DbUpdateException>().Where(e => IsUniqueViolation(e));
    }

    [Fact]
    public async Task A_stale_version_cannot_be_saved()
    {
        var staff = await api.CreateStaffAsync((await api.CreateRoleAsync()).Id);
        await using var first = api.Services.CreateAsyncScope();
        await using var second = api.Services.CreateAsyncScope();
        var firstDb = first.ServiceProvider.GetRequiredService<AppDbContext>();
        var secondDb = second.ServiceProvider.GetRequiredService<AppDbContext>();
        var fresh = await firstDb.Set<StaffUser>().SingleAsync(s => s.Id == staff.Id, Ct);
        var stale = await secondDb.Set<StaffUser>().SingleAsync(s => s.Id == staff.Id, Ct);

        fresh.DisplayName = "First";
        await firstDb.SaveChangesAsync(Ct);
        stale.DisplayName = "Second";
        var save = () => secondDb.SaveChangesAsync();
        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task An_audited_create_writes_one_row_with_the_new_values()
    {
        var actor = await NewActorAsync();
        var role = new Role { Name = $"role-{Guid.NewGuid():N}", Permissions = [AdminPermissions.AuditRead] };
        var staff = new StaffUser { Email = IdentityTestData.NewEmail("s"), RoleId = role.Id };
        await api.AsStaffAsync(actor.Id, async db => { db.AddRange(role, staff); await db.SaveChangesAsync(); });

        var row = (await api.AuditRowsForAsync(role.Id)).Should().ContainSingle().Subject;
        row.StaffUserId.Should().Be(actor.Id);
        row.Action.Should().Be("TEST");
        row.EntityType.Should().Be("role");
        var changes = row.Changes.RootElement;
        changes.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("name", "permissions", "isSystem");
        changes.GetProperty("name").GetProperty("from").ValueKind.Should().Be(JsonValueKind.Null);
        changes.GetProperty("name").GetProperty("to").GetString().Should().Be(role.Name);
        changes.GetProperty("permissions").GetProperty("to").EnumerateArray().Select(e => e.GetString()).Should().Equal(AdminPermissions.AuditRead);
        changes.GetProperty("isSystem").GetProperty("to").GetBoolean().Should().BeFalse();

        var staffChanges = (await api.AuditRowsForAsync(staff.Id)).Should().ContainSingle().Subject.Changes.RootElement;
        staffChanges.GetProperty("roleId").GetProperty("to").GetGuid().Should().Be(role.Id);
        staffChanges.GetProperty("active").GetProperty("to").GetBoolean().Should().BeTrue();

        var saved = await LoadRoleAsync(role.Id);
        saved.CreatedByStaffId.Should().Be(actor.Id);
        saved.UpdatedByStaffId.Should().Be(actor.Id);
    }

    [Fact]
    public async Task An_audited_update_records_only_changed_fields()
    {
        var actor = await NewActorAsync();
        var role = await api.CreateRoleAsync(AdminPermissions.AuditRead);
        var newName = $"renamed-{Guid.NewGuid():N}";
        await api.AsStaffAsync(actor.Id, async db =>
        {
            (await db.Set<Role>().SingleAsync(r => r.Id == role.Id)).Name = newName;
            await db.SaveChangesAsync();
        });

        var changes = (await api.AuditRowsForAsync(role.Id)).Should().ContainSingle().Subject.Changes.RootElement;
        changes.EnumerateObject().Select(p => p.Name).Should().Equal("name");
        changes.GetProperty("name").GetProperty("from").GetString().Should().Be(role.Name);
        changes.GetProperty("name").GetProperty("to").GetString().Should().Be(newName);

        var saved = await LoadRoleAsync(role.Id);
        saved.UpdatedByStaffId.Should().Be(actor.Id);
        saved.CreatedByStaffId.Should().BeNull();
    }

    [Fact]
    public async Task An_audited_delete_records_the_old_values()
    {
        var actor = await NewActorAsync();
        var role = await api.CreateRoleAsync();
        await api.AsStaffAsync(actor.Id, async db =>
        {
            db.Remove(await db.Set<Role>().SingleAsync(r => r.Id == role.Id));
            await db.SaveChangesAsync();
        });

        var changes = (await api.AuditRowsForAsync(role.Id)).Should().ContainSingle().Subject.Changes.RootElement;
        changes.GetProperty("name").GetProperty("from").GetString().Should().Be(role.Name);
        changes.GetProperty("name").GetProperty("to").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Saves_outside_an_admin_write_are_not_audited()
    {
        var role = await api.CreateRoleAsync();
        (await api.AuditRowsForAsync(role.Id)).Should().BeEmpty();
        (await LoadRoleAsync(role.Id)).CreatedByStaffId.Should().BeNull();
    }

    [Fact]
    public async Task A_failed_save_writes_no_audit_row()
    {
        var actor = await NewActorAsync();
        var suffix = Guid.NewGuid().ToString("N");
        await api.WithDbAsync(async db => { db.Add(new Role { Name = $"Ops-{suffix}" }); await db.SaveChangesAsync(); });
        var clash = new Role { Name = $"ops-{suffix}" };

        var save = () => api.AsStaffAsync(actor.Id, async db => { db.Add(clash); await db.SaveChangesAsync(); });
        await save.Should().ThrowAsync<DbUpdateException>();
        (await api.AuditRowsForAsync(clash.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task An_audit_row_that_cannot_be_saved_rolls_back_the_change()
    {
        var role = new Role { Name = $"role-{Guid.NewGuid():N}" };
        // No such staff: the audit_log foreign key fails, and the role insert goes with it.
        var save = () => api.AsStaffAsync(Guid.CreateVersion7(), async db => { db.Add(role); await db.SaveChangesAsync(); });
        await save.Should().ThrowAsync<DbUpdateException>()
            .Where(e => IsForeignKeyViolation(e));
        var saved = true;
        await api.WithDbAsync(async db => saved = await db.Set<Role>().AnyAsync(r => r.Id == role.Id));
        saved.Should().BeFalse();
    }

    [Fact]
    public async Task A_retry_after_a_failed_save_writes_one_audit_row()
    {
        var actor = await NewActorAsync();
        var suffix = Guid.NewGuid().ToString("N");
        await api.WithDbAsync(async db => { db.Add(new Role { Name = $"Ops-{suffix}" }); await db.SaveChangesAsync(); });
        var clash = new Role { Name = $"ops-{suffix}" };

        await api.AsStaffAsync(actor.Id, async db =>
        {
            db.Add(clash);
            var first = () => db.SaveChangesAsync();
            await first.Should().ThrowAsync<DbUpdateException>();
            clash.Name = $"retry-{suffix}";
            await db.SaveChangesAsync();
        });

        var row = (await api.AuditRowsForAsync(clash.Id)).Should().ContainSingle().Subject;
        row.Changes.RootElement.GetProperty("name").GetProperty("to").GetString().Should().Be($"retry-{suffix}");
    }

    [Fact]
    public void The_catalogue_lists_every_registered_permission() =>
        api.Services.GetRequiredService<PermissionCatalog>().Names.Should().Equal("audit.read", "jobs.manage", "roles.manage", "sites.manage", "staff.manage");

    [Fact]
    public void A_permission_registered_twice_fails()
    {
        var permission = new Permission("test.twice", "Test permission");
        var build = () => new PermissionCatalog([permission, permission]);
        build.Should().Throw<InvalidOperationException>();
    }

    private async Task<StaffUser> NewActorAsync() => await api.CreateStaffAsync((await api.CreateRoleAsync()).Id);

    private async Task<Role> LoadRoleAsync(Guid id)
    {
        Role? role = null;
        await api.WithDbAsync(async db => role = await db.Set<Role>().SingleAsync(r => r.Id == id));
        return role!;
    }

    private static bool IsForeignKeyViolation(DbUpdateException e) =>
        e.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation };

    private static bool IsUniqueViolation(DbUpdateException e) =>
        e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
