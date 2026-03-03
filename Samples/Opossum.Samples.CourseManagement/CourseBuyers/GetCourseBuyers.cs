using Opossum.Core;
using Opossum.Mediator;
using Opossum.Projections;

namespace Opossum.Samples.CourseManagement.CourseBuyers;

public sealed record GetCourseBuyersQuery(Guid CourseId);

public static class GetCourseBuyersEndpoint
{
    public static void MapGetCourseBuyersEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/courses/{courseId:guid}/book-buyers", async (
            Guid courseId,
            [FromServices] IMediator mediator) =>
        {
            var query = new GetCourseBuyersQuery(courseId);
            var result = await mediator.InvokeAsync<CommandResult<CourseBuyersState>>(query);

            return result.Success
                ? Results.Ok(result.Value)
                : Results.NotFound(result.ErrorMessage);
        })
        .WithName("GetCourseBuyers")
        .WithTags("Queries")
        .WithSummary("Get all students who purchased the course's textbook")
        .WithDescription(
            "Returns the course name and all students who have purchased the course's assigned textbook, " +
            "via either individual purchases or cart orders. " +
            "Known limitation: a cart order containing books from multiple courses will only update " +
            "the buyer list for the first courseId tag on the order event. " +
            "Purchases made through the DataSeeder are not affected, as the seeder constrains all " +
            "books in one order to the same course.");
    }
}

public sealed class GetCourseBuyersQueryHandler
{
    public async Task<CommandResult<CourseBuyersState>> HandleAsync(
        GetCourseBuyersQuery query,
        IProjectionStore<CourseBuyersState> projectionStore)
    {
        var state = await projectionStore.GetAsync(query.CourseId.ToString());

        if (state is null)
        {
            return CommandResult<CourseBuyersState>.Fail(
                $"No course buyers data found for course {query.CourseId}.");
        }

        return CommandResult<CourseBuyersState>.Ok(state);
    }
}
