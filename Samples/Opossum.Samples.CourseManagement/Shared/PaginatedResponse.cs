namespace Opossum.Samples.CourseManagement.Shared;

/// <summary>
/// Generic paginated response wrapper for query results.
/// </summary>
public record PaginatedResponse<T>
{
    public required List<T> Items { get; init; }
    public required int TotalCount { get; init; }
    public required int PageNumber { get; init; }
    public required int PageSize { get; init; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}

/// <summary>
/// Base query parameters for pagination.
/// </summary>
public record PaginationQuery
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;

    public int PageNumber { get; init; } = 1;
    
    private int _pageSize = DefaultPageSize;
    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = value > MaxPageSize ? MaxPageSize : (value < 1 ? DefaultPageSize : value);
    }
}
