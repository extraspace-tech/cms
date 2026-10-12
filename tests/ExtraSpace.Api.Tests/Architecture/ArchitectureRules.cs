using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Mono.Cecil;

namespace ExtraSpace.Api.Tests.Architecture;

/// <summary>The layout rules of S0 spec §3. Each method returns the offending names; empty means the rule holds.</summary>
/// <remarks>
/// The rules read IL with Mono.Cecil instead of NetArchTest: NetArchTest 1.3.2 looks only one nesting level deep, so it
/// misses code inside async lambdas (state machine inside <c>&lt;&gt;c</c>), and it treats string constants as dependencies.
/// Known blind spots (NetArchTest has them too): <c>typeof</c> in an attribute's named arguments, <c>typeof</c> passed
/// through an <c>object</c> attribute parameter, return-value attributes, and function-pointer field types.
/// A handler method in a class that neither maps routes nor ends with <c>Endpoints</c> is not checked as an endpoint.
/// The top-level <c>Program</c> (global namespace) is not checked; it maps groups only, never handlers.
/// </remarks>
public static class ArchitectureRules
{
    /// <summary>Feature X may use feature Y only through <c>Y.Contracts</c> (this also catches <c>db.Set&lt;YEntity&gt;()</c>).</summary>
    public static IReadOnlyList<string> CrossFeatureViolations(Assembly assembly, string root)
    {
        var featuresPrefix = root + ".Features.";
        return Dependencies(assembly)
            .Where(t => FeatureOf(t.Type.Namespace, featuresPrefix) is { } x
                && t.Uses.Any(d => FeatureOf(d.Namespace, featuresPrefix) is { } y && y != x
                    && !IsContracts(d.Namespace, featuresPrefix + y)))
            .Select(t => t.Type.FullName)
            .ToList();
    }

    /// <summary><c>Platform/</c> and <c>Integrations/</c> never depend on <c>Features/</c>.</summary>
    public static IReadOnlyList<string> LayerViolations(Assembly assembly, string root)
    {
        var layers = new Regex($"^{Regex.Escape(root)}\\.(Platform|Integrations)(\\.|$)");
        var features = new Regex(Under(root + ".Features"));
        return Dependencies(assembly)
            .Where(t => layers.IsMatch(t.Type.Namespace) && t.Uses.Any(d => features.IsMatch(d.Namespace)))
            .Select(t => t.Type.FullName)
            .ToList();
    }

    /// <summary>Endpoint classes call a service; they never touch a DbContext or EF Core.</summary>
    /// <remarks>An endpoint class ends with <c>Endpoints</c> (itself or a nested type) or maps routes itself.</remarks>
    public static IReadOnlyList<string> EndpointDataAccess(Assembly assembly, string root)
    {
        var dbContexts = assembly.GetTypes().Where(t => typeof(DbContext).IsAssignableFrom(t)).Select(t => t.FullName!).ToHashSet();
        var under = new Regex(Under(root));
        var ef = new Regex(Under("Microsoft.EntityFrameworkCore"));
        // Every method of this class is a Map* method (MapGet, MapPost, MapGroup...), so using it means mapping routes.
        const string mapMethods = "Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions";
        return Dependencies(assembly)
            .Where(t => under.IsMatch(t.Type.Namespace)
                && (t.Names.Any(n => n.EndsWith("Endpoints", StringComparison.Ordinal)) || t.Uses.Any(d => d.FullName == mapMethods))
                && t.Uses.Any(d => dbContexts.Contains(d.FullName) || ef.IsMatch(d.Namespace)))
            .Select(t => t.Type.FullName)
            .ToList();
    }

    /// <summary><c>ExtraSpace.Engines</c> is pure maths: no EF Core, ASP.NET Core or System.Net.Http.</summary>
    public static IReadOnlyList<string> ForbiddenEngineReferences(Assembly engines) =>
        engines.GetReferencedAssemblies()
            .Select(r => r.Name!)
            .Where(n => n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                || n.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                || n == "System.Net.Http")
            .ToList();

    private sealed record Dep(string Namespace, string FullName);

    /// <summary>Every top-level type with all the types it and its nested types (any depth) use, and the CLR names of all of them.</summary>
    private static List<(Dep Type, HashSet<Dep> Uses, List<string> Names)> Dependencies(Assembly assembly)
    {
        using var module = ModuleDefinition.ReadModule(assembly.Location);
        // Keep other '<' names: a C# `file` type compiles to a top-level `<File>F<hash>__Name` in its own namespace.
        return module.Types
            .Where(t => t.Name != "<Module>")
            .Select(t =>
            {
                var uses = new HashSet<Dep>();
                Collect(t, uses);
                return (DepOf(t), uses, SelfAndNested(t).Select(n => n.FullName.Replace('/', '+')).ToList());
            })
            .ToList();
    }

