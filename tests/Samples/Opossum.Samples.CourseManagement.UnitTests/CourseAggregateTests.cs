using Opossum.Core;
using Opossum.Samples.CourseManagement.Events;

// "CourseAggregate" is both the folder namespace and the class name.
// An alias with the same name still loses to the enclosing namespace lookup in C#,
// so we use a distinct alias to avoid the ambiguity.
using Aggregate = Opossum.Samples.CourseManagement.CourseAggregate.CourseAggregate;

namespace Opossum.Samples.CourseManagement.UnitTests;

/// <summary>
/// Pure unit tests for <see cref="CourseAggregate"/>.
/// No I/O, no dependencies â€” only input/output assertions on the aggregate's
/// public API: factories, business methods, recorded events, and version tracking.
/// </summary>
public class CourseAggregateTests
{
    private readonly Guid _courseId = Guid.NewGuid();
    private readonly Guid _studentId = Guid.NewGuid();

    private static SequencedEvent MakeEvent(IEvent payload, long position = 1) =>
        new()
        {
            Position = position,
            Event = new DomainEvent
            {
                EventType = payload.GetType().Name,
                Event = payload
            }
        };

    // â”€â”€ Create â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Create_RecordsSingleCourseCreatedEvent()
    {
        var aggregate = Aggregate.Create(_courseId, "Math 101", "Basic maths", 30);

        var single = Assert.Single(aggregate.PullRecordedEvents());
        Assert.IsType<CourseCreatedEvent>(single);
    }

    [Fact]
    public void Create_SetsStateFromCourseCreatedEvent()
    {
        var aggregate = Aggregate.Create(_courseId, "Math 101", "Basic maths", 30);

        Assert.Equal(_courseId, aggregate.CourseId);
        Assert.Equal("Math 101", aggregate.Name);
        Assert.Equal("Basic maths", aggregate.Description);
        Assert.Equal(30, aggregate.Capacity);
        Assert.Equal(0, aggregate.EnrollmentCount);
    }

    [Fact]
    public void Create_VersionIsZero_BeforeFirstSave()
    {
        var aggregate = Aggregate.Create(_courseId, "Math 101", "Basic maths", 30);

        Assert.Equal(0, aggregate.Version);
    }

    // â”€â”€ Reconstitute â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Reconstitute_SingleCourseCreatedEvent_RestoresStateAndVersion()
    {
        var events = new[]
        {
            MakeEvent(new CourseCreatedEvent(_courseId, "Math 101", "Basic maths", 30), position: 5)
        };

        var aggregate = Aggregate.Reconstitute(events);

        Assert.Equal(_courseId, aggregate.CourseId);
        Assert.Equal("Math 101", aggregate.Name);
        Assert.Equal("Basic maths", aggregate.Description);
        Assert.Equal(30, aggregate.Capacity);
        Assert.Equal(0, aggregate.EnrollmentCount);
        Assert.Equal(5, aggregate.Version);
    }

    [Fact]
    public void Reconstitute_MultipleEvents_AppliesAllInOrder()
    {
        var events = new[]
        {
            MakeEvent(new CourseCreatedEvent(_courseId, "Math 101", "Basic maths", 10), position: 1),
            MakeEvent(new CourseStudentLimitModifiedEvent(_courseId, 25), position: 3),
            MakeEvent(new StudentEnrolledToCourseEvent(_courseId, _studentId), position: 7)
        };

        var aggregate = Aggregate.Reconstitute(events);

        Assert.Equal(25, aggregate.Capacity);
        Assert.Equal(1, aggregate.EnrollmentCount);
    }

    [Fact]
    public void Reconstitute_VersionEqualsLastEventPosition()
    {
        var events = new[]
        {
            MakeEvent(new CourseCreatedEvent(_courseId, "C", "D", 10), position: 42),
            MakeEvent(new CourseStudentLimitModifiedEvent(_courseId, 20), position: 99)
        };

        var aggregate = Aggregate.Reconstitute(events);

        Assert.Equal(99, aggregate.Version);
    }

    [Fact]
    public void Reconstitute_ProducesNoRecordedEvents()
    {
        var aggregate = Aggregate.Reconstitute([
            MakeEvent(new CourseCreatedEvent(_courseId, "Math 101", "Basic maths", 10), position: 1)
        ]);

        Assert.Empty(aggregate.PullRecordedEvents());
    }

