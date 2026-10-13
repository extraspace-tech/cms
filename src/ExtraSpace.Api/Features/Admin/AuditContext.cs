using System.Net;

namespace ExtraSpace.Api.Features.Admin;

/// <summary>Per request: set by AdminRequestFilter for a signed-in admin write, read by <see cref="AuditInterceptor"/>.</summary>
internal sealed class AuditContext
{
    public Guid? StaffId { get; private set; }
    public string Action { get; private set; } = "";
    public IPAddress? Ip { get; private set; }

    public void Begin(Guid staffId, string action, IPAddress? ip) => (StaffId, Action, Ip) = (staffId, action, ip);
}
