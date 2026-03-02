using Opossum.Core;
using Opossum.Mediator;
using Opossum.Projections;
using Opossum.Samples.CourseManagement.CourseBookCatalog;
using Opossum.Samples.CourseManagement.Shared;

namespace Opossum.Samples.CourseManagement.CourseBookCatalog;

public sealed record GetCourseBookCatalogQuery(
    int PageNumber = 1,
    int PageSize = 50,
    CourseBookSortField SortBy = CourseBookSortField.Title,
    SortOrder SortOrder = SortOrder.Ascending
) : PaginationQuery
{
    public new int PageNumber { get; init; } = PageNumber;
    public new int PageSize { get; init; } = PageSize;
}

public static class GetCourseBookCatalogEndpoint
{
    public static void MapGetCourseBookCatalogEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/course-books", async (
            int pageNumber,
            int pageSize,
            CourseBookSortField sortBy,
            SortOrder sortOrder,
            [FromServices] IMediator mediator) =>
        {
            var query = new GetCourseBookCatalogQuery(
                PageNumber: pageNumber > 0 ? pageNumber : 1,
                PageSize: pageSize > 0 ? pageSize : 50,
                SortBy: sortBy,
                SortOrder: sortOrder
            );

            var result = await mediator.InvokeAsync<CommandResult<PaginatedResponse<CourseBookCatalogEntry>>>(query);

            return result.Success
                ? Results.Ok(result.Value)
                : Results.BadRequest(result.ErrorMessage);
        })
        .WithName("GetCourseBookCatalog")
        .WithTags("Course Books (Dynamic Price)")
        .WithSummary("List all course books with current prices")
        .WithDescription("Returns the current course book catalog including the latest price for each book.");
    }
}

public sealed class GetCourseBookCatalogQueryHandler
{
    public async Task<CommandResult<PaginatedResponse<CourseBookCatalogEntry>>> HandleAsync(
        GetCourseBookCatalogQuery query,
        IProjectionStore<CourseBookCatalogEntry> projectionStore)
    {
        var all = await projectionStore.GetAllAsync();

        var sorted = query.SortBy switch
        {
            CourseBookSortField.Author => query.SortOrder == SortOrder.Ascending
                ? all.OrderBy(e => e.Author)
                : all.OrderByDescending(e => e.Author),

            CourseBookSortField.Price => query.SortOrder == SortOrder.Ascending
                ? all.OrderBy(e => e.CurrentPrice)
                : all.OrderByDescending(e => e.CurrentPrice),

            _ => query.SortOrder == SortOrder.Ascending
                ? all.OrderBy(e => e.Title)
                : all.OrderByDescending(e => e.Title)
        };

        var list = sorted.ToList();
        var totalCount = list.Count;
        var items = list
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return CommandResult<PaginatedResponse<CourseBookCatalogEntry>>.Ok(new PaginatedResponse<CourseBookCatalogEntry>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        });
    }
}
