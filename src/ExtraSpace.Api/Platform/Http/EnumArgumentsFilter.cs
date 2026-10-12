using System.Text.Json;

namespace ExtraSpace.Api.Platform.Http;

/// <summary>Rejects enum arguments that are not a defined member: route and query binding use <c>Enum.TryParse</c>, which takes any number.</summary>
public static class EnumArgumentsFilter
{
    public static RouteGroupBuilder WithEnumGuard(this RouteGroupBuilder group)
    {
        group.AddEndpointFilterFactory(Create);
        return group;
    }

    private static EndpointFilterDelegate Create(EndpointFilterFactoryContext context, EndpointFilterDelegate next)
    {
        var enums = context.MethodInfo.GetParameters()
            .Select((p, index) => (Index: index, Name: p.Name!, Type: Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType))
            .Where(p => p.Type.IsEnum)
            .ToArray();
        if (enums.Length == 0) return next;

        return invocation =>
        {
            Dictionary<string, string[]>? errors = null;
            foreach (var (index, name, type) in enums)
                if (invocation.Arguments[index] is { } value && !Enum.IsDefined(type, value))
                    (errors ??= [])[JsonNamingPolicy.CamelCase.ConvertName(name)] = ["The value is not valid."];
            return errors is null ? next(invocation) : ValueTask.FromResult<object?>(TypedResults.ValidationProblem(errors));
        };
    }
}
