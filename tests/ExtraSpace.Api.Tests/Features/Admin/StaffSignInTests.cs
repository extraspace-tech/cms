using ExtraSpace.Api.Features.Admin;
using ExtraSpace.Api.Features.Identity.Contracts;
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

    private async Task<T> WithSignInAsync<T>(AdminOptions options, Func<StaffSignIn, AppDbContext, Task<T>> action)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();
        var signIn = new StaffSignIn(db, services.GetRequiredService<ILoginCodes>(), Options.Create(options), TimeProvider.System,
            services.GetRequiredService<ILogger<StaffSignIn>>());
        return await action(signIn, db);
    }

    private Task<Guid?> ResolveAsync(string email, AdminOptions? options = null) =>
        WithSignInAsync(options ?? new AdminOptions(), (signIn, _) => signIn.ResolveAsync(email, Ct));

    private Task<bool> MaySignInAsync(string email, AdminOptions? options = null) =>
        WithSignInAsync(options ?? new AdminOptions(), (signIn, _) => signIn.MaySignInAsync(email, Ct));

    private async Task<StaffUser?> FindAsync(string email)
    {
        StaffUser? staff = null;
        await api.WithDbAsync(async db => staff = await db.Set<StaffUser>().SingleOrDefaultAsync(s => s.Email == email));
        return staff;
    }

    private async Task<StaffUser> NewStaffAsync(bool active = true) =>
        await api.CreateStaffAsync((await api.CreateRoleAsync()).Id, active);

    private IEnumerable<string?> SignedInLinesFor(Guid staffId) => api.Logs.All()
        .Where(l => l.TryGetProperty("staffId", out var id) && id.GetString() == staffId.ToString())
        .Select(l => l.GetProperty("messageTemplate").GetString());

    [Fact]
    public async Task An_unknown_email_is_refused()
    {
        await api.ClearOwnersAsync();
        var email = IdentityTestData.NewEmail("s");

        (await MaySignInAsync(email)).Should().BeFalse();
        (await ResolveAsync(email)).Should().BeNull();
        (await FindAsync(email)).Should().BeNull();
    }

    [Fact]
    public async Task An_inactive_staff_member_is_refused()
    {
        await api.ClearOwnersAsync();
        var staff = await NewStaffAsync(active: false);

        (await MaySignInAsync(staff.Email)).Should().BeFalse();
        (await ResolveAsync(staff.Email)).Should().BeNull();
        (await FindAsync(staff.Email))!.LastSignInAt.Should().BeNull();
    }

    [Fact]
    public async Task A_staff_member_is_found_by_email_and_the_last_sign_in_is_recorded()
    {
        await api.ClearOwnersAsync();
        var staff = await NewStaffAsync();

        (await MaySignInAsync($" {staff.Email.ToUpperInvariant()} ")).Should().BeTrue();
        (await ResolveAsync($" {staff.Email.ToUpperInvariant()} ")).Should().Be(staff.Id);

        (await FindAsync(staff.Email))!.LastSignInAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        SignedInLinesFor(staff.Id).Should().Equal("Staff {staffId} signed in");
    }

    [Fact]
    public async Task A_bootstrap_email_becomes_owner_while_no_owner_exists()
    {
        await api.ClearOwnersAsync();
        var email = IdentityTestData.NewEmail("boot");
        var options = new AdminOptions { BootstrapEmails = [$" {email.ToUpperInvariant()} "] };

        (await MaySignInAsync(email, options)).Should().BeTrue();
        (await FindAsync(email)).Should().BeNull("asking writes nothing");
        var id = await ResolveAsync(email.ToUpperInvariant(), options);

        var staff = await FindAsync(email);
        staff!.Id.Should().Be(id!.Value);
        staff.RoleId.Should().Be(Role.OwnerId);
        staff.Active.Should().BeTrue();
        SignedInLinesFor(staff.Id).Should().Equal("Bootstrap Owner {staffId} created", "Staff {staffId} signed in");
    }

    [Fact]
    public async Task Bootstrap_is_refused_once_an_active_owner_exists()
    {
        await api.ClearOwnersAsync();
        await api.CreateStaffAsync(Role.OwnerId);
        var email = IdentityTestData.NewEmail("boot");
        var options = new AdminOptions { BootstrapEmails = [email] };

        (await MaySignInAsync(email, options)).Should().BeFalse();
        (await ResolveAsync(email, options)).Should().BeNull();
        (await FindAsync(email)).Should().BeNull();
    }

    [Fact]
    public async Task A_deactivated_bootstrap_address_does_not_come_back()
    {
        await api.ClearOwnersAsync();
        var staff = await api.CreateStaffAsync(Role.OwnerId, active: false);
        var options = new AdminOptions { BootstrapEmails = [staff.Email] };

        (await MaySignInAsync(staff.Email, options)).Should().BeFalse();
        (await ResolveAsync(staff.Email, options)).Should().BeNull();
        (await FindAsync(staff.Email))!.Active.Should().BeFalse();
    }

    [Fact]
    public async Task A_disallowed_domain_is_refused()
    {
        await api.ClearOwnersAsync();
        var staff = await NewStaffAsync();
        var bootstrap = IdentityTestData.NewEmail("boot");
        var options = new AdminOptions { AllowedEmailDomains = ["company.test"], BootstrapEmails = [bootstrap] };

        (await MaySignInAsync(staff.Email, options)).Should().BeFalse();
        (await ResolveAsync(staff.Email, options)).Should().BeNull();
        (await MaySignInAsync(bootstrap, options)).Should().BeFalse();
        (await ResolveAsync(bootstrap, options)).Should().BeNull();
        (await FindAsync(bootstrap)).Should().BeNull();

        var allowed = new AdminOptions { AllowedEmailDomains = ["company.test", " @Example.Test "] };
        (await MaySignInAsync(staff.Email, allowed)).Should().BeTrue();
        (await ResolveAsync(staff.Email, allowed)).Should().Be(staff.Id);
    }

    [Fact]
    public async Task Inside_a_callers_transaction_it_joins_and_never_commits()
    {
        await api.ClearOwnersAsync();
        var staff = await NewStaffAsync();
        var bootstrap = IdentityTestData.NewEmail("boot");

        var bootstrapId = await WithSignInAsync(new AdminOptions { BootstrapEmails = [bootstrap] }, async (signIn, db) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(Ct);
            (await signIn.ResolveAsync(staff.Email, Ct)).Should().Be(staff.Id);
            var created = await signIn.ResolveAsync(bootstrap, Ct);
            db.Database.CurrentTransaction.Should().BeSameAs(transaction);
            await transaction.RollbackAsync(Ct);
            return created;
        });

        bootstrapId.Should().NotBeNull();
        (await FindAsync(staff.Email))!.LastSignInAt.Should().BeNull();
        (await FindAsync(bootstrap)).Should().BeNull();
        SignedInLinesFor(staff.Id).Should().BeEmpty("nothing was committed");
        SignedInLinesFor(bootstrapId!.Value).Should().BeEmpty("nothing was committed");
    }

    [Fact]
    public async Task The_refusal_line_masks_the_email()
    {
        await api.ClearOwnersAsync();
        var email = IdentityTestData.NewEmail("refused");

        (await ResolveAsync(email)).Should().BeNull();

        var refusals = api.Logs.All()
            .Where(l => l.TryGetProperty("messageTemplate", out var t) && t.GetString() == "Staff sign-in refused for {email}: {reason}");
        refusals.Should().Contain(l => l.GetProperty("level").GetString() == "Warning"
            && l.GetProperty("email").GetString() == Pii.MaskEmail(email) && l.GetProperty("reason").GetString() == "unknown");
        api.Logs.All().Should().NotContain(l => l.GetRawText().Contains(email));
    }
}
