using ExtraSpace.Api.Platform.Errors;

namespace ExtraSpace.Api.Features.Sites;

internal static class SitesErrors
{
    public static AppException NotFound(string what) => new(404, ErrorCodes.NotFound, $"{what} not found.");
    public static AppException Invalid(string title) => new(400, ErrorCodes.ValidationFailed, title);
    public static AppException SiteInUse(string title = "Units still belong to this site.") => new(409, "SITE_IN_USE", title);
    public static AppException StorageTypeInUse(string title = "Units or unit sizes still use this storage type.") => new(409, "STORAGE_TYPE_IN_USE", title);
    public static AppException UnitSizeInUse() => new(409, "UNIT_SIZE_IN_USE", "Units still have this size. Change its area range first.");
}
