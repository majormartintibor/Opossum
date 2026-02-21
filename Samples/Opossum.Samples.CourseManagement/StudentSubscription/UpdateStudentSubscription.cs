using Opossum.Core;
using Opossum.Extensions;
using Opossum.Mediator;
using Opossum.Samples.CourseManagement.Events;
using Tier = Opossum.Samples.CourseManagement.EnrollmentTier.EnrollmentTier;

namespace Opossum.Samples.CourseManagement.StudentSubscription;

public sealed record UpdateStudentSubscriptionRequest(Tier EnrollmentTier);
public sealed record UpdateStudentSubscriptionCommand(Guid StudentId, Tier EnrollmentTier);

public static class Endpoint
{
    public static void MapUpdateStudentSubscriptionEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPatch("/students/{studentId:guid}/subscription", async (
            Guid studentId,
            [FromBody] UpdateStudentSubscriptionRequest request,
            [FromServices] IMediator mediator) =>
        {
            var command = new UpdateStudentSubscriptionCommand(
                StudentId: studentId,
                EnrollmentTier: request.EnrollmentTier);

            var result = await mediator.InvokeAsync<CommandResult>(command);

            return result.Success ? Results.Ok() : Results.BadRequest(result.ErrorMessage);
        })
        .WithName("UpdateStudentSubscription")
        .WithTags("Commands");
    }
}

public sealed class UpdateStudentSubscriptionCommandHandler()
{
    public async Task<CommandResult> HandleAsync(
        UpdateStudentSubscriptionCommand command,
        IEventStore eventStore)
    {
        // Invariant: Student must exist
        var studentExistsQuery = Query.FromItems(
                new QueryItem
                {
                    Tags = [new Tag { Key = "studentId", Value = command.StudentId.ToString() }],
                    EventTypes = [nameof(StudentRegisteredEvent)]
                });

        var events = await eventStore.ReadAsync(studentExistsQuery, ReadOption.None);
        if (events.Length == 0)
        {
            return CommandResult.Fail($"Student with ID {command.StudentId} does not exist.");
        }

        NewEvent newEvent = new StudentSubscriptionUpdatedEvent(
            StudentId: command.StudentId,
            EnrollmentTier: command.EnrollmentTier)
            .ToDomainEvent()
            .WithTag("studentId", command.StudentId.ToString())            
            .WithTimestamp(DateTimeOffset.UtcNow);

        await eventStore.AppendAsync(newEvent);

        return CommandResult.Ok();
    }
}