    private static IEnumerable<TypeDefinition> SelfAndNested(TypeDefinition type) =>
        type.NestedTypes.SelectMany(SelfAndNested).Prepend(type);

    private static void Collect(TypeDefinition type, HashSet<Dep> uses)
    {
        Add(type.BaseType, uses);
        foreach (var i in type.Interfaces) Add(i.InterfaceType, uses);
        Attributes(type, uses);
        Constraints(type, uses);
        foreach (var f in type.Fields)
        {
            Add(f.FieldType, uses);
            Attributes(f, uses);
        }
        foreach (var p in type.Properties)
        {
            Add(p.PropertyType, uses);
            Attributes(p, uses);
        }
        foreach (var e in type.Events) Add(e.EventType, uses);
        foreach (var m in type.Methods)
        {
            AddMethod(m, uses);
            Attributes(m, uses);
            Constraints(m, uses);
            foreach (var p in m.Parameters) Attributes(p, uses);
            if (!m.HasBody) continue;
            foreach (var v in m.Body.Variables) Add(v.VariableType, uses);
            foreach (var h in m.Body.ExceptionHandlers) Add(h.CatchType, uses);
            foreach (var instruction in m.Body.Instructions)
            {
                switch (instruction.Operand)
                {
                    case TypeReference t:
                        Add(t, uses);
                        break;
                    case MethodReference r:
                        AddMethod(r, uses);
                        break;
                    case FieldReference f:
                        Add(f.DeclaringType, uses);
                        Add(f.FieldType, uses);
                        break;
                }
            }
        }
        foreach (var nested in type.NestedTypes) Collect(nested, uses);
    }

    private static void AddMethod(MethodReference method, HashSet<Dep> uses)
    {
        Add(method.DeclaringType, uses);
        Add(method.ReturnType, uses);
        foreach (var p in method.Parameters) Add(p.ParameterType, uses);
        if (method is GenericInstanceMethod generic)
            foreach (var a in generic.GenericArguments) Add(a, uses);
    }

    private static void Constraints(IGenericParameterProvider provider, HashSet<Dep> uses)
    {
        foreach (var g in provider.GenericParameters)
        foreach (var c in g.Constraints)
            Add(c.ConstraintType, uses);
    }

    private static void Attributes(Mono.Cecil.ICustomAttributeProvider provider, HashSet<Dep> uses)
    {
        foreach (var a in provider.CustomAttributes)
        {
            Add(a.AttributeType, uses);
            // Parse the argument blob only for typeof(...) arguments; enum arguments would need their assembly resolved.
            if (!a.Constructor.Parameters.Any(p => p.ParameterType.FullName is "System.Type" or "System.Type[]")) continue;
            foreach (var arg in a.ConstructorArguments)
            {
                if (arg.Value is TypeReference t) Add(t, uses);
                if (arg.Value is CustomAttributeArgument[] items)
                    foreach (var item in items)
                        if (item.Value is TypeReference it) Add(it, uses);
            }
        }
    }

    private static void Add(TypeReference? type, HashSet<Dep> uses)
    {
        switch (type)
        {
            case null or GenericParameter:
                return;
            case GenericInstanceType generic:
                Add(generic.ElementType, uses);
                foreach (var a in generic.GenericArguments) Add(a, uses);
                return;
            case TypeSpecification spec:
                Add(spec.ElementType, uses);
                return;
            default:
                uses.Add(DepOf(type));
                return;
        }
    }

    private static Dep DepOf(TypeReference type)
    {
        var outer = type;
        while (outer.DeclaringType is { } declaring) outer = declaring;
        return new Dep(outer.Namespace, type.FullName.Replace('/', '+'));
    }

    private static string Under(string ns) => $"^{Regex.Escape(ns)}(\\.|$)";

    private static string? FeatureOf(string ns, string featuresPrefix)
    {
        if (!ns.StartsWith(featuresPrefix, StringComparison.Ordinal)) return null;
        var rest = ns[featuresPrefix.Length..];
        var dot = rest.IndexOf('.');
        return dot < 0 ? rest : rest[..dot];
    }

    private static bool IsContracts(string ns, string featureNs) =>
        ns == featureNs + ".Contracts" || ns.StartsWith(featureNs + ".Contracts.", StringComparison.Ordinal);
}
