using Opossum.Core;
using Opossum.Extensions;
using Opossum.Mediator;
using Opossum.IntegrationTests.Fixtures;

namespace Opossum.IntegrationTests;

public class ExampleTest(OpossumFixture fixture) : IClassFixture<OpossumFixture>
{
    private readonly IMediator _mediator = fixture.Mediator;
    private readonly IEventStore _eventStore = fixture.EventStore;

    [Fact]
    public async Task EnrollStudentToCourse_ShouldCreateEventAndBuildAggregate()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var courseId = Guid.NewGuid();

        // First, create the course with capacity
        var createCourseCommand = new CreateCourseCommand(courseId, MaxCapacity: 30);
        await _mediator.InvokeAsync<CommandResult>(createCourseCommand);

        // Create query for all events related to this enrollment decision
        // Query logic: Get events that have:
        // - courseId tag AND (StudentEnrolled OR StudentUnenrolled event types)
        // - OR studentId tag AND (StudentEnrolled OR StudentUnenrolled event types)
        var enrollmentQuery = Query.FromItems(
            new QueryItem
            {
                Tags = [new Tag("courseId", courseId.ToString())],
                EventTypes = [
                    nameof(StudentEnrolledToCourseEvent),
                    nameof(StudentUnenrolledFromCourseEvent)
                ]
            },
            new QueryItem
            {
                Tags = [new Tag("studentId", studentId.ToString())],
                EventTypes = [
                    nameof(StudentEnrolledToCourseEvent),
                    nameof(StudentUnenrolledFromCourseEvent)
                ]
            }
        );

        // Act - Enroll student to course
        var enrollCommand = new EnrollStudentToCourseCommand(courseId, studentId);
        var result = await _mediator.InvokeAsync<CommandResult>(enrollCommand);

        // Assert - Command succeeded
        Assert.True(result.Success);

        // Assert - Event was persisted
        var events = await _eventStore.ReadAsync(enrollmentQuery);
        Assert.NotEmpty(events);

        var enrollmentEvent = events.Last(); // Get the most recent event
        Assert.Equal(nameof(StudentEnrolledToCourseEvent), enrollmentEvent.Event.EventType);

        var typedEvent = (StudentEnrolledToCourseEvent)enrollmentEvent.Event.Event;
        Assert.Equal(studentId, typedEvent.StudentId);
        Assert.Equal(courseId, typedEvent.CourseId);

        // Assert - Event has proper tags
        Assert.Contains(enrollmentEvent.Event.Tags,
            t => t.Key == "courseId" && t.Value == courseId.ToString());
        Assert.Contains(enrollmentEvent.Event.Tags,
            t => t.Key == "studentId" && t.Value == studentId.ToString());

        // Assert - Aggregate can be built from course events
        var courseQuery = Query.FromTags(new Tag("courseId", courseId.ToString()));
        var courseEvents = await _eventStore.ReadAsync(courseQuery);

