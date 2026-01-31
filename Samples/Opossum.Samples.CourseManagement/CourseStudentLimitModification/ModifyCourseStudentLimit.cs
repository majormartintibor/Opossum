using Opossum.Core;
using Opossum.Extensions;
using Opossum.Mediator;
using Opossum.Samples.CourseManagement.CourseCreation;

namespace Opossum.Samples.CourseManagement.CourseStudentLimitModification;

public sealed record ModifyCourseStudentLimitRequest(int NewMaxStudentCount);
public sealed record ModifyCourseStudentLimitCommand(Guid CourseId, int NewMaxStudentCount);
public sealed record CourseStudentLimitModifiedEvent(Guid CourseId, int NewMaxStudentCount) : IEvent;

public static class Endpoint
{
    public static void MapModifyCourseStudentLimitEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPatch("/courses/{courseId:guid}/student-limit", async (
            Guid courseId,
            [FromBody] ModifyCourseStudentLimitRequest request,
            [FromServices] IMediator mediator) =>
        {
            var command = new ModifyCourseStudentLimitCommand(
                CourseId: courseId,
                NewMaxStudentCount: request.NewMaxStudentCount);

            var result = await mediator.InvokeAsync<CommandResult>(command);

            return result.Success ? Results.Ok() : Results.BadRequest(result.ErrorMessage);
        })
        .WithName("ModifyCourseStudentLimit")
        .WithTags("Commands");
    }
}

public sealed class ModifyCourseStudentLimitCommandHandler()
{
    public async Task<CommandResult> HandleAsync(
        ModifyCourseStudentLimitCommand command,
        IEventStore eventStore)
    {
        // Invariant: Course must exist
        var courseExistsQuery = Query.FromItems(
                new QueryItem
                {
                    Tags = [new Tag { Key = "courseId", Value = command.CourseId.ToString() }],
                    EventTypes = [nameof(CourseCreatedEvent)]
                });

        var events = await eventStore.ReadAsync(courseExistsQuery, ReadOption.None);
        if (events.Length == 0)
        {
            return CommandResult.Fail($"Course with ID {command.CourseId} does not exist.");
        }

        // Validation: MaxStudentCount must be positive
        if (command.NewMaxStudentCount <= 0)
        {
            return CommandResult.Fail("Maximum student count must be greater than zero.");
        }

        SequencedEvent sequencedEvent = new CourseStudentLimitModifiedEvent(
            CourseId: command.CourseId,
            NewMaxStudentCount: command.NewMaxStudentCount)
            .ToDomainEvent()
            .WithTag("courseId", command.CourseId.ToString())
            .WithTimestamp(DateTimeOffset.UtcNow);

        await eventStore.AppendAsync(sequencedEvent);

        return CommandResult.Ok();
    }
}
