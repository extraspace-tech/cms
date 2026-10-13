using System.Net.Http.Json;
using System.Text.Json;
using ExtraSpace.Api.Features.Admin;
using ExtraSpace.Api.Tests.TestSupport;

namespace ExtraSpace.Api.Tests.Features.Admin;

public sealed class PermissionsEndpointTests(ApiFactory api)
{
    [Fact]
    public async Task The_catalogue_lists_names_and_descriptions()
    {
        var (client, _) = await api.StaffClientAsync();

        var list = await client.GetFromJsonAsync<JsonElement>("/admin/api/permissions", TestContext.Current.CancellationToken);

        var items = list.EnumerateArray().ToList();
        items.Select(p => p.GetProperty("name").GetString())
            .Should().Equal(AdminPermissions.AuditRead, AdminPermissions.JobsManage, AdminPermissions.RolesManage, AdminPermissions.StaffManage);
        items.Should().AllSatisfy(p => p.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace());
    }
}
