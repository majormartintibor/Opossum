using Opossum.Core;
using Opossum.Exceptions;
using Opossum.Extensions;
using Opossum.Mediator;
using Opossum.Samples.CourseManagement.CourseCreation;
using Opossum.Samples.CourseManagement.CourseStudentLimitModification;
using Opossum.Samples.CourseManagement.EnrollmentTier;
using Opossum.Samples.CourseManagement.StudentRegistration;
using Opossum.Samples.CourseManagement.StudentSubscription;
using Tier = Opossum.Samples.CourseManagement.EnrollmentTier.EnrollmentTier;

namespace Opossum.Samples.CourseManagement.CourseEnrollment;

public sealed record EnrollStudentToCourseRequest(Guid StudentId);
public sealed record EnrollStudentToCourseCommand(Guid CourseId, Guid StudentId);
public sealed record StudentEnrolledToCourseEvent(Guid CourseId, Guid StudentId) : IEvent;

public sealed record CourseEnrollmentAggregate
{
    // Identity (from command - immutable)
    public Guid CourseId { get; private init; }
    public Guid StudentId { get; private init; }

    // Course capacity tracking
    public int CourseMaxCapacity { get; private init; }
    public int CourseCurrentEnrollmentCount { get; private init; }

    // Student enrollment tracking
    public Tier StudentEnrollmentTier { get; private init; }
    public int StudentCurrentCourseEnrollmentCount { get; private init; }

    // Computed properties
    public int StudentMaxCourseEnrollmentLimit => StudentMaxCourseEnrollment.GetMaxCoursesAllowed(StudentEnrollmentTier);
    public bool IsStudentAlreadyEnrolledInThisCourse { get; private init; }

    private CourseEnrollmentAggregate() { }

    public CourseEnrollmentAggregate(Guid courseId, Guid studentId) 
    { 
        CourseId = courseId;
        StudentId = studentId;
        StudentEnrollmentTier = Tier.Basic; // Default tier
    }

    public CourseEnrollmentAggregate Apply(object @event) => @event switch
    {
        // Course events - track course capacity
        CourseCreatedEvent created when created.CourseId == CourseId =>
            this with { CourseMaxCapacity = created.MaxStudentCount },

        CourseStudentLimitModifiedEvent limitModified when limitModified.CourseId == CourseId =>
            this with { CourseMaxCapacity = limitModified.NewMaxStudentCount },

        // Student events - track student tier
        StudentRegisteredEvent registered when registered.StudentId == StudentId =>
            this with { StudentEnrollmentTier = Tier.Basic },

        StudentSubscriptionUpdatedEvent subscriptionUpdated when subscriptionUpdated.StudentId == StudentId =>
            this with { StudentEnrollmentTier = subscriptionUpdated.EnrollmentTier },

        // Enrollment events - track enrollment counts
        StudentEnrolledToCourseEvent enrolled when enrolled.CourseId == CourseId && enrolled.StudentId == StudentId =>
            this with { IsStudentAlreadyEnrolledInThisCourse = true },

        StudentEnrolledToCourseEvent enrolled when enrolled.CourseId == CourseId =>
            this with { CourseCurrentEnrollmentCount = CourseCurrentEnrollmentCount + 1 },

        StudentEnrolledToCourseEvent enrolled when enrolled.StudentId == StudentId =>
            this with { StudentCurrentCourseEnrollmentCount = StudentCurrentCourseEnrollmentCount + 1 },

        _ => this
    };
}

public static class Endpoint
{
    public static void MapEnrollStudentToCourseEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/courses/{courseId:guid}/enrollments", async (
            Guid courseId,
            [FromBody] EnrollStudentToCourseRequest request,
            [FromServices] IMediator mediator) =>
        {
            var command = new EnrollStudentToCourseCommand(
                CourseId: courseId,
                StudentId: request.StudentId);

            var commandResult = await mediator.InvokeAsync<CommandResult>(command);

            if (!commandResult.Success)
            {
                return Results.BadRequest(commandResult.ErrorMessage);
            }

            return Results.Created($"/courses/{courseId}/enrollments/{request.StudentId}", new { courseId, studentId = request.StudentId });
        })
        .WithName("EnrollStudentToCourse")
        .WithTags("Commands");
    }
}

public sealed class EnrollStudentToCourseCommandHandler()
{
    private const int MaxRetryAttempts = 3;
    private const int InitialRetryDelayMs = 50;

