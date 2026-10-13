using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ExtraSpace.Api.Platform.Errors;

/// <summary>S0 spec §4.1 in the OpenAPI document: one Problem schema, and every operation says its errors use it.</summary>
internal static class ProblemOpenApi
{
    private const string SchemaName = "Problem";

    public static OpenApiOptions AddProblemResponses(this OpenApiOptions options) => options
        .AddDocumentTransformer((document, _, _) =>
        {
            document.Components ??= new OpenApiComponents();
            document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
            // TryAdd: a DTO that is really named Problem, or an endpoint with its own default response, is kept.
            document.Components.Schemas.TryAdd(SchemaName, new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Required = new HashSet<string> { "title", "status", "code", "traceId" },
                Properties = new Dictionary<string, IOpenApiSchema>
                {
                    ["type"] = Text(),
                    ["title"] = Text(),
                    ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
                    ["detail"] = Text(),
                    ["instance"] = Text(),
                    ["code"] = Text(),
                    ["traceId"] = Text(),
                    // Present for field validation only; keys are camelCase field names.
                    ["errors"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        AdditionalProperties = new OpenApiSchema { Type = JsonSchemaType.Array, Items = Text() },
                    },
                },
            });
            return Task.CompletedTask;
        })
        .AddOperationTransformer((operation, context, _) =>
        {
            operation.Responses ??= new OpenApiResponses();
            operation.Responses.TryAdd("default", new OpenApiResponse
            {
                Description = "Error",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/problem+json"] = new() { Schema = new OpenApiSchemaReference(SchemaName, context.Document) },
                },
            });
            return Task.CompletedTask;
        });

    private static OpenApiSchema Text() => new() { Type = JsonSchemaType.String };
}
