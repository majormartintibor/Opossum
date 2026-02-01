using Opossum.Core;
using Opossum.Mediator;
using Opossum.Projections;

namespace Opossum.Samples.CourseManagement.StudentDetails;

public sealed record GetStudentDetailsCommand(Guid StudentId);

public static class Endpoint
{
    public static void MapGetStudentDetailsEndpoint(this IEndpointRouteBuilder app)
    {
        // GET /students/{studentId}/details - Get detailed student information
        app.MapGet("/students/{studentId:guid}/details", async (
            Guid studentId,
            [FromServices] IMediator mediator) =>
        {
            var command = new GetStudentDetailsCommand(studentId);
            var commandResult = await mediator.InvokeAsync<CommandResult<StudentDetails>>(command);

            if (!commandResult.Success)
            {
                return Results.NotFound(commandResult.ErrorMessage);
            }

            return Results.Ok(commandResult.Value);
        })
        .WithName("GetStudentDetails")
        .WithTags("Queries")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get detailed student information including enrolled courses";
            operation.Description = "Returns comprehensive student details with the list of courses they are enrolled in.";
            return operation;
        });
    }
}

public sealed class GetStudentDetailsCommandHandler()
{
    public async Task<CommandResult<StudentDetails>> HandleAsync(
        GetStudentDetailsCommand command,
        IProjectionStore<StudentDetails> projectionStore)
    {
        var student = await projectionStore.GetAsync(command.StudentId.ToString());

        if (student == null)
        {
            return CommandResult<StudentDetails>.Fail($"Student with ID {command.StudentId} not found.");
        }

        return CommandResult<StudentDetails>.Ok(student);
    }
}
