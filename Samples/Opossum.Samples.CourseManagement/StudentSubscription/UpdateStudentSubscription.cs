using Opossum.Core;
using Opossum.Extensions;
using Opossum.Mediator;
using Opossum.Samples.CourseManagement.StudentRegistration;
using Tier = Opossum.Samples.CourseManagement.EnrollmentTier.EnrollmentTier;

namespace Opossum.Samples.CourseManagement.StudentSubscription;

public sealed record UpdateStudentSubscriptionRequest(Guid StudentId, Tier EnrollmentTier);
public sealed record UpdateStudentSubscriptionCommand(Guid StudentId, Tier EnrollmentTier);
public sealed record StudentSubscriptionUpdatedEvent(Guid StudentId, Tier EnrollmentTier) : IEvent;

public static class Endpoint
{
    public static void MapUpdateStudentSubscriptionEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/students/subscription", async (
            [FromBody] UpdateStudentSubscriptionRequest request,
            [FromServices] IMediator mediator) =>
        {
            var command = new UpdateStudentSubscriptionCommand(
                StudentId: request.StudentId,
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
        
        SequencedEvent sequencedEvent = new StudentSubscriptionUpdatedEvent(
            StudentId: command.StudentId,
            EnrollmentTier: command.EnrollmentTier)
            .ToDomainEvent()
            .WithTag("studentId", command.StudentId.ToString())            
            .WithTimestamp(DateTimeOffset.UtcNow);

        await eventStore.AppendAsync(sequencedEvent);

        return CommandResult.Ok();
    }
}