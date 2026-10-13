using ExtraSpace.Api.Features.Admin;
using ExtraSpace.Api.Platform.Data;
using ExtraSpace.Api.Platform.Logging;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExtraSpace.Api.Tests.Features.Admin;

[Collection("Owners")]
public sealed class StaffSignInTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Guid?> ResolveAsync(string? objectId, string? email, string? name = null, params string[] bootstrapEmails)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var signIn = new StaffSignIn(services.GetRequiredService<AppDbContext>(),
            Options.Create(new AdminOptions { BootstrapEmails = bootstrapEmails }), TimeProvider.System,
            services.GetRequiredService<ILogger<StaffSignIn>>());
        return await signIn.ResolveAsync(objectId, email, name, Ct);
    }

    private async Task<StaffUser> LoadAsync(Guid id)
    {
        StaffUser? staff = null;
        await api.WithDbAsync(async db => staff = await db.Set<StaffUser>().SingleAsync(s => s.Id == id));
        return staff!;
    }

    private async Task<StaffUser> NewStaffAsync(bool active = true) =>
        await api.CreateStaffAsync((await api.CreateRoleAsync()).Id, active);

    private static string NewObjectId() => $"test-oid-{Guid.NewGuid():N}";

    [Fact]
    public async Task An_unknown_email_is_refused()
    {
        await api.ClearOwnersAsync();
        (await ResolveAsync(NewObjectId(), IdentityTestData.NewEmail("s"))).Should().BeNull();
    }

    [Fact]
    public async Task An_inactive_staff_member_is_refused()
    {
        await api.ClearOwnersAsync();
        var staff = await NewStaffAsync(active: false);
        (await ResolveAsync(NewObjectId(), staff.Email)).Should().BeNull();
    }

    [Fact]
    public async Task The_first_sign_in_by_email_links_the_object_id()
    {
        await api.ClearOwnersAsync();
        var staff = await NewStaffAsync();
        var objectId = NewObjectId();

        (await ResolveAsync(objectId, staff.Email.ToUpperInvariant())).Should().Be(staff.Id);
        (await LoadAsync(staff.Id)).EntraObjectId.Should().Be(objectId);
        (await ResolveAsync(objectId, IdentityTestData.NewEmail("renamed"))).Should().Be(staff.Id);
    }

    [Fact]
    public async Task An_email_linked_to_another_object_id_is_refused()
    {
        await api.ClearOwnersAsync();
        var staff = await NewStaffAsync();
        (await ResolveAsync(NewObjectId(), staff.Email)).Should().Be(staff.Id);
        (await ResolveAsync(NewObjectId(), staff.Email)).Should().BeNull();
    }

    [Fact]
    public async Task The_display_name_and_last_sign_in_are_recorded()
    {
        await api.ClearOwnersAsync();
        var unnamed = await NewStaffAsync();
        var named = await NewStaffAsync();
        await api.WithDbAsync(async db =>
        {
            (await db.Set<StaffUser>().SingleAsync(s => s.Id == named.Id)).DisplayName = "Kept Name";
            await db.SaveChangesAsync();
        });

        await ResolveAsync(null, unnamed.Email, "Jane Staff");
        await ResolveAsync(null, named.Email, "Other Name");

        var first = await LoadAsync(unnamed.Id);
        first.DisplayName.Should().Be("Jane Staff");
        first.LastSignInAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        (await LoadAsync(named.Id)).DisplayName.Should().Be("Kept Name");
    }

    [Fact]
    public async Task A_bootstrap_email_becomes_owner_while_no_owner_exists()
    {
        await api.ClearOwnersAsync();
        var email = IdentityTestData.NewEmail("boot");

        var id = await ResolveAsync(NewObjectId(), email.ToUpperInvariant(), "First Owner", email);

        id.Should().NotBeNull();
        var staff = await LoadAsync(id!.Value);
        staff.RoleId.Should().Be(Role.OwnerId);
        staff.Active.Should().BeTrue();
        staff.Email.Should().Be(email);
        staff.DisplayName.Should().Be("First Owner");
    }

    [Fact]
    public async Task Bootstrap_is_refused_once_an_active_owner_exists()
    {
        await api.ClearOwnersAsync();
        await api.CreateStaffAsync(Role.OwnerId);
        var email = IdentityTestData.NewEmail("boot");

        (await ResolveAsync(NewObjectId(), email, null, email)).Should().BeNull();
        var exists = true;
        await api.WithDbAsync(async db => exists = await db.Set<StaffUser>().AnyAsync(s => s.Email == email));
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task The_refusal_line_masks_the_email()
    {
        await api.ClearOwnersAsync();
        var email = IdentityTestData.NewEmail("refused");

        (await ResolveAsync(null, email)).Should().BeNull();

        var refusals = api.Logs.All()
            .Where(l => l.TryGetProperty("messageTemplate", out var t) && t.GetString() == "Staff sign-in refused for {email}: {reason}");
        refusals.Should().Contain(l => l.GetProperty("level").GetString() == "Warning"
            && l.GetProperty("email").GetString() == Pii.MaskEmail(email) && l.GetProperty("reason").GetString() == "unknown");
        api.Logs.All().Should().NotContain(l => l.GetRawText().Contains(email));
    }
}
