using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ExtraSpace.Api.Platform.Http;

/// <summary>
/// Request bodies declare `[Required] T?` so that a missing field is a validation error, and the document then
/// says "required, may be null". Null is refused, so the document says so. Only members that carry [Required]
/// change: a response record's nullable member is always present and really can be null.
/// On a positional record the attribute must be written `[property: Required]`.
/// </summary>
internal static class RequiredNotNullOpenApi
{
    // The framework's own mark on a nullable property whose type is a shared component (a nested body, an enum);
    // it later writes such a property as `oneOf: [null, $ref]`. ApiDocsTests fails if a new version renames it.
    private const string NullableProperty = "x-is-nullable-property";

    public static OpenApiOptions AddRequiredNotNull(this OpenApiOptions options) => options
        .AddSchemaTransformer((schema, context, _) =>
        {
            // No check on JsonPropertyInfo: a nested body is transformed as its parent's property.
            if (context.JsonTypeInfo.Kind != JsonTypeInfoKind.Object || schema.Properties is null)
                return Task.CompletedTask;

            foreach (var member in context.JsonTypeInfo.Properties)
            {
                if (member.AttributeProvider?.IsDefined(typeof(RequiredAttribute), inherit: true) != true) continue;
                if (!schema.Properties.TryGetValue(member.Name, out var property) || property is not OpenApiSchema inline) continue;

                if (inline.Type is { } type && type.HasFlag(JsonSchemaType.Null) && type != JsonSchemaType.Null)
                    inline.Type = type & ~JsonSchemaType.Null;
                inline.Metadata?.Remove(NullableProperty);
            }
            return Task.CompletedTask;
        });
}
