using Opossum.Core;
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

        var appendCondition = new AppendCondition
        {
            AfterSequencePosition = events.Length > 0 ? events.Max(e => e.Position) : null,
            FailIfEventsMatch = enrollmentQuery // Use the SAME query that built our decision model
        };

        // Append with Dynamic Consistency Boundary (DCB) spanning multiple aggregates
        // 
        // This is a complex DCB scenario where the consistency boundary includes:
        //   1. Course aggregate (capacity limit)
        //   2. Student aggregate (enrollment limit per tier)
        //   3. Course-Student relationship (no duplicate enrollments)
        // 
        // CRITICAL DCB PATTERN:
        // =====================
        // We use the SAME query for both:
        // A) Reading events to build our decision model (line 57)
        // B) The FailIfEventsMatch append condition (line 110)
        // 
        // This ensures that if ANY event matching our query is added between our read
        // and our append, the append will fail with ConcurrencyException.
        // 
        // AppendCondition Components:
        // 
        // A) AfterSequencePosition (Optimistic Concurrency):
        //    - We read all relevant events up to position X
        //    - We built our aggregate state based on those events
        //    - Condition ensures NO new events exist after position X
        //    - If new events exist → append fails ❌
        // 
        // B) FailIfEventsMatch (Decision Model Staleness Detection):
        //    - Uses the SAME query (enrollmentQuery) that we used to read
        //    - Checks: "Are there any events matching THIS query after position X?"
        //    - This is the KEY to DCB: only events relevant to THIS decision cause conflicts
        //    - Events outside our query (e.g., other courses) don't cause false conflicts
        // 
        // Why BOTH conditions are needed:
        // - AfterSequencePosition alone: would need to check ALL events (too broad)
        // - FailIfEventsMatch alone: doesn't know what position we read from
        // - Together: "Fail if events matching MY query were added after MY read"
        // 
        // Race conditions prevented:
        // 
        //   Scenario 1: Course capacity exceeded
        //     Thread A: Read → 9/10 enrolled, enroll student → 10/10 ✅
        //     Thread B: Read → 9/10 enrolled, enroll student → would be 11/10 ❌
        //     → Thread B's append fails because Thread A's StudentEnrolledToCourseEvent
        //       matches enrollmentQuery (has courseId tag) and was added after Thread B's read
        // 
        //   Scenario 2: Student enrollment limit exceeded
        //     Thread A: Read → student has 2/5 courses, enroll → 3/5 ✅
        //     Thread B: Read → student has 2/5 courses, enroll → would be 4/5 ❌
        //     → Same protection as Scenario 1 (StudentEnrolledToCourseEvent has studentId tag)
        // 
        //   Scenario 3: Duplicate enrollment
        //     Thread A: Read → not enrolled, enroll → success ✅
        //     Thread B: Read → not enrolled, enroll → duplicate ❌
        //     → Thread B's append fails because Thread A's StudentEnrolledToCourseEvent
        //       matches enrollmentQuery (has both courseId AND studentId tags)
        // 
        // If append fails (AppendConditionFailedException), the retry logic (lines 23-44)
        // will re-read events, rebuild aggregate, re-check invariants, and try again with
        // updated conditions. This optimistic concurrency pattern handles expected conflicts
        // gracefully without requiring distributed locks or two-phase commit.
        // 
        // This will throw AppendConditionFailedException if there's a conflict
        await eventStore.AppendAsync(sequencedEvent, appendCondition);

        return CommandResult.Ok();
    }
}
