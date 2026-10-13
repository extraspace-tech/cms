namespace ExtraSpace.Api.Platform.Data;

/// <summary>
/// Changes made during an admin write get an audit_log row in the same save (S0 spec §6.3; written by Features/Admin).
/// An audited entity needs a <c>Guid Id</c> key: it becomes <c>audit_log.entity_id</c>.
/// </summary>
public interface IAudited;

/// <summary><c>created_by_staff_id</c> / <c>updated_by_staff_id</c> (S0 spec §5), filled during admin writes.</summary>
public interface IHasStaffStamps
{
    Guid? CreatedByStaffId { get; set; }
    Guid? UpdatedByStaffId { get; set; }
}

/// <summary>Keeps a property's values out of audit_log (secrets, large blobs).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotAuditedAttribute : Attribute;
