using Opossum.Core;
using Opossum.DecisionModel;
using Opossum.Exceptions;
using Opossum.Extensions;
using Opossum.Mediator;
using Opossum.Samples.CourseManagement.Events;

namespace Opossum.Samples.CourseManagement.CourseAnnouncement;

public sealed record PostCourseAnnouncementRequest(string Title, string Body, Guid IdempotencyToken);
public sealed record PostCourseAnnouncementCommand(Guid CourseId, string Title, string Body, Guid IdempotencyToken);

public static class Endpoint
{
    public static void MapPostCourseAnnouncementEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/courses/{courseId:guid}/announcements", async (
            Guid courseId,
            [FromBody] PostCourseAnnouncementRequest request,
            [FromServices] IMediator mediator) =>
        {
            var command = new PostCourseAnnouncementCommand(
                CourseId: courseId,
                Title: request.Title,
                Body: request.Body,
                IdempotencyToken: request.IdempotencyToken);

            var result = await mediator.InvokeAsync<CommandResult<Guid>>(command);

            if (!result.Success)
                return Results.BadRequest(result.ErrorMessage);

            return Results.Created(
                $"/courses/{courseId}/announcements/{result.Value}",
                new { announcementId = result.Value });
        })
        .WithName("PostCourseAnnouncement")
        .WithTags("Course Announcement (Idempotency Pattern)")
        .WithSummary("Post a course announcement (idempotency token required)")
        .WithDescription(
            "Demonstrates the DCB 'Prevent Record Duplication' pattern. " +
            "The client supplies a client-generated idempotency token. " +
            "If the request is retried with the same token the server detects the re-submission " +
            "and rejects it before any event is appended. " +
            "Two different tokens never interfere with each other — their AppendConditions are " +
            "completely independent, scoped only to the idempotency tag.");
    }
}

public sealed class PostCourseAnnouncementCommandHandler
{
    public async Task<CommandResult<Guid>> HandleAsync(
        PostCourseAnnouncementCommand command,
        IEventStore eventStore)
    {
        try
        {
            return await eventStore.ExecuteDecisionAsync(
                (store, ct) => TryPostAnnouncementAsync(command, store, ct));
        }
        catch (AppendConditionFailedException)
        {
            return CommandResult<Guid>.Fail(
                "Failed to post announcement due to concurrent updates. Please try again.");
        }
    }

    private static async Task<CommandResult<Guid>> TryPostAnnouncementAsync(
        PostCourseAnnouncementCommand command,
        IEventStore eventStore,
        CancellationToken cancellationToken)
    {
        var (courseExists, tokenWasUsed, appendCondition) =
            await eventStore.BuildDecisionModelAsync(
                CourseAnnouncementProjections.CourseExists(command.CourseId),
                CourseAnnouncementProjections.IdempotencyTokenWasUsed(command.IdempotencyToken),
                cancellationToken);

        if (!courseExists)
            return CommandResult<Guid>.Fail("Course does not exist.");

        if (tokenWasUsed)
            return CommandResult<Guid>.Fail("Re-submission detected: this request has already been processed.");

        var announcementId = Guid.NewGuid();

        NewEvent newEvent = new CourseAnnouncementPostedEvent(
                AnnouncementId: announcementId,
                CourseId: command.CourseId,
                Title: command.Title,
                Body: command.Body,
                IdempotencyToken: command.IdempotencyToken)
            .ToDomainEvent()
            .WithTag("courseId", command.CourseId.ToString())
            .WithTag("idempotency", command.IdempotencyToken.ToString())
            .WithTimestamp(DateTimeOffset.UtcNow);

        await eventStore.AppendAsync(newEvent, appendCondition, cancellationToken);

        return CommandResult<Guid>.Ok(announcementId);
    }
}
