using Opossum.Core;
using Opossum.Exceptions;
using Opossum.Extensions;
using Opossum.Samples.CourseManagement.Events;

namespace Opossum.Samples.CourseManagement.CourseAggregate;

/// <summary>
/// Minimal API endpoints for the Event-Sourced Aggregate sample.
/// </summary>
/// <remarks>
/// <para>
/// These endpoints cover the same course-management domain as the Decision Model endpoints
/// (<c>POST /courses</c>, <c>PATCH /courses/{id}/student-limit</c>,
/// <c>POST /courses/{id}/enrollments</c>), but use the Event-Sourced Aggregate pattern
/// described at <see href="https://dcb.events/examples/event-sourced-aggregate/#dcb-approach"/>.
/// </para>
/// <para>
/// <b>You do not need both sets of endpoints in a real application.</b>
/// The sample includes both intentionally so you can compare the two patterns side by side.
/// Pick one style and apply it consistently across your domain.
/// </para>
/// <para><b>Retry pattern:</b> when <see cref="CourseAggregateRepository.SaveAsync"/> detects
/// a concurrent write it throws <see cref="AppendConditionFailedException"/>. The handlers
/// below catch that exception, reload the aggregate (getting the updated state), and reapply
/// the command — up to <see cref="MaxRetries"/> times. On the final attempt the exception is
/// not caught and propagates to the global exception handler, which maps it to HTTP 409.
/// </para>
/// </remarks>
public static class CourseAggregateEndpoints
{
    private const int MaxRetries = 3;

    public static void MapCourseAggregateEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/courses/aggregate", CreateCourseAsync)
            .WithName("CreateCourseAggregate")
            .WithTags("Aggregate (Event-Sourced)");

        app.MapPatch("/courses/aggregate/{courseId:guid}/capacity", ChangeCapacityAsync)
            .WithName("ChangeCourseAggregateCapacity")
            .WithTags("Aggregate (Event-Sourced)");

        app.MapPost("/courses/aggregate/{courseId:guid}/subscriptions", SubscribeStudentAsync)
            .WithName("SubscribeStudentToAggregateCourse")
            .WithTags("Aggregate (Event-Sourced)");
    }

    private static async Task<IResult> CreateCourseAsync(
        [FromBody] CreateCourseAggregateRequest request,
        [FromServices] CourseAggregateRepository repository)
    {
        var courseId = Guid.NewGuid();
        var aggregate = CourseAggregate.Create(courseId, request.Name, request.Description, request.MaxStudents);

        // AppendCondition.AfterSequencePosition = null (Version == 0) means "fail if any
        // event for this courseId already exists" — prevents duplicate creation.
        // With a freshly generated Guid this practically never trips, but the guard is
        // correct and required. AppendConditionFailedException propagates to the global
        // exception handler → HTTP 409.
        await repository.SaveAsync(aggregate);
        return Results.Created($"/courses/aggregate/{courseId}", new { id = courseId });
    }

    private static async Task<IResult> ChangeCapacityAsync(
        Guid courseId,
        [FromBody] ChangeCapacityRequest request,
        [FromServices] CourseAggregateRepository repository)
    {
        // Each iteration: load → apply command → save.
        // If a concurrent write invalidates the aggregate's Version between load and save,
        // AppendConditionFailedException is caught (while not on the last attempt) and the
        // loop reloads fresh state. On the last attempt the exception propagates → HTTP 409.
        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            var aggregate = await repository.LoadAsync(courseId);
            if (aggregate is null)
                return Results.NotFound($"Course '{courseId}' does not exist.");

            try
            {
                aggregate.ChangeCapacity(request.NewCapacity);
                await repository.SaveAsync(aggregate);
                return Results.Ok();
            }
            catch (InvalidOperationException ex)
            {
                // Business rule violated by the aggregate — not a concurrency issue, no retry.
                return Results.BadRequest(ex.Message);
            }
            catch (AppendConditionFailedException) when (attempt < MaxRetries - 1)
            {
                // Concurrent write detected — reload with fresh state and try again.
            }
        }

        // Unreachable: on the final attempt AppendConditionFailedException is not caught by
        // the when-guard above and propagates out of the method → global handler → HTTP 409.
        throw new UnreachableException();
    }

    private static async Task<IResult> SubscribeStudentAsync(
        Guid courseId,
        [FromBody] SubscribeStudentRequest request,
        [FromServices] CourseAggregateRepository repository,
        [FromServices] IEventStore eventStore)
    {
        // Validate student existence before touching the aggregate — the aggregate only enforces
        // course-level invariants (capacity). Cross-entity validation stays at the endpoint level,
        // consistent with how EnrollStudentToCourseCommand handles this.
        var studentQuery = Query.FromItems(new QueryItem
        {
            EventTypes = [nameof(StudentRegisteredEvent)],
            Tags = [new Tag("studentId", request.StudentId.ToString())]
        });
        var studentEvents = await eventStore.ReadAsync(studentQuery);
        if (studentEvents.Length == 0)
            return Results.BadRequest($"Student '{request.StudentId}' is not registered.");

        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            var aggregate = await repository.LoadAsync(courseId);
            if (aggregate is null)
                return Results.NotFound($"Course '{courseId}' does not exist.");

            try
            {
                aggregate.SubscribeStudent(request.StudentId);
                await repository.SaveAsync(aggregate);
                return Results.Created(
                    $"/courses/aggregate/{courseId}/subscriptions/{request.StudentId}",
                    new { courseId, studentId = request.StudentId });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
            catch (AppendConditionFailedException) when (attempt < MaxRetries - 1)
            {
                // Concurrent write detected — reload with fresh state and try again.
            }
        }

        throw new UnreachableException();
    }
}

public sealed record CreateCourseAggregateRequest(string Name, string Description, int MaxStudents);
public sealed record ChangeCapacityRequest(int NewCapacity);
public sealed record SubscribeStudentRequest(Guid StudentId);
