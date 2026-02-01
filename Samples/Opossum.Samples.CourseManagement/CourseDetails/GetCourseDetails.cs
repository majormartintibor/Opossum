using Opossum.Core;
using Opossum.Mediator;
using Opossum.Projections;

namespace Opossum.Samples.CourseManagement.CourseDetails;

public sealed record GetCourseDetailsCommand(Guid CourseId);

public static class Endpoint
{
    public static void MapGetCourseDetailsEndpoint(this IEndpointRouteBuilder app)
    {
        // GET /courses/{courseId}/details - Get detailed course information
        app.MapGet("/courses/{courseId:guid}/details", async (
            Guid courseId,
            [FromServices] IMediator mediator) =>
        {
            var command = new GetCourseDetailsCommand(courseId);
            var commandResult = await mediator.InvokeAsync<CommandResult<CourseDetails>>(command);

            if (!commandResult.Success)
            {
                return Results.NotFound(commandResult.ErrorMessage);
            }

            return Results.Ok(commandResult.Value);
        })
        .WithName("GetCourseDetails")
        .WithTags("Queries")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get detailed course information including enrolled students";
            operation.Description = "Returns comprehensive course details with the list of students enrolled in the course.";
            return operation;
        });
    }
}

public sealed class GetCourseDetailsCommandHandler()
{
    public async Task<CommandResult<CourseDetails>> HandleAsync(
        GetCourseDetailsCommand command,
        IProjectionStore<CourseDetails> projectionStore)
    {
        var course = await projectionStore.GetAsync(command.CourseId.ToString());

        if (course == null)
        {
            return CommandResult<CourseDetails>.Fail($"Course with ID {command.CourseId} not found.");
        }

        return CommandResult<CourseDetails>.Ok(course);
    }
}
