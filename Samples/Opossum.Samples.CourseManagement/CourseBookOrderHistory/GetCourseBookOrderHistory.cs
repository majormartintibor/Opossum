using Opossum.Core;
using Opossum.Mediator;
using Opossum.Projections;
using Opossum.Samples.CourseManagement.CourseBookOrderHistory;
using Opossum.Samples.CourseManagement.Shared;

namespace Opossum.Samples.CourseManagement.CourseBookOrderHistory;

public sealed record GetCourseBookOrderHistoryQuery(
    Guid? StudentId = null,
    int PageNumber = 1,
    int PageSize = 50,
    CourseBookOrderSortField SortBy = CourseBookOrderSortField.OrderedAt,
    SortOrder SortOrder = SortOrder.Descending
) : PaginationQuery
{
    public new int PageNumber { get; init; } = PageNumber;
    public new int PageSize { get; init; } = PageSize;
}

public static class GetCourseBookOrderHistoryEndpoint
{
    public static void MapGetCourseBookOrderHistoryEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/course-books/orders", async (
            Guid? studentId,
            int pageNumber,
            int pageSize,
            CourseBookOrderSortField sortBy,
            SortOrder sortOrder,
            [FromServices] IMediator mediator) =>
        {
            var query = new GetCourseBookOrderHistoryQuery(
                StudentId: studentId,
                PageNumber: pageNumber > 0 ? pageNumber : 1,
                PageSize: pageSize > 0 ? pageSize : 50,
                SortBy: sortBy,
                SortOrder: sortOrder
            );

            var result = await mediator.InvokeAsync<CommandResult<PaginatedResponse<CourseBookOrderHistoryEntry>>>(query);

            return result.Success
                ? Results.Ok(result.Value)
                : Results.BadRequest(result.ErrorMessage);
        })
        .WithName("GetCourseBookOrderHistory")
        .WithTags("Course Books (Dynamic Price)")
        .WithSummary("List course book order history")
        .WithDescription("Returns all course book orders, optionally filtered by student ID.");
    }
}

public sealed class GetCourseBookOrderHistoryQueryHandler
{
    public async Task<CommandResult<PaginatedResponse<CourseBookOrderHistoryEntry>>> HandleAsync(
        GetCourseBookOrderHistoryQuery query,
        IProjectionStore<CourseBookOrderHistoryEntry> projectionStore)
    {
        IReadOnlyList<CourseBookOrderHistoryEntry> filtered;

        if (query.StudentId.HasValue)
        {
            // Use tag index - only loads orders for the given student
            filtered = await projectionStore.QueryByTagAsync(
                new Tag("StudentId", query.StudentId.Value.ToString()));
        }
        else
        {
            // No filter - load all orders
            filtered = await projectionStore.GetAllAsync();
        }

        var sorted = query.SortBy switch
        {
            CourseBookOrderSortField.StudentId => query.SortOrder == SortOrder.Ascending
                ? filtered.OrderBy(e => e.StudentId)
                : filtered.OrderByDescending(e => e.StudentId),

            _ => query.SortOrder == SortOrder.Ascending
                ? filtered.OrderBy(e => e.OrderedAt)
                : filtered.OrderByDescending(e => e.OrderedAt)
        };

        var list = sorted.ToList();
        var totalCount = list.Count;
        var items = list
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return CommandResult<PaginatedResponse<CourseBookOrderHistoryEntry>>.Ok(new PaginatedResponse<CourseBookOrderHistoryEntry>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        });
    }
}
