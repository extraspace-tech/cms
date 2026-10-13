using ExtraSpace.Api.Platform.Errors;

namespace ExtraSpace.Api.Features.Admin;

internal static class AdminErrors
{
    public static AppException Unauthenticated() => new(401, ErrorCodes.Unauthenticated, "Sign in again.");
    public static AppException NoAccess() => new(403, ErrorCodes.Forbidden, "Your account has no access to the admin. Ask an administrator.");
    public static AppException OwnerOnly() => new(403, ErrorCodes.Forbidden, "Only an Owner can do this.");
    public static AppException NotFound(string what) => new(404, ErrorCodes.NotFound, $"{what} not found.");
    public static AppException Invalid(string title) => new(400, ErrorCodes.ValidationFailed, title);
    public static AppException CsrfFailed() => new(400, "CSRF_FAILED", "The security token is missing or out of date. Reload and try again.");
    public static AppException LastOwner() => new(409, "LAST_OWNER", "At least one active Owner must remain.");
    public static AppException CannotDeactivateSelf() => new(409, "CANNOT_DEACTIVATE_SELF", "You cannot deactivate yourself.");
    public static AppException SystemRole() => new(409, "SYSTEM_ROLE", "System roles cannot be changed or deleted.");
    public static AppException RoleInUse() => new(409, "ROLE_IN_USE", "Staff still hold this role. Move them to another role first.");
    public static AppException RoleNameTaken() => new(409, "ROLE_NAME_TAKEN", "A role with this name already exists.");
}