    public async Task<CommandResult> HandleAsync(
        EnrollStudentToCourseCommand command,
        IEventStore eventStore)
    {
        int attempt = 0;

        while (attempt < MaxRetryAttempts)
        {
            try
            {
                return await TryEnrollStudentAsync(command, eventStore);
            }
            catch (AppendConditionFailedException)
            {
                attempt++;

                // If we've exhausted all retries, return failure
                if (attempt >= MaxRetryAttempts)
                {
                    return CommandResult.Fail(
                        $"Failed to enroll student after {MaxRetryAttempts} attempts due to concurrent updates. Please try again.");
                }

                // Exponential backoff: 50ms, 100ms, 200ms
                var delayMs = InitialRetryDelayMs * (int)Math.Pow(2, attempt - 1);
                await Task.Delay(delayMs);

                // Loop continues to retry
            }
        }

        // Should never reach here, but just in case
        return CommandResult.Fail("Unexpected error during enrollment.");
    }

    private async Task<CommandResult> TryEnrollStudentAsync(
        EnrollStudentToCourseCommand command,
        IEventStore eventStore)
    {
        // Query all relevant events for both course and student
        var enrollmentQuery = Query.FromItems(
            new QueryItem
            {
                Tags = [new Tag { Key = "courseId", Value = command.CourseId.ToString() }],
                EventTypes = [
                    nameof(CourseCreatedEvent),
                    nameof(CourseStudentLimitModifiedEvent),
                    nameof(StudentEnrolledToCourseEvent)
                ]
            },
            new QueryItem
            {
                Tags = [new Tag { Key = "studentId", Value = command.StudentId.ToString() }],
                EventTypes = [
                    nameof(StudentRegisteredEvent),
                    nameof(StudentSubscriptionUpdatedEvent),
                    nameof(StudentEnrolledToCourseEvent)
                ]
            });

        var events = await eventStore.ReadAsync(enrollmentQuery, ReadOption.None);

        // Invariant: Course must exist
        if (!events.Any(e => e.Event.Event is CourseCreatedEvent created && created.CourseId == command.CourseId))
        {
            return CommandResult.Fail("Course does not exist.");
        }

        // Invariant: Student must be registered
        if (!events.Any(e => e.Event.Event is StudentRegisteredEvent registered && registered.StudentId == command.StudentId))
        {
            return CommandResult.Fail("Student is not registered.");
        }

        // Build aggregate to check business rules
        var aggregate = events
            .OrderBy(e => e.Position)
            .Select(e => e.Event.Event)
            .Aggregate(
                new CourseEnrollmentAggregate(command.CourseId, command.StudentId),
                (current, @event) => current.Apply(@event));

        // Invariant: Student cannot be enrolled in the same course twice
        if (aggregate.IsStudentAlreadyEnrolledInThisCourse)
        {
            return CommandResult.Fail($"Student is already enrolled in this course.");
        }

        // Invariant: Course must not be at capacity
        if (aggregate.CourseCurrentEnrollmentCount >= aggregate.CourseMaxCapacity)
        {
            return CommandResult.Fail($"Course is at maximum capacity ({aggregate.CourseMaxCapacity} students).");
        }

        // Invariant: Student must not exceed their enrollment limit
        if (aggregate.StudentCurrentCourseEnrollmentCount >= aggregate.StudentMaxCourseEnrollmentLimit)
        {
            return CommandResult.Fail($"Student has reached their enrollment limit ({aggregate.StudentMaxCourseEnrollmentLimit} courses for {aggregate.StudentEnrollmentTier} tier).");
        }

        // Create enrollment event
        SequencedEvent sequencedEvent = new StudentEnrolledToCourseEvent(
            CourseId: command.CourseId,
            StudentId: command.StudentId)
            .ToDomainEvent()
            .WithTag("courseId", command.CourseId.ToString())
            .WithTag("studentId", command.StudentId.ToString())
            .WithTimestamp(DateTimeOffset.UtcNow);

        // Append with condition to prevent race conditions
        var appendCondition = new AppendCondition
        {
            AfterSequencePosition = events.Length > 0 ? events.Max(e => e.Position) : null,
            FailIfEventsMatch = Query.FromItems(
                new QueryItem
                {
                    Tags = [
                        new Tag { Key = "courseId", Value = command.CourseId.ToString() },
                        new Tag { Key = "studentId", Value = command.StudentId.ToString() }
                    ],
                    EventTypes = [nameof(StudentEnrolledToCourseEvent)]
                })
        };

        // This will throw AppendConditionFailedException if there's a conflict
        await eventStore.AppendAsync(sequencedEvent, appendCondition);

        return CommandResult.Ok();
    }
}
