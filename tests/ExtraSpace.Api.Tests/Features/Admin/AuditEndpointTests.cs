using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExtraSpace.Api.Features.Admin;
using ExtraSpace.Api.Tests.TestSupport;

namespace ExtraSpace.Api.Tests.Features.Admin;

public sealed class AuditEndpointTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> AddStaffAsync(HttpClient client, Guid roleId)
    {
        var response = await client.PostAsJsonAsync("/admin/api/staff", new { email = IdentityTestData.NewEmail("audited"), roleId }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<JsonElement> ChangeRoleAsync(HttpClient client, JsonElement staff, Guid roleId)
    {
        var response = await client.PatchAsJsonAsync($"/admin/api/staff/{staff.GetProperty("id").GetGuid()}",
            new { roleId, version = staff.GetProperty("version").GetUInt32() }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private async Task<JsonElement> SearchAsync(string query)
    {
        var (reader, _) = await api.StaffClientAsync(AdminPermissions.AuditRead);
        var response = await reader.GetAsync($"/admin/api/audit?{query}", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static List<JsonElement> Items(JsonElement page) => page.GetProperty("items").EnumerateArray().ToList();

    [Fact]
    public async Task Audit_needs_audit_read()
    {
        var (client, _) = await api.StaffClientAsync(AdminPermissions.StaffManage, AdminPermissions.RolesManage);
        await Problem.ReadAsync(await client.GetAsync("/admin/api/audit", Ct), 403, "FORBIDDEN");
    }

    [Fact]
    public async Task Filters_by_staff_and_entity()
    {
        var (first, firstActor) = await api.StaffClientAsync(AdminPermissions.StaffManage);
        var (second, secondActor) = await api.StaffClientAsync(AdminPermissions.StaffManage);
        var role = await api.CreateRoleAsync();
        var staff = await AddStaffAsync(first, role.Id);
        var staffId = staff.GetProperty("id").GetGuid();
        await ChangeRoleAsync(second, staff, (await api.CreateRoleAsync()).Id);

        Items(await SearchAsync($"entityId={staffId}")).Should().HaveCount(2);

        var bySecond = Items(await SearchAsync($"entityId={staffId}&staffId={secondActor.Id}")).Should().ContainSingle().Subject;
        bySecond.GetProperty("staffId").GetGuid().Should().Be(secondActor.Id);
        bySecond.GetProperty("staffEmail").GetString().Should().Be(secondActor.Email);
        bySecond.GetProperty("action").GetString().Should().Be("PATCH /admin/api/staff/{id:guid}");
        bySecond.GetProperty("entityType").GetString().Should().Be("staff_user");
        bySecond.GetProperty("entityId").GetGuid().Should().Be(staffId);

        Items(await SearchAsync($"staffId={firstActor.Id}&entityType=staff_user")).Should().ContainSingle()
            .Which.GetProperty("action").GetString().Should().Be("POST /admin/api/staff");
        Items(await SearchAsync($"staffId={firstActor.Id}&entityType=role")).Should().BeEmpty();
    }

    [Fact]
    public async Task Pages_newest_first_with_a_total()
    {
        var (client, _) = await api.StaffClientAsync(AdminPermissions.StaffManage);
        var second = await api.CreateRoleAsync();
        var third = await api.CreateRoleAsync();
        var staff = await AddStaffAsync(client, (await api.CreateRoleAsync()).Id);
        var staffId = staff.GetProperty("id").GetGuid();
        await ChangeRoleAsync(client, await ChangeRoleAsync(client, staff, second.Id), third.Id);

        var page1 = await SearchAsync($"entityId={staffId}&pageSize=2");
        page1.GetProperty("total").GetInt32().Should().Be(3);
        page1.GetProperty("page").GetInt32().Should().Be(1);
        page1.GetProperty("pageSize").GetInt32().Should().Be(2);
        var newest = Items(page1);
        newest.Select(i => i.GetProperty("changes").GetProperty("roleId").GetProperty("to").GetGuid()).Should().Equal(third.Id, second.Id);
        newest[0].GetProperty("occurredAt").GetString().Should().EndWith("Z");

        var page2 = await SearchAsync($"entityId={staffId}&pageSize=2&page=2");
        page2.GetProperty("total").GetInt32().Should().Be(3);
        Items(page2).Should().ContainSingle().Which.GetProperty("action").GetString().Should().Be("POST /admin/api/staff");
    }

    [Fact]
    public async Task Filters_by_time_range()
    {
        var (client, _) = await api.StaffClientAsync(AdminPermissions.StaffManage);
        var staffId = (await AddStaffAsync(client, (await api.CreateRoleAsync()).Id)).GetProperty("id").GetGuid();
        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8));
        string At(TimeSpan shift) => Uri.EscapeDataString((now + shift).ToString("O"));

        Items(await SearchAsync($"entityId={staffId}&from={At(TimeSpan.FromHours(-1))}&to={At(TimeSpan.FromHours(1))}")).Should().ContainSingle();
        var later = await SearchAsync($"entityId={staffId}&from={At(TimeSpan.FromHours(1))}");
        Items(later).Should().BeEmpty();
        later.GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Rejects_a_page_size_over_100()
    {
        var (reader, _) = await api.StaffClientAsync(AdminPermissions.AuditRead);
        await Problem.ReadAsync(await reader.GetAsync("/admin/api/audit?pageSize=101", Ct), 400, "VALIDATION_FAILED");
    }

    [Theory]
    [InlineData("pageSize=0")]
    [InlineData("page=0")]
    [InlineData("page=2147483647&pageSize=2")]
    [InlineData("from=2026-10-13T10:00:00Z&to=2026-10-13T09:00:00Z")]
    [InlineData("from=2026-10-13T10:00:00")]
    [InlineData("to=2026-10-13")]
    [InlineData("from=yesterday")]
    public async Task Rejects_bad_paging_and_time_values(string query)
    {
        var (reader, _) = await api.StaffClientAsync(AdminPermissions.AuditRead);
        await Problem.ReadAsync(await reader.GetAsync($"/admin/api/audit?{query}", Ct), 400, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Accepts_an_offset_or_z()
    {
        var (reader, _) = await api.StaffClientAsync(AdminPermissions.AuditRead);
        var query = $"from={Uri.EscapeDataString("2026-10-13T10:00:00+08:00")}&to=2026-10-13T02:00:00Z";
        // The same instant written two ways: from == to, so the range is empty but valid.
        (await reader.GetAsync($"/admin/api/audit?{query}", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Changes_come_back_as_a_json_object()
    {
        var (client, _) = await api.StaffClientAsync(AdminPermissions.StaffManage);
        var from = await api.CreateRoleAsync();
        var to = await api.CreateRoleAsync();
        var staff = await AddStaffAsync(client, from.Id);
        await ChangeRoleAsync(client, staff, to.Id);

        var latest = Items(await SearchAsync($"entityId={staff.GetProperty("id").GetGuid()}&pageSize=1"))[0];

        var roleId = latest.GetProperty("changes").GetProperty("roleId");
        roleId.GetProperty("from").GetString().Should().Be(from.Id.ToString());
        roleId.GetProperty("to").GetString().Should().Be(to.Id.ToString());
    }
}
