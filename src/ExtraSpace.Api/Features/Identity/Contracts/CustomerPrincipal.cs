using System.Security.Claims;

namespace ExtraSpace.Api.Features.Identity.Contracts;

internal static class CustomerPrincipal
{
    /// <summary>The signed-in customer's id (JWT <c>sub</c>), or null when the request is anonymous.</summary>
    public static Guid? CustomerId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("sub"), out var id) ? id : null;
}
