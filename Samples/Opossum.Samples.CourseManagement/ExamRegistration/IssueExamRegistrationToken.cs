using Opossum.Core;
using Opossum.DecisionModel;
using Opossum.Extensions;
using Opossum.Mediator;
using Opossum.Samples.CourseManagement.Events;

namespace Opossum.Samples.CourseManagement.ExamRegistration;

public sealed record IssueExamRegistrationTokenRequest(Guid CourseId);
public sealed record IssueExamRegistrationTokenCommand(Guid ExamId, Guid CourseId);

public static class IssueExamRegistrationTokenEndpoint
{
    public static void MapIssueExamRegistrationTokenEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/exams/{examId:guid}/registration-tokens", async (
            Guid examId,
            [FromBody] IssueExamRegistrationTokenRequest request,
            [FromServices] IMediator mediator) =>
        {
            var command = new IssueExamRegistrationTokenCommand(
                ExamId: examId,
                CourseId: request.CourseId);

            var result = await mediator.InvokeAsync<CommandResult<Guid>>(command);

            if (!result.Success)
                return Results.BadRequest(result.ErrorMessage);

            return Results.Created(
                $"/exams/registration-tokens/{result.Value}",
                new { tokenId = result.Value });
        })
        .WithName("IssueExamRegistrationToken")
        .WithTags("Exam Registration (Opt-In Token Pattern)")
        .WithSummary("Issue an exam registration token (instructor)")
        .WithDescription(
            "Demonstrates the DCB 'Opt-In Token' pattern. " +
            "The instructor issues a server-generated, single-use token for a specific exam. " +
            "The token id is returned and must be shared with the intended student out-of-band. " +
            "No persistent token table is required — the event store IS the token registry.");
    }
}

public sealed class IssueExamRegistrationTokenCommandHandler
{
    public async Task<CommandResult<Guid>> HandleAsync(
        IssueExamRegistrationTokenCommand command,
        IEventStore eventStore)
    {
        var (courseExists, appendCondition) = await eventStore.BuildDecisionModelAsync(
            ExamRegistrationTokenProjections.CourseExists(command.CourseId));

        if (!courseExists)
            return CommandResult<Guid>.Fail("Course does not exist.");

        var tokenId = Guid.NewGuid();

        NewEvent newEvent = new ExamRegistrationTokenIssuedEvent(
                TokenId: tokenId,
                ExamId: command.ExamId,
                CourseId: command.CourseId)
            .ToDomainEvent()
            .WithTag("examToken", tokenId.ToString())
            .WithTag("examId", command.ExamId.ToString())
            .WithTag("courseId", command.CourseId.ToString())
            .WithTimestamp(DateTimeOffset.UtcNow);

        await eventStore.AppendAsync(newEvent, appendCondition);

        return CommandResult<Guid>.Ok(tokenId);
    }
}
