using Opossum.Core;
using Opossum.DecisionModel;
using Opossum.Extensions;
using Opossum.Mediator;
using Opossum.Samples.CourseManagement.Events;

namespace Opossum.Samples.CourseManagement.CourseAnnouncementRetraction;

public sealed record RetractCourseAnnouncementCommand(Guid CourseId, Guid IdempotencyToken);

public static class Endpoint
{
    public static void MapRetractCourseAnnouncementEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/courses/{courseId:guid}/announcements/{idempotencyToken:guid}/retract", async (
            Guid courseId,
            Guid idempotencyToken,
            [FromServices] IMediator mediator) =>
        {
            var command = new RetractCourseAnnouncementCommand(
                CourseId: courseId,
                IdempotencyToken: idempotencyToken);

            var result = await mediator.InvokeAsync<CommandResult>(command);

            if (!result.Success)
                return Results.BadRequest(result.ErrorMessage);

            return Results.Ok();
        })
        .WithName("RetractCourseAnnouncement")
        .WithTags("Course Announcement (Idempotency Pattern)")
        .WithSummary("Retract a course announcement (frees the idempotency token)")
        .WithDescription(
            "Retracts the announcement identified by its original idempotency token. " +
            "After retraction the token is freed: a subsequent Post request with the same token " +
            "succeeds — the idempotency projection folds Posted (→ true) then Retracted (→ false) " +
            "in sequence order and arrives at 'not used'. No special handling in the Post handler " +
            "is required; the token reuse is a consequence of the event fold.");
    }
}

public sealed class RetractCourseAnnouncementCommandHandler
{
    public async Task<CommandResult> HandleAsync(
        RetractCourseAnnouncementCommand command,
        IEventStore eventStore)
    {
        var model = await eventStore.BuildDecisionModelAsync(
            CourseAnnouncementRetractionProjection.RetractableAnnouncement(command.IdempotencyToken));

        if (model.State is null)
            return CommandResult.Fail("Announcement not found.");

        if (model.State.IsRetracted)
            return CommandResult.Fail("Announcement has already been retracted.");

        NewEvent retractedEvent = new CourseAnnouncementRetractedEvent(
                AnnouncementId: model.State.AnnouncementId,
                CourseId: model.State.CourseId,
                IdempotencyToken: command.IdempotencyToken)
            .ToDomainEvent()
            .WithTag("courseId", model.State.CourseId.ToString())
            .WithTag("idempotency", command.IdempotencyToken.ToString())
            .WithTimestamp(DateTimeOffset.UtcNow);

        await eventStore.AppendAsync(retractedEvent, model.AppendCondition);

        return CommandResult.Ok();
    }
}