    // â”€â”€ ChangeCapacity â€” happy path â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void ChangeCapacity_ValidValue_RecordsCourseStudentLimitModifiedEvent()
    {
        var aggregate = Aggregate.Reconstitute([
            MakeEvent(new CourseCreatedEvent(_courseId, "Math 101", "Basic maths", 10), position: 1)
        ]);

        aggregate.ChangeCapacity(25);

        var single = Assert.Single(aggregate.PullRecordedEvents());
        var modified = Assert.IsType<CourseStudentLimitModifiedEvent>(single);
        Assert.Equal(25, modified.NewMaxStudentCount);
    }

    [Fact]
    public void ChangeCapacity_ValidValue_UpdatesCapacityInMemory()
    {
        var aggregate = Aggregate.Reconstitute([
            MakeEvent(new CourseCreatedEvent(_courseId, "Math 101", "Basic maths", 10), position: 1)
        ]);

        aggregate.ChangeCapacity(25);

        Assert.Equal(25, aggregate.Capacity);
    }

    [Fact]
    public void ChangeCapacity_ExactlyAtEnrollmentCount_Succeeds()
    {
        // newCapacity == EnrollmentCount is allowed: the check is newCapacity < EnrollmentCount.
        // Shrinking to exactly the current headcount keeps all enrolled students valid.
        var aggregate = Aggregate.Reconstitute([
            MakeEvent(new CourseCreatedEvent(_courseId, "Math 101", "Basic maths", 5), position: 1),
            MakeEvent(new StudentEnrolledToCourseEvent(_courseId, _studentId), position: 2)
        ]);

        // Explicit Action cast avoids the xUnit 2.9.x Action/Func<Task> overload ambiguity.
        var ex = Record.Exception((Action)(() => aggregate.ChangeCapacity(1)));

        Assert.Null(ex);
    }

    // â”€â”€ ChangeCapacity â€” error cases â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void ChangeCapacity_SameValue_ThrowsInvalidOperationException()
    {
        var aggregate = Aggregate.Reconstitute([
            MakeEvent(new CourseCreatedEvent(_courseId, "Math 101", "Basic maths", 10), position: 1)
        ]);

        var ex = Assert.Throws<InvalidOperationException>(
            (Action)(() => aggregate.ChangeCapacity(10)));

        Assert.Contains("already has a capacity", ex.Message);
    }

    [Fact]
    public void ChangeCapacity_SameValue_DoesNotRecordEvent()
    {
        var aggregate = Aggregate.Reconstitute([
            MakeEvent(new CourseCreatedEvent(_courseId, "Math 101", "Basic maths", 10), position: 1)
        ]);

        Assert.Throws<InvalidOperationException>(
            (Action)(() => aggregate.ChangeCapacity(10)));

        Assert.Empty(aggregate.PullRecordedEvents());
    }

    [Fact]
    public void ChangeCapacity_BelowEnrollmentCount_ThrowsInvalidOperationException()
    {
        var aggregate = Aggregate.Reconstitute([
            MakeEvent(new CourseCreatedEvent(_courseId, "Math 101", "Basic maths", 5), position: 1),
            MakeEvent(new StudentEnrolledToCourseEvent(_courseId, Guid.NewGuid()), position: 2),
            MakeEvent(new StudentEnrolledToCourseEvent(_courseId, Guid.NewGuid()), position: 3)
        ]);

        var ex = Assert.Throws<InvalidOperationException>(
            (Action)(() => aggregate.ChangeCapacity(1)));

        Assert.Contains("active subscriptions", ex.Message);
    }

    [Fact]
    public void ChangeCapacity_BelowEnrollmentCount_DoesNotRecordEvent()
    {
        var aggregate = Aggregate.Reconstitute([
            MakeEvent(new CourseCreatedEvent(_courseId, "Math 101", "Basic maths", 5), position: 1),
            MakeEvent(new StudentEnrolledToCourseEvent(_courseId, Guid.NewGuid()), position: 2),
            MakeEvent(new StudentEnrolledToCourseEvent(_courseId, Guid.NewGuid()), position: 3)
        ]);

        Assert.Throws<InvalidOperationException>(
            (Action)(() => aggregate.ChangeCapacity(1)));

        Assert.Empty(aggregate.PullRecordedEvents());
    }

