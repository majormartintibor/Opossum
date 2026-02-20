using Opossum.Core;
using Opossum.DecisionModel;
using Opossum.Exceptions;
using Opossum.Extensions;
using Opossum.Samples.CourseManagement.Events;

namespace Opossum.Samples.CourseManagement.CourseEnrollment;

public sealed record EnrollStudentToCourseCommand(Guid CourseId, Guid StudentId);

public sealed class EnrollStudentToCourseCommandHandler()
{
    public async Task<CommandResult> HandleAsync(
        EnrollStudentToCourseCommand command,
        IEventStore eventStore)
    {
        try
        {
            return await eventStore.ExecuteDecisionAsync(
                (store, ct) => TryEnrollStudentAsync(command, store));
        }
        catch (AppendConditionFailedException)
        {
            return CommandResult.Fail(
                "Failed to enroll student due to concurrent updates. Please try again.");
        }
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
