using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

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
    public async Task The_document_declares_the_bearer_scheme()
    {
        var client = api.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json", Ct);
        var bearer = document.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        bearer.GetProperty("type").GetString().Should().Be("http");
        bearer.GetProperty("scheme").GetString().Should().Be("bearer");
        bearer.GetProperty("bearerFormat").GetString().Should().Be("JWT");
    }

    [Fact]
    public async Task Only_routes_that_need_sign_in_require_the_bearer_scheme()
    {
        var client = api.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json", Ct);
        document.TryGetProperty("security", out _).Should().BeFalse("the requirement is per operation, not document-wide");
        var paths = document.GetProperty("paths");

        var me = paths.GetProperty("/api/v1/me").GetProperty("get").GetProperty("security");
        me.EnumerateArray().Should().ContainSingle().Which.TryGetProperty("Bearer", out _).Should().BeTrue();
        paths.GetProperty("/api/v1/auth/code").GetProperty("post").TryGetProperty("security", out _).Should().BeFalse();
        if (paths.TryGetProperty("/health/live", out var health))
            health.EnumerateObject().Where(o => o.Value.TryGetProperty("security", out _)).Should().BeEmpty();
    }

    private static IEnumerable<JsonElement> Walk(JsonElement element)
    {
        yield return element;
        var children = element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().Select(p => p.Value),
            JsonValueKind.Array => element.EnumerateArray(),
            _ => [],
        };
        foreach (var child in children)
            foreach (var nested in Walk(child))
                yield return nested;
    }

    [Fact]
    public async Task Numbers_are_never_documented_as_number_or_string()
    {
        var document = await api.CreateClient().GetFromJsonAsync<JsonElement>("/openapi/v1.json", Ct);
        var mixed = Walk(document)
            .Where(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.Array)
            .Select(e => e.GetProperty("type").EnumerateArray().Select(t => t.GetString()).ToList())
            .Where(types => types.Contains("string") && (types.Contains("integer") || types.Contains("number")));
        mixed.Should().BeEmpty("generated clients would type every number as number | string");
    }

    [Fact]
    public async Task Every_operation_documents_the_problem_body()
    {
        var document = await api.CreateClient().GetFromJsonAsync<JsonElement>("/openapi/v1.json", Ct);
        var problem = document.GetProperty("components").GetProperty("schemas").GetProperty("Problem");
        problem.GetProperty("properties").EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(["type", "title", "status", "detail", "instance", "code", "traceId", "errors"]);
        problem.GetProperty("required").EnumerateArray().Select(r => r.GetString())
            .Should().BeEquivalentTo(["title", "status", "code", "traceId"]);

        string[] methods = ["get", "post", "put", "patch", "delete"];
        var operations = document.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject().Where(o => methods.Contains(o.Name)).Select(o => (Path: path.Name, Operation: o)))
            .ToList();
        operations.Should().NotBeEmpty();
        foreach (var (path, operation) in operations)
            operation.Value.GetProperty("responses").GetProperty("default").GetProperty("content")
                .GetProperty("application/problem+json").GetProperty("schema").GetProperty("$ref").GetString()
                .Should().Be("#/components/schemas/Problem", $"{operation.Name} {path}");
    }

    [Theory]
    [InlineData("UpdateStaffBody", "version", false)]   // [Required] uint?
    [InlineData("StaffCodeBody", "email", false)]       // [Required] string?
    [InlineData("AddStaffBody", "roleId", false)]       // [Required] Guid?
    [InlineData("UpdateStaffBody", "roleId", true)]     // optional: null = unchanged
    [InlineData("StaffResponse", "lastSignInAt", true)] // always present in a response, and really null at times
    [InlineData("AdminMeResponse", "displayName", true)]
    public async Task Only_fields_marked_required_lose_null(string schema, string property, bool nullable)
    {
        var document = await api.CreateClient().GetFromJsonAsync<JsonElement>("/openapi/v1.json", Ct);
        var type = document.GetProperty("components").GetProperty("schemas").GetProperty(schema)
            .GetProperty("properties").GetProperty(property).GetProperty("type");
        var types = type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Select(t => t.GetString()).ToList() : [type.GetString()];
        types.Contains("null").Should().Be(nullable, $"{schema}.{property}");
    }

    [Theory]
    [InlineData("TestInnerBody", "name", false)]           // a body reached only as another body's property
    [InlineData("TestInnerBody", "note", true)]
    [InlineData("TestOuterBody", "inner", false)]          // [Required] nested body
    [InlineData("TestOuterBody", "status", false)]         // [Required] nullable enum
    [InlineData("TestOuterBody", "optionalInner", true)]
    [InlineData("TestOuterBody", "optionalStatus", true)]
    public async Task Required_also_removes_null_from_nested_bodies_and_references(string schema, string property, bool nullable)
    {
        var document = await api.CreateClient().GetFromJsonAsync<JsonElement>("/openapi/v1.json", Ct);
        var schemas = document.GetProperty("components").GetProperty("schemas");
        var documented = schemas.GetProperty(schema).GetProperty("properties").GetProperty(property);

        // A nullable reference is `oneOf: [null, $ref]`; a nullable inline type lists "null" in its own `type`.
        var variants = documented.TryGetProperty("oneOf", out var oneOf) ? oneOf.EnumerateArray().ToList() : [documented];
        variants.Any(IsNull).Should().Be(nullable, $"{schema}.{property} is {documented.GetRawText()}");
        variants.Any(v => v.TryGetProperty("$ref", out _) || !IsOnlyNull(v)).Should().BeTrue("a real type must be left");

        static bool IsNull(JsonElement s) => s.TryGetProperty("type", out var type) && type.ValueKind switch
        {
            JsonValueKind.Array => type.EnumerateArray().Any(t => t.GetString() == "null"),
            _ => type.GetString() == "null",
        };
        static bool IsOnlyNull(JsonElement s) => s.GetProperty("type") is { ValueKind: JsonValueKind.String } type && type.GetString() == "null";
    }

    [Fact]
    public async Task The_admin_page_fallback_is_not_in_the_document()
    {
        var document = await api.CreateClient().GetFromJsonAsync<JsonElement>("/openapi/v1.json", Ct);
        document.GetProperty("paths").EnumerateObject().Select(p => p.Name).Should().NotContain(p => p.Contains("{path}") || p.Contains("{*path"));
    }

    [Fact]
    public async Task Production_serves_neither()
    {
        // Email:Transport=None is refused outside Testing; Ses only builds a client here (nothing is sent).
        await using var production = api.WithWebHostBuilder(b => b.UseEnvironment("Production").UseSetting("App:Role", "Api")
            .UseSetting("Email:Transport", "Ses").UseSetting("Email:From", "ExtraSpace <no-reply@example.test>").UseTestKeyRing());
        var client = production.CreateClient();
        (await client.GetAsync("/openapi/v1.json", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/swagger/index.html", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var protector = production.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test");
        protector.Unprotect(protector.Protect("test-value")).Should().Be("test-value");
        production.Services.GetRequiredService<TestKeyRing>().GetAllElements().Should().NotBeEmpty();
    }
}
