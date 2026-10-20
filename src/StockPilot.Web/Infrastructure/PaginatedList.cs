using Microsoft.EntityFrameworkCore;

namespace StockPilot.Web.Infrastructure;

/// <summary>Simple server-side pagination wrapper.</summary>
public class PaginatedList<T> : List<T>, IPaginatedMetadata
{
    public int PageIndex { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageIndex > 1;
    public bool HasNextPage => PageIndex < TotalPages;
    public int FirstItemIndex => TotalCount == 0 ? 0 : (PageIndex - 1) * PageSize + 1;
    public int LastItemIndex => Math.Min(PageIndex * PageSize, TotalCount);

    public PaginatedList(List<T> items, int totalCount, int pageIndex, int pageSize)
    {
        TotalCount = totalCount;
        PageIndex = pageIndex;
        PageSize = pageSize;
        AddRange(items);
    }

    public static async Task<PaginatedList<T>> CreateAsync(
        IQueryable<T> source,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        pageIndex = Math.Max(1, pageIndex);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var count = await source.CountAsync(cancellationToken);
        var items = await source
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PaginatedList<T>(items, count, pageIndex, pageSize);
    }

    public static PaginatedList<T> Create(IReadOnlyList<T> allItems, int pageIndex, int pageSize)
    {
        pageIndex = Math.Max(1, pageIndex);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var items = allItems.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList();
        return new PaginatedList<T>(items, allItems.Count, pageIndex, pageSize);
    }
}
