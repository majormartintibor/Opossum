using Opossum.Core;
using Opossum.Extensions;
using Opossum.Mediator;
using Opossum.Samples.CourseManagement.Events;

namespace Opossum.Samples.CourseManagement.CourseCreation;

public sealed record CreateCourseRequest(string Name, string Description, int MaxStudentCount);
public sealed record CreateCourseCommand(Guid CourseId, string Name, string Description, int MaxStudentCount);

public static class Endpoint
{
    public static void MapCreateCourseEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/courses", async (
            [FromBody] CreateCourseRequest request,
            [FromServices] IMediator mediator) =>
        {
            var courseId = Guid.NewGuid();
            var command = new CreateCourseCommand(courseId, request.Name, request.Description, request.MaxStudentCount);
            
            var commandResult = await mediator.InvokeAsync<CommandResult>(command);
            
            if (!commandResult.Success)
            {
                return Results.BadRequest(commandResult.ErrorMessage);
            }
            return Results.Created($"/courses/{courseId}", new { id = courseId });
        })
        .WithName("CreateCourse")
        .WithTags("Commands");
    }
}

public sealed class CreateCourseCommandHandler()
{
    public async Task<CommandResult> HandleAsync(
        CreateCourseCommand command,
        IEventStore eventStore)
    {
        SequencedEvent sequencedEvent = new CourseCreatedEvent(command.CourseId, command.Name, command.Description, command.MaxStudentCount)
            .ToDomainEvent()
            .WithTag("courseId", command.CourseId.ToString())
            .WithTimestamp(DateTimeOffset.UtcNow);
        
        await eventStore.AppendAsync(sequencedEvent);

        return new CommandResult(Success: true);
    }
}
