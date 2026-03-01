using Opossum.Core;
using Opossum.DecisionModel;
using Opossum.Extensions;
using Opossum.Mediator;
using Opossum.Samples.CourseManagement.Events;

namespace Opossum.Samples.CourseManagement.ExamRegistration;

public sealed record RevokeExamRegistrationTokenCommand(Guid TokenId);

public static class RevokeExamRegistrationTokenEndpoint
{
    public static void MapRevokeExamRegistrationTokenEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapDelete("/exams/registration-tokens/{tokenId:guid}", async (
            Guid tokenId,
            [FromServices] IMediator mediator) =>
        {
            var command = new RevokeExamRegistrationTokenCommand(TokenId: tokenId);

            var result = await mediator.InvokeAsync<CommandResult>(command);

            if (!result.Success)
                return Results.BadRequest(result.ErrorMessage);

            return Results.Ok();
        })
        .WithName("RevokeExamRegistrationToken")
        .WithTags("Exam Registration (Opt-In Token Pattern)")
        .WithSummary("Revoke an exam registration token (instructor)")
        .WithDescription(
            "Revokes an outstanding exam registration token. " +
            "A revoked token cannot be redeemed — subsequent redemption attempts receive " +
            "a 'token has been revoked' error. " +
            "Tokens that have already been redeemed cannot be revoked.");
    }
}

public sealed class RevokeExamRegistrationTokenCommandHandler
{
    public async Task<CommandResult> HandleAsync(
        RevokeExamRegistrationTokenCommand command,
        IEventStore eventStore)
    {
        var model = await eventStore.BuildDecisionModelAsync(
            ExamRegistrationTokenProjections.TokenStatus(command.TokenId));

        return model.State.Status switch
        {
            ExamTokenStatus.NotIssued => CommandResult.Fail("Exam registration token not found."),
            ExamTokenStatus.Revoked   => CommandResult.Fail("Exam registration token has already been revoked."),
            ExamTokenStatus.Redeemed  => CommandResult.Fail("Cannot revoke a token that has already been redeemed."),
            _ => await AppendRevocationAsync(command, eventStore, model.State, model.AppendCondition)
        };
    }

    private static async Task<CommandResult> AppendRevocationAsync(
        RevokeExamRegistrationTokenCommand command,
        IEventStore eventStore,
        ExamTokenState tokenState,
        AppendCondition appendCondition)
    {
        NewEvent newEvent = new ExamRegistrationTokenRevokedEvent(
                TokenId: command.TokenId,
                ExamId: tokenState.ExamId)
            .ToDomainEvent()
            .WithTag("examToken", command.TokenId.ToString())
            .WithTag("examId", tokenState.ExamId.ToString())
            .WithTimestamp(DateTimeOffset.UtcNow);

        await eventStore.AppendAsync(newEvent, appendCondition);

        return CommandResult.Ok();
    }
}
