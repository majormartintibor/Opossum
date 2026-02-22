using Opossum.Core;
using Opossum.Mediator;

namespace Opossum.Samples.CourseManagement.CourseEnrollment;

public sealed record EnrollStudentToCourseRequest(Guid StudentId);

public static class Endpoint
{
    public static void MapEnrollStudentToCourseEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/courses/{courseId:guid}/enrollments", async (
            Guid courseId,
            [FromBody] EnrollStudentToCourseRequest request,
            [FromServices] IMediator mediator) =>
        {
            var command = new EnrollStudentToCourseCommand(
                CourseId: courseId,
                StudentId: request.StudentId);

            var commandResult = await mediator.InvokeAsync<CommandResult>(command);

            if (!commandResult.Success)
            {
                return Results.BadRequest(commandResult.ErrorMessage);
            }

            return Results.Created($"/courses/{courseId}/enrollments/{request.StudentId}", new { courseId, studentId = request.StudentId });
        })
        .WithName("EnrollStudentToCourse")
        .WithTags("Commands");
    }
}
