using Opossum.Core;

namespace Opossum.IntegrationTests;

/// <summary>
/// Command handler for creating courses
/// </summary>
public class CreateCourseCommandHandler
{
    public async Task<CommandResult> HandleAsync(
        CreateCourseCommand command,
        IEventStore eventStore)
    {
        // Create the CourseCreated event
        var @event = new CourseCreated(command.CourseId, command.MaxCapacity);

        // Build the NewEvent with proper tags
        var newEvent = new NewEvent
        {
            Event = new DomainEvent
            {
                EventType = nameof(CourseCreated),
                Event = @event,
                Tags =
                [
                    new Tag("courseId", command.CourseId.ToString())
                ]
            },
            Metadata = new Metadata
            {
                Timestamp = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid()
            }
        };

        // Append to event store
        await eventStore.AppendAsync([newEvent], condition: null);

        return new CommandResult(Success: true);
    }
}

/// <summary>
/// Command handler for enrolling students in courses.
/// Demonstrates DCB pattern: loads only events needed for THIS decision.
/// </summary>
public class EnrollStudentToCourseCommandHandler
{
    public async Task<CommandResult> HandleAsync(
        EnrollStudentToCourseCommand command,
        IEventStore eventStore)
    {
        // Implement retry logic for optimistic concurrency
        // Higher retry count for high-concurrency scenarios
        const int maxRetries = 10;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            // Step 1: Build DCB aggregate from relevant events
            // Query for events needed to make enrollment decision:
            // - Course events (capacity tracking)
            // - Student events (enrollment count tracking)
            var query = Query.FromItems(
                new QueryItem
                {
                    Tags = [new Tag("courseId", command.CourseId.ToString())],
                    EventTypes =
                    [
                        nameof(CourseCreated),
                        nameof(CourseCapacityUpdatedEvent),
                        nameof(StudentEnrolledToCourseEvent),
                        nameof(StudentUnenrolledFromCourseEvent)
                    ]
                },
                new QueryItem
                {
                    Tags = [new Tag("studentId", command.StudentId.ToString())],
                    EventTypes =
                    [
                        nameof(StudentEnrolledToCourseEvent),
                        nameof(StudentUnenrolledFromCourseEvent)
                    ]
                }
            );

            var events = await eventStore.ReadAsync(query, readOptions: null);

            // Remember the last position for optimistic concurrency control
            var lastPosition = events.Length > 0 ? events[^1].Position : 0;

            // Fold events into aggregate
            var aggregate = BuildAggregate(events, command.CourseId, command.StudentId);

            // Step 2: Validate business rules
            // Check if student is already enrolled in this specific course
            var isAlreadyEnrolled = events.Any(e =>
                e.Event.EventType == nameof(StudentEnrolledToCourseEvent) &&
                e.Event.Event is StudentEnrolledToCourseEvent enrollEvent &&
                enrollEvent.CourseId == command.CourseId &&
                enrollEvent.StudentId == command.StudentId);

            if (isAlreadyEnrolled)
            {
                // Student is already enrolled - this is idempotent, just return success
                // Alternatively, could return an error. For this test, we'll prevent duplicate enrollment.
                return new CommandResult(Success: false, ErrorMessage: "Student is already enrolled in this course");
            }

            if (!aggregate.CanEnrollStudent())
            {
                return new CommandResult(
                    Success: false,
                    ErrorMessage: aggregate.GetEnrollmentFailureReason()
                );
            }

            // Step 3: Create and append event
            var @event = new StudentEnrolledToCourseEvent(command.CourseId, command.StudentId);

            var newEvent = new NewEvent
            {
                Event = new DomainEvent
                {
                    EventType = nameof(StudentEnrolledToCourseEvent),
                    Event = @event,
                    Tags =
                    [
                        new Tag("courseId", command.CourseId.ToString()),
                        new Tag("studentId", command.StudentId.ToString())
                    ]
                },
                Metadata = new Metadata
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    CorrelationId = Guid.NewGuid()
                }
            };

            // Step 4: Create AppendCondition to prevent stale decision models
            // This is CRITICAL for DCB pattern!
            // - AfterSequencePosition: The position we read up to
            // - FailIfEventsMatch: The same query we used to build our decision model
            // Together: Fail ONLY if events matching our query were added AFTER our read
            var appendCondition = new AppendCondition
            {
                AfterSequencePosition = lastPosition,  // Position when we read
                FailIfEventsMatch = query              // Only fail if matching events added after this position
            };

            // Step 5: Append the event with optimistic concurrency control
            try
            {
                await eventStore.AppendAsync([newEvent], appendCondition);
                return new CommandResult(Success: true);
            }
            catch (Exceptions.AppendConditionFailedException) when (attempt < maxRetries - 1)
            {
                // Decision model was stale - retry with fresh read
                await Task.Delay(10 * (attempt + 1)); // Small exponential backoff
                continue; // Retry
            }
            catch (Exceptions.AppendConditionFailedException)
            {
                // Max retries exceeded
                return new CommandResult(Success: false, ErrorMessage: "Concurrency conflict detected");
            }
        }

        // Should never reach here
        return new CommandResult(Success: false, ErrorMessage: "Unexpected error");
    }

    /// <summary>
    /// Build the DCB aggregate by folding events.
    /// This is the same logic as in the test file.
    /// </summary>
    private static CourseEnlistmentAggregate BuildAggregate(
        SequencedEvent[] events,
        Guid courseId,
        Guid studentId)
    {
        // Initialize aggregate with command context (defines the consistency boundary)
        // Note: Using default studentMaxLimit of 5 (from CourseEnlistmentAggregate constructor)
        // In a real system, this would come from configuration or a StudentCreated event
        var aggregate = new CourseEnlistmentAggregate(courseId, studentId);

        // Fold all events over the aggregate
        foreach (var sequencedEvent in events)
        {
            var eventInstance = sequencedEvent.Event.Event;

            aggregate = eventInstance switch
            {
                CourseCreated e => aggregate.Apply(e),
                CourseCapacityUpdatedEvent e => aggregate.Apply(e),
                StudentEnrolledToCourseEvent e => aggregate.Apply(e),
                StudentUnenrolledFromCourseEvent e => aggregate.Apply(e),
                _ => aggregate
            };
        }

        return aggregate;
    }
}

/// <summary>
/// Command handler for registering students
/// </summary>
public class RegisterStudentCommandHandler
{
    public async Task<CommandResult> HandleAsync(
        RegisterStudentCommand command,
        IEventStore eventStore)
    {
        // Create the StudentRegistered event
        var @event = new StudentRegisteredEvent(command.StudentId, command.Name);

        // Build the NewEvent with proper tags
        var newEvent = new NewEvent
        {
            Event = new DomainEvent
            {
                EventType = nameof(StudentRegisteredEvent),
                Event = @event,
                Tags =
                [
                    new Tag("studentId", command.StudentId.ToString())
                ]
            },
            Metadata = new Metadata
            {
                Timestamp = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid()
            }
        };

        // Append to event store
        await eventStore.AppendAsync([newEvent], condition: null);

        return new CommandResult(Success: true);
    }
}
