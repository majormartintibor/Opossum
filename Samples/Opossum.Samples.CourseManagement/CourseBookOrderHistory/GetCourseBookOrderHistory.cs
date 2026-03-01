using Opossum.Core;
using Opossum.Mediator;
using Opossum.Projections;
using Opossum.Samples.CourseManagement.CourseBookOrderHistory;

namespace Opossum.Samples.CourseManagement.CourseBookOrderHistory;

public sealed record GetCourseBookOrderHistoryQuery(Guid? StudentId = null);

public static class GetCourseBookOrderHistoryEndpoint
{
    public static void MapGetCourseBookOrderHistoryEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/course-books/orders", async (
            Guid? studentId,
            [FromServices] IMediator mediator) =>
        {
            var result = await mediator.InvokeAsync<CommandResult<IReadOnlyList<CourseBookOrderHistoryEntry>>>(
                new GetCourseBookOrderHistoryQuery(studentId));

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
    public async Task<CommandResult<IReadOnlyList<CourseBookOrderHistoryEntry>>> HandleAsync(
        GetCourseBookOrderHistoryQuery query,
        IProjectionStore<CourseBookOrderHistoryEntry> projectionStore)
    {
        var all = await projectionStore.GetAllAsync();

        var filtered = query.StudentId.HasValue
            ? all.Where(e => e.StudentId == query.StudentId.Value).ToList()
            : all.OrderByDescending(e => e.OrderedAt).ToList();

        return CommandResult<IReadOnlyList<CourseBookOrderHistoryEntry>>.Ok(filtered);
    }
}
