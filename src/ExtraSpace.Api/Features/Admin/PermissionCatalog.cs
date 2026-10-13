using ExtraSpace.Api.Features.Admin.Contracts;

namespace ExtraSpace.Api.Features.Admin;

internal sealed class PermissionCatalog
{
    public PermissionCatalog(IEnumerable<Permission> permissions)
    {
        All = permissions.OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
        if (All.DistinctBy(p => p.Name).Count() != All.Count) throw new InvalidOperationException("A permission name is registered twice.");
        Names = All.Select(p => p.Name).ToList();
    }

    public IReadOnlyList<Permission> All { get; }
    public IReadOnlyList<string> Names { get; }
    public bool Contains(string name) => Names.Contains(name);
}
