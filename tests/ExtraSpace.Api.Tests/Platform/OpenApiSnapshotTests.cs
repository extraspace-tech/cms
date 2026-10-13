using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ExtraSpace.Api.Platform.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ExtraSpace.Api.Tests.Platform;

/// <summary>
/// admin-ui/openapi.json is the committed copy of the API contract; the UI's TypeScript types are generated from it.
/// Update it with: UPDATE_OPENAPI=1 dotnet test tests/ExtraSpace.Api.Tests -- --filter-class "*OpenApiSnapshotTests"
/// then run `npm run gen:api` in admin-ui.
/// </summary>
public sealed class OpenApiSnapshotTests(ApiFactory api)
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [Fact]
    public async Task The_committed_document_matches_the_app()
    {
        var ct = TestContext.Current.CancellationToken;
        // Without the /test/* routes, which exist only in this test project.
        await using var host = api.WithWebHostBuilder(b => b.UseSetting("App:Role", "Api")
            .ConfigureTestServices(services => services.RemoveAll<IEndpointMapper>()));
        var document = JsonNode.Parse(await host.CreateClient().GetStringAsync("/openapi/v1.json", ct))!.AsObject();
        document.Remove("servers");   // the host name of whoever asked
        var current = document.ToJsonString(Indented).ReplaceLineEndings("\n") + "\n";

        var path = Path.Combine(RepoRoot(), "admin-ui", "openapi.json");
        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, current, ct);
            return;
        }

        File.Exists(path).Should().BeTrue("admin-ui/openapi.json is committed; see the comment on this class");
        var committed = (await File.ReadAllTextAsync(path, ct)).ReplaceLineEndings("\n");
        (committed == current).Should().BeTrue(
            "the API contract changed: rerun this test with UPDATE_OPENAPI=1, then `npm run gen:api` in admin-ui, and review both diffs");
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ExtraSpace.slnx"))) return dir.FullName;
        throw new InvalidOperationException("ExtraSpace.slnx not found above the test output folder.");
    }
}