    // â”€â”€ SubscribeStudent â€” happy path â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void SubscribeStudent_SeatsAvailable_RecordsStudentEnrolledToCourseEvent()
    {
        var aggregate = Aggregate.Reconstitute([
            MakeEvent(new CourseCreatedEvent(_courseId, "Math 101", "Basic maths", 5), position: 1)
        ]);

        aggregate.SubscribeStudent(_studentId);

        var single = Assert.Single(aggregate.PullRecordedEvents());
        var enrolled = Assert.IsType<StudentEnrolledToCourseEvent>(single);
        Assert.Equal(_courseId, enrolled.CourseId);
        Assert.Equal(_studentId, enrolled.StudentId);
    }

    [Fact]
    public void SubscribeStudent_SeatsAvailable_IncrementsEnrollmentCount()
    {
        var aggregate = Aggregate.Reconstitute([
            MakeEvent(new CourseCreatedEvent(_courseId, "Math 101", "Basic maths", 5), position: 1)
        ]);

        aggregate.SubscribeStudent(_studentId);

        Assert.Equal(1, aggregate.EnrollmentCount);
    }

    [Fact]
    public void SubscribeStudent_MultipleStudents_AccumulatesEnrollmentCount()
    {
        var aggregate = Aggregate.Reconstitute([
            MakeEvent(new CourseCreatedEvent(_courseId, "Math 101", "Basic maths", 10), position: 1)
        ]);

        for (var i = 0; i < 5; i++)
            aggregate.SubscribeStudent(Guid.NewGuid());

        Assert.Equal(5, aggregate.EnrollmentCount);
    }

    // â”€â”€ SubscribeStudent â€” error cases â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void SubscribeStudent_AtCapacity_ThrowsInvalidOperationException()
    {
        var aggregate = Aggregate.Reconstitute([
            MakeEvent(new CourseCreatedEvent(_courseId, "Math 101", "Basic maths", 1), position: 1),
            MakeEvent(new StudentEnrolledToCourseEvent(_courseId, Guid.NewGuid()), position: 2)
        ]);

        var ex = Assert.Throws<InvalidOperationException>(
            (Action)(() => aggregate.SubscribeStudent(_studentId)));

        Assert.Contains("fully booked", ex.Message);
    }

    [Fact]
    public void SubscribeStudent_AtCapacity_DoesNotRecordEvent()
    {
        var aggregate = Aggregate.Reconstitute([
            MakeEvent(new CourseCreatedEvent(_courseId, "Math 101", "Basic maths", 1), position: 1),
            MakeEvent(new StudentEnrolledToCourseEvent(_courseId, Guid.NewGuid()), position: 2)
        ]);

        Assert.Throws<InvalidOperationException>(
            (Action)(() => aggregate.SubscribeStudent(_studentId)));

        Assert.Empty(aggregate.PullRecordedEvents());
    }

    // â”€â”€ PullRecordedEvents â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void PullRecordedEvents_ClearsListAfterPull()
    {
        var aggregate = Aggregate.Create(_courseId, "Math 101", "Basic maths", 30);
        aggregate.PullRecordedEvents(); // flush the CourseCreatedEvent

        Assert.Empty(aggregate.PullRecordedEvents());
    }

    [Fact]
    public void PullRecordedEvents_AccumulatesEventsAcrossMultipleCommands()
    {
        var aggregate = Aggregate.Reconstitute([
            MakeEvent(new CourseCreatedEvent(_courseId, "Math 101", "Basic maths", 10), position: 1)
        ]);

        aggregate.ChangeCapacity(20);
        aggregate.SubscribeStudent(_studentId);

        var events = aggregate.PullRecordedEvents();

        Assert.Equal(2, events.Length);
        Assert.IsType<CourseStudentLimitModifiedEvent>(events[0]);
        Assert.IsType<StudentEnrolledToCourseEvent>(events[1]);
    }

    // â”€â”€ Version semantics â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Version_ReflectsGlobalStorePosition_NotAggregateEventCount()
    {
        // This course has 2 aggregate events, but their store positions are 1001 and 1042
        // because other (unrelated) events exist between them in the global log.
        // Version == 1042, not 2 â€” demonstrating that Version is always a global position.
        var events = new[]
        {
            MakeEvent(new CourseCreatedEvent(_courseId, "C", "D", 10), position: 1001),
            MakeEvent(new CourseStudentLimitModifiedEvent(_courseId, 20), position: 1042)
        };

        var aggregate = Aggregate.Reconstitute(events);

        Assert.Equal(1042, aggregate.Version);
        Assert.Equal(20, aggregate.Capacity);
    }
}
