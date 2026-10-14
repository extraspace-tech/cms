using ExtraSpace.Api.Platform.Errors;

namespace ExtraSpace.Api.Features.Sites;

public sealed record SiteCountry(string Code, string Currency, string TimeZone, string TaxLabel);

/// <summary>The three countries of release 1 (S1 spec §1). The currency follows the country; zone and tax label are defaults.</summary>
internal static class SiteCountries
{
    private static readonly Dictionary<string, SiteCountry> All = new(StringComparer.Ordinal)
    {
        ["SG"] = new("SG", "SGD", "Asia/Singapore", "GST"),
        ["MY"] = new("MY", "MYR", "Asia/Kuala_Lumpur", "SST"),
        ["KR"] = new("KR", "KRW", "Asia/Seoul", "VAT"),
    };

    public static SiteCountry Get(string code) =>
        All.TryGetValue(code, out var country) ? country : throw new AppException(400, ErrorCodes.ValidationFailed, $"Unknown country '{code}'.");
}
