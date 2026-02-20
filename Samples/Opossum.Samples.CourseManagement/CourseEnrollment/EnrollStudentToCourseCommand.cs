using Opossum.Core;
using Opossum.DecisionModel;
using Opossum.Exceptions;
using Opossum.Extensions;
using Opossum.Samples.CourseManagement.Events;

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
            catch (ConcurrencyException) when (attempt < MaxRetryAttempts - 1)
            {
                // Decision model was stale - retry with fresh read
                attempt++;
                var delayMs = InitialRetryDelayMs * (int)Math.Pow(2, attempt - 1);
                await Task.Delay(delayMs);
                // Loop continues to retry
            }
            catch (AppendConditionFailedException) when (attempt < MaxRetryAttempts - 1)
            {
                // Append condition failed - retry with fresh read
                attempt++;
                var delayMs = InitialRetryDelayMs * (int)Math.Pow(2, attempt - 1);
                await Task.Delay(delayMs);
                // Loop continues to retry
            }
            catch (ConcurrencyException)
            {
                // Max retries exhausted with concurrency conflict
                return CommandResult.Fail(
                    $"Failed to enroll student after {MaxRetryAttempts} attempts due to concurrent updates. Please try again.");
            }
            catch (AppendConditionFailedException)
            {
                // Max retries exhausted with append condition failure
                return CommandResult.Fail(
                    $"Failed to enroll student after {MaxRetryAttempts} attempts due to concurrent updates. Please try again.");
            }
        }

        // Should never reach here, but just in case
        return CommandResult.Fail("Unexpected error during enrollment.");
    }

    private static async Task<CommandResult> TryEnrollStudentAsync(
        EnrollStudentToCourseCommand command,
        IEventStore eventStore)
    {
        // Build decision model from three independent projections in a single read.
        // The returned AppendCondition automatically spans all three queries — a concurrent
        // write matching any of them will reject the append and trigger a retry.
        var (courseCapacity, studentLimit, alreadyEnrolled, appendCondition) =
            await eventStore.BuildDecisionModelAsync(
                CourseEnrollmentProjections.CourseCapacity(command.CourseId),
                CourseEnrollmentProjections.StudentEnrollmentLimit(command.StudentId),
                CourseEnrollmentProjections.AlreadyEnrolled(command.CourseId, command.StudentId));

        if (courseCapacity is null)
            return CommandResult.Fail("Course does not exist.");

        if (studentLimit is null)
            return CommandResult.Fail("Student is not registered.");

        if (alreadyEnrolled)
            return CommandResult.Fail("Student is already enrolled in this course.");

        if (courseCapacity.IsFull)
            return CommandResult.Fail($"Course is at maximum capacity ({courseCapacity.MaxCapacity} students).");

        if (studentLimit.IsAtLimit)
            return CommandResult.Fail($"Student has reached their enrollment limit ({studentLimit.MaxAllowed} courses for {studentLimit.Tier} tier).");

        SequencedEvent enrollmentEvent = new StudentEnrolledToCourseEvent(
            CourseId: command.CourseId,
            StudentId: command.StudentId)
            .ToDomainEvent()
            .WithTag("courseId", command.CourseId.ToString())
            .WithTag("studentId", command.StudentId.ToString())
            .WithTimestamp(DateTimeOffset.UtcNow);

        await eventStore.AppendAsync(enrollmentEvent, appendCondition);

        return CommandResult.Ok();
    }
}
