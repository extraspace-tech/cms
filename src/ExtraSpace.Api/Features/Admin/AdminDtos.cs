using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace ExtraSpace.Api.Features.Admin;

internal sealed record StaffSession(Guid Id, string Email, string? DisplayName, Guid RoleId, string RoleName, bool IsOwner, IReadOnlyList<string> Permissions);

internal sealed record AdminMeResponse(Guid Id, string Email, string? DisplayName, Guid RoleId, string RoleName, IReadOnlyList<string> Permissions);

internal sealed record CsrfResponse(string Token);

internal sealed record StaffResponse(Guid Id, string Email, string? DisplayName, Guid RoleId, string RoleName, bool Active, DateTime? LastSignInAt, uint Version);

internal sealed record RoleResponse(Guid Id, string Name, IReadOnlyList<string> Permissions, bool IsSystem, int StaffCount, uint Version);

internal sealed record PermissionResponse(string Name, string Description);

internal sealed record AuditItem(Guid Id, DateTime OccurredAt, Guid StaffId, string StaffEmail, string Action, string EntityType, Guid EntityId, JsonElement Changes, string? Ip);

internal sealed record AuditPage(IReadOnlyList<AuditItem> Items, int Total, int Page, int PageSize);

// Request bodies are public: the .NET 10 validation generator skips non-public types.
public sealed class StaffCodeBody
{
    [Required, EmailAddress, MaxLength(254)]
    public string? Email { get; init; }
}

public sealed class StaffVerifyBody
{
    [Required, EmailAddress, MaxLength(254)]
    public string? Email { get; init; }

    [Required, RegularExpression("^[0-9]{6}$")]
    public string? Code { get; init; }
}

public sealed class AddStaffBody
{
    [Required, EmailAddress, MaxLength(254)]
    public string? Email { get; init; }

    [Required]
    public Guid? RoleId { get; init; }

    [MaxLength(200)]
    public string? DisplayName { get; init; }
}

public sealed class UpdateStaffBody
{
    public Guid? RoleId { get; init; }
    public bool? Active { get; init; }

    /// <summary>Null = unchanged; empty = clear.</summary>
    [MaxLength(200)]
    public string? DisplayName { get; init; }

    [Required]
    public uint? Version { get; init; }
}

public sealed class CreateRoleBody
{
    [Required, MinLength(1), MaxLength(100)]
    public string? Name { get; init; }

    [Required]
    public string[]? Permissions { get; init; }
}

public sealed class UpdateRoleBody
{
    [MinLength(1), MaxLength(100)]
    public string? Name { get; init; }

    public string[]? Permissions { get; init; }

    [Required]
    public uint? Version { get; init; }
}
