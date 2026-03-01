using Opossum.Core;
using Opossum.Mediator;
using Opossum.Projections;
using Opossum.Samples.CourseManagement.CourseBookCatalog;

namespace Opossum.Samples.CourseManagement.CourseBookCatalog;

public sealed record GetCourseBookCatalogQuery;

public static class GetCourseBookCatalogEndpoint
{
    public static void MapGetCourseBookCatalogEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/course-books", async ([FromServices] IMediator mediator) =>
        {
            var result = await mediator.InvokeAsync<CommandResult<IReadOnlyList<CourseBookCatalogEntry>>>(new GetCourseBookCatalogQuery());

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
    public async Task<CommandResult<IReadOnlyList<CourseBookCatalogEntry>>> HandleAsync(
        GetCourseBookCatalogQuery _,
        IProjectionStore<CourseBookCatalogEntry> projectionStore)
    {
        var all = await projectionStore.GetAllAsync();
        var sorted = all.OrderBy(e => e.Title).ToList();
        return CommandResult<IReadOnlyList<CourseBookCatalogEntry>>.Ok(sorted);
    }
}
