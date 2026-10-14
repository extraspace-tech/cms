using ExtraSpace.Api.Platform.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace ExtraSpace.Api.Features.Sites.Admin;

internal static class SitesAdminEndpoints
{
    // Reads: any staff (S1 spec §11.3). Writes and sync: sites.manage.
    public static RouteGroupBuilder MapSitesAdminEndpoints(this RouteGroupBuilder admin)
    {
        var sites = admin.MapGroup("").WithTags("Sites");
        sites.MapGet("/sites", ListSites);
        sites.MapGet("/sites/{id:guid}", GetSite);
        sites.MapPost("/sites", CreateSite).RequireAuthorization(SitesPermissions.Manage);
        sites.MapPatch("/sites/{id:guid}", UpdateSite).RequireAuthorization(SitesPermissions.Manage);
        sites.MapDelete("/sites/{id:guid}", DeleteSite).RequireAuthorization(SitesPermissions.Manage);

        var types = admin.MapGroup("").WithTags("Storage types");
        types.MapGet("/storage-types", ListTypes);
        types.MapGet("/storage-types/{id:guid}", GetStorageType);
        types.MapPost("/storage-types", CreateType).RequireAuthorization(SitesPermissions.Manage);
        types.MapPatch("/storage-types/{id:guid}", UpdateType).RequireAuthorization(SitesPermissions.Manage);
        types.MapDelete("/storage-types/{id:guid}", DeleteType).RequireAuthorization(SitesPermissions.Manage);

        var sizes = admin.MapGroup("").WithTags("Unit sizes");
        sizes.MapGet("/unit-sizes", ListSizes);
        sizes.MapGet("/unit-sizes/{id:guid}", GetSize);
        sizes.MapPost("/unit-sizes", CreateSize).RequireAuthorization(SitesPermissions.Manage);
        sizes.MapPatch("/unit-sizes/{id:guid}", UpdateSize).RequireAuthorization(SitesPermissions.Manage);
        sizes.MapDelete("/unit-sizes/{id:guid}", DeleteSize).RequireAuthorization(SitesPermissions.Manage);

        var units = admin.MapGroup("").WithTags("Units");
        units.MapGet("/units", ListUnits);
        units.MapGet("/units/{id:guid}", GetUnit);
        units.MapGet("/unit-sync", SyncStatus);
        units.MapPost("/sites/{id:guid}/unit-sync", StartSync).RequireAuthorization(SitesPermissions.Manage);
        return admin;
    }

    private static async Task<Ok<Paged<SiteListItem>>> ListSites(SiteAdminService service, CancellationToken cancellationToken,
        string? search = null, int page = 1, int pageSize = Paging.DefaultPageSize) =>
        TypedResults.Ok(await service.ListAsync(search, page, pageSize, cancellationToken));

    private static async Task<Ok<SiteResponse>> GetSite(Guid id, SiteAdminService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetAsync(id, cancellationToken));

    private static async Task<Created<SiteResponse>> CreateSite(CreateSiteBody body, SiteAdminService service, CancellationToken cancellationToken) =>
        TypedResults.Created((string?)null, await service.CreateAsync(body, cancellationToken));

    private static async Task<Ok<SiteResponse>> UpdateSite(Guid id, UpdateSiteBody body, SiteAdminService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.UpdateAsync(id, body, cancellationToken));

    private static async Task<NoContent> DeleteSite(Guid id, [FromQuery] uint version, SiteAdminService service, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, version, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<Paged<StorageTypeResponse>>> ListTypes(StorageTypeAdminService service, CancellationToken cancellationToken,
        string? search = null, int page = 1, int pageSize = Paging.DefaultPageSize) =>
        TypedResults.Ok(await service.ListAsync(search, page, pageSize, cancellationToken));

    private static async Task<Ok<StorageTypeResponse>> GetStorageType(Guid id, StorageTypeAdminService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetAsync(id, cancellationToken));

    private static async Task<Created<StorageTypeResponse>> CreateType(CreateStorageTypeBody body, StorageTypeAdminService service, CancellationToken cancellationToken) =>
        TypedResults.Created((string?)null, await service.CreateAsync(body, cancellationToken));

    private static async Task<Ok<StorageTypeResponse>> UpdateType(Guid id, UpdateStorageTypeBody body, StorageTypeAdminService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.UpdateAsync(id, body, cancellationToken));

    private static async Task<NoContent> DeleteType(Guid id, [FromQuery] uint version, StorageTypeAdminService service, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, version, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<Paged<UnitSizeResponse>>> ListSizes(UnitSizeAdminService service, CancellationToken cancellationToken,
        string? search = null, int page = 1, int pageSize = Paging.DefaultPageSize) =>
        TypedResults.Ok(await service.ListAsync(search, page, pageSize, cancellationToken));

    private static async Task<Ok<UnitSizeResponse>> GetSize(Guid id, UnitSizeAdminService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetAsync(id, cancellationToken));

    private static async Task<Created<UnitSizeResponse>> CreateSize(CreateUnitSizeBody body, UnitSizeAdminService service, CancellationToken cancellationToken) =>
        TypedResults.Created((string?)null, await service.CreateAsync(body, cancellationToken));

    private static async Task<Ok<UnitSizeResponse>> UpdateSize(Guid id, UpdateUnitSizeBody body, UnitSizeAdminService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.UpdateAsync(id, body, cancellationToken));

    private static async Task<NoContent> DeleteSize(Guid id, [FromQuery] uint version, UnitSizeAdminService service, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, version, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<Paged<UnitListItem>>> ListUnits(UnitAdminQuery query, CancellationToken cancellationToken,
        string? search = null, Guid? siteId = null, bool? available = null, int page = 1, int pageSize = Paging.DefaultPageSize) =>
        TypedResults.Ok(await query.ListAsync(search, siteId, available, page, pageSize, cancellationToken));

    private static async Task<Ok<UnitResponse>> GetUnit(Guid id, UnitAdminQuery query, CancellationToken cancellationToken) =>
        TypedResults.Ok(await query.GetAsync(id, cancellationToken));

    private static async Task<Ok<List<UnitSyncStatus>>> SyncStatus(UnitAdminQuery query, CancellationToken cancellationToken) =>
        TypedResults.Ok(await query.SyncStatusAsync(cancellationToken));

    private static async Task<Accepted> StartSync(Guid id, UnitSyncBody body, UnitRefreshRequests requests, CancellationToken cancellationToken)
    {
        await requests.StartAsync(id, body.Full, cancellationToken);
        return TypedResults.Accepted((string?)null);
    }
}
