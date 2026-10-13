using Microsoft.Extensions.Options;

namespace ExtraSpace.Api.Platform.Http;

/// <summary>
/// The admin UI is a single-page app built into wwwroot/admin. Static files (step 7) serve its files; this fallback
/// (step 15) answers every other page address with index.html so a reload of /admin/staff works.
/// </summary>
internal static class AdminSpa
{
    // style-src needs 'unsafe-inline': the UI libraries position menus and toasts with inline styles. Scripts stay strict.
    public const string Csp =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; " +
        "connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    private const string AssetCache = "public, max-age=31536000, immutable";

    /// <summary>A page or file of the UI: under /admin, but not the admin API and not the jobs dashboard.</summary>
    public static bool IsPage(PathString path) =>
        path.StartsWithSegments("/admin") && !path.StartsWithSegments("/admin/api") && !path.StartsWithSegments("/admin/jobs");

    // Vite puts a content hash in every name under assets/, so those never change; index.html must be re-checked.
    public static void ConfigureStaticFiles(StaticFileOptions options) =>
        options.OnPrepareResponse = context => context.Context.Response.Headers.CacheControl =
            context.Context.Request.Path.StartsWithSegments("/admin/assets") ? AssetCache : "no-cache";

    // :nonfile keeps a missing /admin/assets/x.js a 404. MapFallback alone matches every method, which would turn the
    // API's 405 for a wrong method into a 404; the method metadata keeps it to GET and HEAD.
    public static IEndpointConventionBuilder MapAdminSpa(this IEndpointRouteBuilder app) =>
        app.MapFallback("/admin/{*path:nonfile}", ServeIndexAsync)
            .WithMetadata(new HttpMethodMetadata([HttpMethods.Get, HttpMethods.Head])).AllowAnonymous().ExcludeFromDescription();

    private static async Task ServeIndexAsync(HttpContext http, IOptions<StaticFileOptions> staticFiles, IWebHostEnvironment environment)
    {
        var request = http.Request;
        var index = IsPage(request.Path)
            ? (staticFiles.Value.FileProvider ?? environment.WebRootFileProvider).GetFileInfo("admin/index.html")
            : null;
        if (index is not { Exists: true })
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;   // UseStatusCodePages writes the problem body
            return;
        }

        http.Response.ContentType = "text/html; charset=utf-8";
        http.Response.ContentLength = index.Length;
        http.Response.Headers.CacheControl = "no-cache";
        if (HttpMethods.IsGet(request.Method)) await http.Response.SendFileAsync(index, http.RequestAborted);
    }
}
