using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Hosting;

namespace ExtraSpace.Api.Tests.Platform;

public sealed class ApiDocsTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OpenApi_and_swagger_ui_are_served_outside_production()
    {
        var client = api.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json", Ct);
        document.GetProperty("openapi").GetString().Should().StartWith("3.");
        (await client.GetAsync("/swagger/index.html", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Enums_are_strings_on_the_wire_and_in_the_document()
    {
        var client = api.CreateClient();
        var body = await client.GetFromJsonAsync<JsonElement>("/test/enum", Ct);
        body.GetProperty("status").ValueKind.Should().Be(JsonValueKind.String);
        body.GetProperty("status").GetString().Should().Be("Archived");

        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json", Ct);
        var schemas = document.GetProperty("components").GetProperty("schemas");
        var status = schemas.GetProperty(nameof(TestEnumBody)).GetProperty("properties").GetProperty("status");
        if (status.TryGetProperty("$ref", out var reference))
            status = schemas.GetProperty(reference.GetString()!.Split('/')[^1]);

        var type = status.GetProperty("type");
        var types = type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Select(t => t.GetString()).ToList() : [type.GetString()];
        types.Should().Contain("string").And.NotContain("integer");
        status.GetProperty("enum").EnumerateArray().Select(e => e.GetString()).Should().Contain(["Active", "Archived"]);
    }

    [Fact]
    public async Task Every_enum_component_is_a_string_without_null()
    {
        var client = api.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json", Ct);
        var enums = document.GetProperty("components").GetProperty("schemas").EnumerateObject()
            .Where(s => s.Value.TryGetProperty("enum", out _)).ToList();
        enums.Should().Contain(s => s.Name == nameof(TestStatus));

        foreach (var schema in enums)
        {
            schema.Value.TryGetProperty("type", out var type).Should().BeTrue($"{schema.Name} needs a type");
            var types = type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Select(t => t.GetString()).ToList() : [type.GetString()];
            types.Should().Contain("string", schema.Name).And.NotContain("integer", schema.Name);
            schema.Value.GetProperty("enum").EnumerateArray().Should().NotContain(e => e.ValueKind == JsonValueKind.Null, schema.Name);
        }
    }

    [Fact]
    public async Task Production_serves_neither()
    {
        await using var production = api.WithWebHostBuilder(b => b.UseEnvironment("Production"));
        var client = production.CreateClient();
        (await client.GetAsync("/openapi/v1.json", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/swagger/index.html", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
