using Opossum.Core;
using Opossum.Extensions;
using Opossum.Mediator;

namespace Opossum.Samples.CourseManagement.StudentRegistration;

public sealed record RegisterStudentRequest(string FirstName, string LastName, string Email);
//NOTE: Opossum is currently missing command validation.
public sealed record RegisterStudentCommand(Guid StudentId, string FirstName, string LastName, string Email);
public sealed record StudentRegisteredEvent(
    Guid StudentId, string FirstName, string LastName, string Email) : IEvent;

public static class Endpoint
{
    public static void MapRegisterStudentEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/students", async (
            [FromBody] RegisterStudentRequest request,
            [FromServices] IMediator mediator) =>
        {
            var studentId = Guid.NewGuid();

            var command = new RegisterStudentCommand(
                StudentId: studentId,
                FirstName: request.FirstName,
                LastName: request.LastName,
                Email: request.Email);

            var commandResult = await mediator.InvokeAsync<CommandResult>(command);

            if (!commandResult.Success)
            {
                return Results.BadRequest(commandResult.ErrorMessage);
            }
            return Results.Created($"/students/{studentId}", new { id = studentId });
        })
        .WithName("RegisterStudent")
        .WithTags("Commands");
    }
}

public sealed class RegisterStudentCommandHandler()
{
    public async Task<CommandResult> HandleAsync(
        RegisterStudentCommand command,
        IEventStore eventStore)
    {
        // Validate Student with email does not exist
        var validateEmailNotTakenQuery = Query.FromItems(
                new QueryItem
                {
                    Tags = [new Tag { Key = "studentEmail", Value = command.Email.ToString() }],
                    EventTypes = []
                });

        var emailValidationResult = await eventStore.ReadAsync(validateEmailNotTakenQuery, ReadOption.None);

        if (emailValidationResult.Length != 0)
        {
            return new CommandResult(Success: false, "A user with this email already exists.");
        }

        // Append student registered event using fluent API
        SequencedEvent sequencedEvent = new StudentRegisteredEvent(
                command.StudentId,
                command.FirstName,
                command.LastName,
                command.Email)
            .ToDomainEvent()
            .WithTag("studentId", command.StudentId.ToString())
            .WithTag("studentEmail", command.Email)
            .WithTimestamp(DateTimeOffset.UtcNow);

        // Append to event store with optimistic concurrency control using DCB
        // 
        // Dynamic Consistency Boundary (DCB) Pattern:
        // We enforce email uniqueness across ALL students (the consistency boundary).
        // 
        // Between our validation check and this append, another concurrent request
        // might have registered a student with the same email. The AppendCondition ensures
        // atomicity by re-checking the condition at append time:
        // 
        // 1. If NO events match validateEmailNotTakenQuery → Append succeeds ✅
        // 2. If ANY event matches (email was taken) → Append fails with ConcurrencyException ❌
        // 
        // This prevents the race condition:
        //   Thread A: Check email → available
        //   Thread B: Check email → available  
        //   Thread B: Append event → success
        //   Thread A: Append event → MUST FAIL (email now taken)
        // 
        // The FailIfEventsMatch condition acts as an optimistic lock, ensuring the consistency
        // boundary (unique emails) is maintained without requiring distributed transactions.
        // 
        // If a ConcurrencyException is thrown, the global exception handler will map it to
        // HTTP 409 Conflict with a Problem Details response (see Program.cs).
        await eventStore.AppendAsync(
            sequencedEvent,
            condition: new AppendCondition() { FailIfEventsMatch = validateEmailNotTakenQuery });

        return new CommandResult(Success: true);
    }
}
