namespace ExtraSpace.Api.Features.Admin;

internal static class AdminPermissions
{
    public const string StaffManage = "staff.manage";
    public const string RolesManage = "roles.manage";
    public const string AuditRead = "audit.read";
    // The jobs dashboard belongs to Platform, which cannot depend on a feature, so Admin declares it.
    public const string JobsManage = "jobs.manage";
}
