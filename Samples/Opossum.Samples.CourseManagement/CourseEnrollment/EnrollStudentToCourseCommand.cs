using Opossum.Core;
using Opossum.Exceptions;
using Opossum.Extensions;
using Opossum.Samples.CourseManagement.CourseCreation;
using Opossum.Samples.CourseManagement.StudentRegistration;

namespace Opossum.Samples.CourseManagement.CourseEnrollment;

public sealed record EnrollStudentToCourseCommand(Guid CourseId, Guid StudentId);

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

    private static async Task<CommandResult> TryEnrollStudentAsync(
        EnrollStudentToCourseCommand command,
        IEventStore eventStore)
    {
        // Query all relevant events for both course and student
        var enrollmentQuery = command.GetCourseEnrollmentQuery();

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
            FailIfEventsMatch = command.GetFailIfMatchQuery()
        };

        // This will throw AppendConditionFailedException if there's a conflict
        await eventStore.AppendAsync(sequencedEvent, appendCondition);

        return CommandResult.Ok();
    }
}