        var aggregate = BuildAggregate(courseEvents, courseId, studentId);
        Assert.NotNull(aggregate);
        Assert.Equal(courseId, aggregate.CourseId);
        Assert.Equal(studentId, aggregate.StudentId);
        Assert.Equal(30, aggregate.CourseMaxCapacity);
        Assert.Equal(1, aggregate.CourseCurrentEnrollmentCount);
        Assert.Equal(1, aggregate.StudentCurrentCourseEnrollmentCount); // Student enrolled in 1 course
    }

    [Fact]
    public async Task EnrollStudentToCourse_WhenCourseIsFull_ShouldFail()
    {
        // Arrange
        var courseId = Guid.NewGuid();
        var maxCapacity = 2;

        // Create course with capacity of 2
        var createCourseCommand = new CreateCourseCommand(courseId, maxCapacity);
        await _mediator.InvokeAsync<CommandResult>(createCourseCommand);

        // Enroll first student
        var student1Id = Guid.NewGuid();
        await _mediator.InvokeAsync<CommandResult>(
            new EnrollStudentToCourseCommand(courseId, student1Id));

        // Enroll second student (course now full)
        var student2Id = Guid.NewGuid();
        await _mediator.InvokeAsync<CommandResult>(
            new EnrollStudentToCourseCommand(courseId, student2Id));

        // Act - Try to enroll third student (should fail)
        var student3Id = Guid.NewGuid();
        var result = await _mediator.InvokeAsync<CommandResult>(
            new EnrollStudentToCourseCommand(courseId, student3Id));

        // Assert - Command should fail due to capacity constraint
        Assert.False(result.Success);
        Assert.Equal("Course is at maximum capacity", result.ErrorMessage);

        // Verify only 2 students are enrolled
        var courseQuery = Query.FromTags(new Tag("courseId", courseId.ToString()));
        var courseEvents = await _eventStore.ReadAsync(courseQuery);
        var aggregate = BuildAggregate(courseEvents, courseId, student3Id);

        Assert.Equal(2, aggregate.CourseCurrentEnrollmentCount);
        Assert.Equal(0, aggregate.StudentCurrentCourseEnrollmentCount); // student3 is NOT enrolled
    }

    [Fact]
    public async Task EnrollStudentToCourse_WhenStudentReachedLimit_ShouldFail()
    {
        // Arrange
        var studentId = Guid.NewGuid();

        // Create 6 courses (student limit is 5, so 6th enrollment should fail)
        var course1Id = Guid.NewGuid();
        var course2Id = Guid.NewGuid();
        var course3Id = Guid.NewGuid();
        var course4Id = Guid.NewGuid();
        var course5Id = Guid.NewGuid();
        var course6Id = Guid.NewGuid();

        await _mediator.InvokeAsync<CommandResult>(new CreateCourseCommand(course1Id, 30));
        await _mediator.InvokeAsync<CommandResult>(new CreateCourseCommand(course2Id, 30));
        await _mediator.InvokeAsync<CommandResult>(new CreateCourseCommand(course3Id, 30));
        await _mediator.InvokeAsync<CommandResult>(new CreateCourseCommand(course4Id, 30));
        await _mediator.InvokeAsync<CommandResult>(new CreateCourseCommand(course5Id, 30));
        await _mediator.InvokeAsync<CommandResult>(new CreateCourseCommand(course6Id, 30));

        // Enroll student in first 5 courses
        await _mediator.InvokeAsync<CommandResult>(
            new EnrollStudentToCourseCommand(course1Id, studentId));
        await _mediator.InvokeAsync<CommandResult>(
            new EnrollStudentToCourseCommand(course2Id, studentId));
        await _mediator.InvokeAsync<CommandResult>(
            new EnrollStudentToCourseCommand(course3Id, studentId));
        await _mediator.InvokeAsync<CommandResult>(
            new EnrollStudentToCourseCommand(course4Id, studentId));
        await _mediator.InvokeAsync<CommandResult>(
            new EnrollStudentToCourseCommand(course5Id, studentId));

        // Act - Try to enroll in sixth course (should fail - student at limit)
        var result = await _mediator.InvokeAsync<CommandResult>(
            new EnrollStudentToCourseCommand(course6Id, studentId));

        // Assert - Command should fail due to student enrollment limit
        Assert.False(result.Success);
        Assert.Contains("Student has reached maximum course enrollment limit", result.ErrorMessage);

        // Verify student is only in 5 courses
        var enrollmentQuery = Query.FromItems(
            new QueryItem
            {
                Tags = [new Tag("studentId", studentId.ToString())],
                EventTypes = [
                    nameof(StudentEnrolledToCourseEvent),
                    nameof(StudentUnenrolledFromCourseEvent)
                ]
            }
        );

        var studentEvents = await _eventStore.ReadAsync(enrollmentQuery);
        var aggregate = BuildAggregate(studentEvents, course6Id, studentId);

        Assert.Equal(0, aggregate.CourseCurrentEnrollmentCount); // course6 has 0 students
        Assert.Equal(5, aggregate.StudentCurrentCourseEnrollmentCount); // student in 5 courses
    }

    [Fact]
    public async Task EnrollStudentToCourse_MultipleStudents_ShouldTrackCorrectly()
    {
        // Arrange - Test that the aggregate correctly counts DIFFERENT students in the course
        var courseId = Guid.NewGuid();
        var student1Id = Guid.NewGuid();
        var student2Id = Guid.NewGuid();
        var student3Id = Guid.NewGuid();

        await _mediator.InvokeAsync<CommandResult>(new CreateCourseCommand(courseId, 10));

        // Act - Enroll 3 different students in the same course
        await _mediator.InvokeAsync<CommandResult>(
            new EnrollStudentToCourseCommand(courseId, student1Id));
        await _mediator.InvokeAsync<CommandResult>(
            new EnrollStudentToCourseCommand(courseId, student2Id));
        await _mediator.InvokeAsync<CommandResult>(
            new EnrollStudentToCourseCommand(courseId, student3Id));

        // Assert - Course should have 3 students enrolled
        var courseQuery = Query.FromTags(new Tag("courseId", courseId.ToString()));
        var courseEvents = await _eventStore.ReadAsync(courseQuery);

        // Build aggregate for student1's perspective
        var aggregateForStudent1 = BuildAggregate(courseEvents, courseId, student1Id);
        Assert.Equal(3, aggregateForStudent1.CourseCurrentEnrollmentCount); // 3 students in course
        Assert.Equal(1, aggregateForStudent1.StudentCurrentCourseEnrollmentCount); // student1 in 1 course

        // Build aggregate for student2's perspective
        var aggregateForStudent2 = BuildAggregate(courseEvents, courseId, student2Id);
        Assert.Equal(3, aggregateForStudent2.CourseCurrentEnrollmentCount); // 3 students in course
        Assert.Equal(1, aggregateForStudent2.StudentCurrentCourseEnrollmentCount); // student2 in 1 course
    }

    private static CourseEnlistmentAggregate BuildAggregate(SequencedEvent[] events, Guid courseId, Guid studentId)
    {
        // Initialize aggregate with command context (defines the consistency boundary)
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

// ============================================================================
// COMMAND & EVENT DEFINITIONS
// ============================================================================

public record EnrollStudentToCourseCommand(Guid CourseId, Guid StudentId);
public record CreateCourseCommand(Guid CourseId, int MaxCapacity);

public record CommandResult(bool Success, string? ErrorMessage = null);

// Domain Events
public record CourseCreated(Guid CourseId, int MaxCapacity) : IEvent;
public record CourseCapacityUpdatedEvent(Guid CourseId, int NewCapacity) : IEvent;
public record StudentEnrolledToCourseEvent(Guid CourseId, Guid StudentId) : IEvent;
public record StudentUnenrolledFromCourseEvent(Guid CourseId, Guid StudentId) : IEvent;

// ============================================================================
// AGGREGATE DEFINITION
// ============================================================================

/// <summary>
/// Dynamic Consistency Boundary for Course Enrollment decision.
/// Tracks BOTH course capacity AND student enrollment count for validation.
/// </summary>
public record CourseEnlistmentAggregate
{
    // Identity (from command - immutable)
    public Guid CourseId { get; private init; }
    public Guid StudentId { get; private init; }

    // Course capacity tracking
    public int CourseMaxCapacity { get; private init; }
    public int CourseCurrentEnrollmentCount { get; private init; }

    // Student enrollment tracking
    public int StudentMaxCourseEnrollmentLimit { get; private init; }
    public int StudentCurrentCourseEnrollmentCount { get; private init; }

    /// <summary>
    /// Initialize aggregate from command context.
    /// The courseId and studentId define the consistency boundary.
    /// </summary>
    public CourseEnlistmentAggregate(Guid courseId, Guid studentId, int studentMaxLimit = 5)
    {
        CourseId = courseId;
        StudentId = studentId;
        StudentMaxCourseEnrollmentLimit = studentMaxLimit;
    }

    /// <summary>
    /// Private constructor for record 'with' expressions.
    /// </summary>
    private CourseEnlistmentAggregate() { }

    /// <summary>
    /// Apply CourseCreated event - sets the course capacity.
    /// Only applies if the event is for THIS course.
    /// </summary>
    public CourseEnlistmentAggregate Apply(CourseCreated @event)
    {
        // Only apply if it's for our course
        if (@event.CourseId != CourseId)
            return this;

        return this with
        {
            CourseMaxCapacity = @event.MaxCapacity
        };
    }

    /// <summary>
    /// Apply CourseCapacityUpdatedEvent - updates the course capacity.
    /// </summary>
    public CourseEnlistmentAggregate Apply(CourseCapacityUpdatedEvent @event)
    {
        if (@event.CourseId != CourseId)
            return this;

        return this with
        {
            CourseMaxCapacity = @event.NewCapacity
        };
    }

    /// <summary>
    /// Apply StudentEnrolledToCourseEvent - updates counts.
    /// - If event is for THIS course -> increment course enrollment count
    /// - If event is for THIS student -> increment student enrollment count
    /// - Could be BOTH if student enrolls in this course
    /// </summary>
    public CourseEnlistmentAggregate Apply(StudentEnrolledToCourseEvent @event)
    {
        var courseCount = CourseCurrentEnrollmentCount;
        var studentCount = StudentCurrentCourseEnrollmentCount;

        // Check if this enrollment affects our course
        if (@event.CourseId == CourseId)
        {
            courseCount++;
        }

        // Check if this enrollment affects our student
        if (@event.StudentId == StudentId)
        {
            studentCount++;
        }

        return this with
        {
            CourseCurrentEnrollmentCount = courseCount,
            StudentCurrentCourseEnrollmentCount = studentCount
        };
    }

    /// <summary>
    /// Apply StudentUnenrolledFromCourseEvent - updates counts.
    /// - If event is for THIS course -> decrement course enrollment count
    /// - If event is for THIS student -> decrement student enrollment count
    /// </summary>
    public CourseEnlistmentAggregate Apply(StudentUnenrolledFromCourseEvent @event)
    {
        var courseCount = CourseCurrentEnrollmentCount;
        var studentCount = StudentCurrentCourseEnrollmentCount;

        // Check if this unenrollment affects our course
        if (@event.CourseId == CourseId)
        {
            courseCount = Math.Max(0, courseCount - 1);
        }

        // Check if this unenrollment affects our student
        if (@event.StudentId == StudentId)
        {
            studentCount = Math.Max(0, studentCount - 1);
        }

        return this with
        {
            CourseCurrentEnrollmentCount = courseCount,
            StudentCurrentCourseEnrollmentCount = studentCount
        };
    }

    // Business logic - Invariant validation

    /// <summary>
    /// Check if student can enroll in course.
    /// Both invariants must be satisfied:
    /// 1. Course has available capacity
    /// 2. Student hasn't reached their enrollment limit
    /// </summary>
    public bool CanEnrollStudent()
    {
        return CourseCurrentEnrollmentCount < CourseMaxCapacity &&
               StudentCurrentCourseEnrollmentCount < StudentMaxCourseEnrollmentLimit;
    }

    public bool CanUnenrollStudent()
    {
        return CourseCurrentEnrollmentCount > 0;
    }

    public string? GetEnrollmentFailureReason()
    {
        if (CourseCurrentEnrollmentCount >= CourseMaxCapacity)
            return "Course is at maximum capacity";

        if (StudentCurrentCourseEnrollmentCount >= StudentMaxCourseEnrollmentLimit)
            return $"Student has reached maximum course enrollment limit ({StudentMaxCourseEnrollmentLimit})";

        return null;
    }
}
