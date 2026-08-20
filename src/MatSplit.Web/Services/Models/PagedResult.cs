namespace MatSplit.Web.Services.Models;

/// <summary>
/// Result of every paged list query in the service layer. Feeds the
/// <c>ms-pagination</c> control directly.
/// </summary>
public sealed class PagedResult<T>
{
    public PagedResult()
    {
    }

    public PagedResult(IReadOnlyList<T> items, int page, int pageSize, int totalCount)
    {
        Items = items;
        Page = page;
        PageSize = pageSize;
        TotalCount = totalCount;
    }

    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>1-based page number.</summary>
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public int TotalCount { get; init; }

    public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

    public bool HasPreviousPage => Page > 1;

    public bool HasNextPage => Page < TotalPages;

    public bool IsEmpty => Items.Count == 0;

    public static PagedResult<T> Empty(int page = 1, int pageSize = 20) => new([], page, pageSize, 0);
}

/// <summary>
/// Normalises user supplied paging values so services never see nonsense.
/// </summary>
public static class Paging
{
    public const int DefaultPageSize = 20;

    public const int MaxPageSize = 200;

    public static (int Page, int PageSize) Normalize(int page, int pageSize)
    {
        var safePage = page < 1 ? 1 : page;
        var safeSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);
        return (safePage, safeSize);
    }

    public static int Skip(int page, int pageSize) => (page - 1) * pageSize;
}
