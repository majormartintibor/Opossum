using Opossum.Core;
using Opossum.DecisionModel;
using Opossum.Exceptions;
using Opossum.Extensions;
using Opossum.Mediator;
using Opossum.Samples.CourseManagement.Events;

namespace Opossum.Samples.CourseManagement.ExamRegistration;

public sealed record RedeemExamRegistrationTokenRequest(Guid StudentId);
public sealed record RedeemExamRegistrationTokenCommand(Guid TokenId, Guid StudentId);

public static class RedeemExamRegistrationTokenEndpoint
{
    public static void MapRedeemExamRegistrationTokenEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/exams/registration-tokens/{tokenId:guid}/redeem", async (
            Guid tokenId,
            [FromBody] RedeemExamRegistrationTokenRequest request,
            [FromServices] IMediator mediator) =>
        {
            var command = new RedeemExamRegistrationTokenCommand(
                TokenId: tokenId,
                StudentId: request.StudentId);

            var result = await mediator.InvokeAsync<CommandResult>(command);

            if (!result.Success)
                return Results.BadRequest(result.ErrorMessage);

            return Results.Ok();
        })
        .WithName("RedeemExamRegistrationToken")
        .WithTags("Exam Registration (Opt-In Token Pattern)")
        .WithSummary("Redeem an exam registration token (student)")
        .WithDescription(
            "Demonstrates the DCB 'Opt-In Token' pattern — redeem step. " +
            "The student submits the server-generated token to register for the exam. " +
            "The ExamTokenState projection validates the token in a single ephemeral read: " +
            "NotIssued → not found, Revoked → revoked, Redeemed → already used. " +
            "No persistent 'valid tokens' table is maintained — DCB replaces it entirely.");
    }
}

public sealed class RedeemExamRegistrationTokenCommandHandler
{
    public async Task<CommandResult> HandleAsync(
        RedeemExamRegistrationTokenCommand command,
        IEventStore eventStore)
    {
        try
        {
            return await eventStore.ExecuteDecisionAsync(
                (store, ct) => TryRedeemTokenAsync(command, store, ct));
        }
        catch (AppendConditionFailedException)
        {
            return CommandResult.Fail(
                "Failed to redeem token due to concurrent updates. Please try again.");
        }
    }

    private static async Task<CommandResult> TryRedeemTokenAsync(
        RedeemExamRegistrationTokenCommand command,
        IEventStore eventStore,
        CancellationToken cancellationToken)
    {
        var model = await eventStore.BuildDecisionModelAsync(
            ExamRegistrationTokenProjections.TokenStatus(command.TokenId),
            cancellationToken);

        return model.State.Status switch
        {
            ExamTokenStatus.NotIssued => CommandResult.Fail("Exam registration token not found."),
            ExamTokenStatus.Revoked   => CommandResult.Fail("Exam registration token has been revoked."),
            ExamTokenStatus.Redeemed  => CommandResult.Fail("Exam registration token has already been used."),
            _ => await AppendRedemptionAsync(command, eventStore, model.State, model.AppendCondition, cancellationToken)
        };
    }

    private static async Task<CommandResult> AppendRedemptionAsync(
        RedeemExamRegistrationTokenCommand command,
        IEventStore eventStore,
        ExamTokenState tokenState,
        AppendCondition appendCondition,
        CancellationToken cancellationToken)
    {
        NewEvent newEvent = new ExamRegistrationTokenRedeemedEvent(
                TokenId: command.TokenId,
                ExamId: tokenState.ExamId,
                StudentId: command.StudentId)
            .ToDomainEvent()
            .WithTag("examToken", command.TokenId.ToString())
            .WithTag("examId", tokenState.ExamId.ToString())
            .WithTag("studentId", command.StudentId.ToString())
            .WithTimestamp(DateTimeOffset.UtcNow);

        await eventStore.AppendAsync(newEvent, appendCondition, cancellationToken);

        return CommandResult.Ok();
    }
}
