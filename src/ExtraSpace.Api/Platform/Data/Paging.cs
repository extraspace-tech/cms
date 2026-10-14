using ExtraSpace.Api.Platform.Errors;
using Microsoft.EntityFrameworkCore;

namespace ExtraSpace.Api.Platform.Data;

public sealed record Paged<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public static class Paging
{
    /// <summary>The demo's list page size (S1 spec §11.1).</summary>
    public const int DefaultPageSize = 20;

    /// <summary>The escape character of <see cref="Contains"/> patterns; pass it to <c>EF.Functions.ILike</c>.</summary>
    public const string Escape = @"\";

    /// <summary>One page of an already ordered query (order before projecting into a positional record).</summary>
    public static async Task<Paged<T>> ToPageAsync<T>(this IQueryable<T> ordered, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (pageSize is < 1 or > 100) throw new AppException(400, ErrorCodes.ValidationFailed, "The page size must be between 1 and 100.");
        if (page < 1 || page > int.MaxValue / pageSize) throw new AppException(400, ErrorCodes.ValidationFailed, "The page is out of range.");
        var total = await ordered.CountAsync(cancellationToken);
        var items = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new Paged<T>(items, total, page, pageSize);
    }

    /// <summary>An ILIKE pattern matching the text anywhere; its own %, _ and \ are literal (escaped with <see cref="Escape"/>).</summary>
    public static string Contains(string text) =>
        "%" + text.Trim().Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";
}
