using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;

namespace ExtraSpace.Api.Features.Admin;

/// <summary>On the /admin/api group: CSRF check and audit context for every signed-in write.</summary>
internal sealed class AdminRequestFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var endpoint = http.GetEndpoint();
        // Anonymous routes (login, dev-login) sign in: a leftover cookie must not make their save an audited admin write.
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null) return await next(context);
        // Fail closed: the group policy should have stopped this request already.
        if (http.User.StaffId() is not { } staffId) throw AdminErrors.Unauthenticated();

        var method = http.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method)) return await next(context);

        if (endpoint?.Metadata.GetMetadata<IAntiforgeryMetadata>() is not { RequiresValidation: false })
        {
            try { await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http); }
            catch (AntiforgeryValidationException) { throw AdminErrors.CsrfFailed(); }
        }
        http.RequestServices.GetRequiredService<AuditContext>()
            .Begin(staffId, $"{method} {(endpoint as RouteEndpoint)?.RoutePattern.RawText}", http.Connection.RemoteIpAddress);
        return await next(context);
    }
}
