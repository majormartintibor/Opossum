using Opossum.Core;
using Opossum.Samples.CourseManagement.Events;

namespace Opossum.Samples.CourseManagement.CourseAggregate;

/// <summary>
/// An Event-Sourced Aggregate representing a course that students can subscribe to.
/// This is the C# implementation of the JavaScript example published at
/// <see href="https://dcb.events/examples/event-sourced-aggregate/#dcb-approach"/>.
/// </summary>
/// <remarks>
/// <para>
/// The aggregate is pure business logic — no knowledge of infrastructure, no Opossum
/// machinery. All I/O is handled by <see cref="CourseAggregateRepository"/>.
/// </para>
/// <para>
/// The same <see cref="CourseCreatedEvent"/>, <see cref="CourseStudentLimitModifiedEvent"/>,
/// and <see cref="StudentEnrolledToCourseEvent"/> events used by the Decision Model endpoints
/// (e.g. <c>POST /courses</c>, <c>POST /courses/{id}/enrollments</c>) are reused here,
/// keeping a single unified event log shared by both approaches.
/// </para>
///
/// <para><b>Two approaches, one event log — pick one for your application:</b></para>
/// <list type="number">
///   <item>
///     <description>
///       <b>DCB Decision Model</b> (<c>CourseEnrollment/EnrollStudentToCourseCommand.cs</c>):
///       Stateless ephemeral projections; each command reads exactly the events it needs.
///       Multiple independent invariants are composed in a single read. Best when business
///       rules cut across several entity types (e.g. course capacity AND student tier limit).
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Event-Sourced Aggregate</b> (this class + <see cref="CourseAggregateRepository"/>):
///       All course-local state is encapsulated in a reconstituted aggregate object.
///       The <see cref="CourseAggregateRepository"/> replaces the traditional named-stream
///       lock with a tag-scoped <see cref="AppendCondition"/>.
///       Best when a rich domain model has many aggregate-internal invariants.
///     </description>
///   </item>
/// </list>
/// <para>You do not need both in the same application. The sample includes both
/// to let you compare them side by side.</para>
/// </remarks>
public sealed class CourseAggregate
{
    private readonly List<IEvent> _recordedEvents = [];

    /// <summary>The identifier of this course.</summary>
    public Guid CourseId { get; private set; }

    /// <summary>The name of this course.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The description of this course.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>Maximum number of students allowed.</summary>
    public int Capacity { get; private set; }

    /// <summary>Number of students currently subscribed.</summary>
    public int EnrollmentCount { get; private set; }

    /// <summary>
    /// The store-assigned position of the last event this aggregate was reconstituted from.
    /// Passed as <see cref="AppendCondition.AfterSequencePosition"/> by
    /// <see cref="CourseAggregateRepository.SaveAsync"/> to guard against concurrent writes.
    /// </summary>
    /// <remarks>
    /// This is a <em>global</em> store position — store-wide monotonically increasing across
    /// all events, not a per-aggregate counter. A course with 3 events may have
    /// <c>Version = 1042</c> if other events exist in the store. This is correct for
    /// the DCB guard: <c>AfterSequencePosition</c> always refers to a global position.
    /// <para>0 means this aggregate has never been persisted.</para>
    /// </remarks>
    public long Version { get; private set; }

    private CourseAggregate() { }

    /// <summary>
    /// Creates a new course. Records a <see cref="CourseCreatedEvent"/> internally
    /// but does not persist it — call <see cref="CourseAggregateRepository.SaveAsync"/> afterwards.
    /// </summary>
    public static CourseAggregate Create(Guid courseId, string name, string description, int maxStudents)
    {
        var instance = new CourseAggregate();
        instance.RecordEvent(new CourseCreatedEvent(courseId, name, description, maxStudents));
        return instance;
    }

    /// <summary>
    /// Reconstitutes an existing course from previously persisted sequenced events.
    /// Sets <see cref="Version"/> to the position of the last event.
    /// </summary>
    public static CourseAggregate Reconstitute(SequencedEvent[] events)
    {
        var instance = new CourseAggregate();
        foreach (var sequencedEvent in events)
        {
            instance.Apply(sequencedEvent.Event.Event);
            instance.Version = sequencedEvent.Position;
        }
        return instance;
    }

    /// <summary>
    /// Changes the maximum capacity of this course.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// When <paramref name="newCapacity"/> equals the current capacity, or is below the
    /// current subscription count.
    /// </exception>
    public void ChangeCapacity(int newCapacity)
    {
        if (newCapacity == Capacity)
            throw new InvalidOperationException(
                $"Course \"{CourseId}\" already has a capacity of {newCapacity}.");

        if (newCapacity < EnrollmentCount)
            throw new InvalidOperationException(
                $"Course \"{CourseId}\" already has {EnrollmentCount} active subscriptions, " +
                $"can't set the capacity below that.");

        RecordEvent(new CourseStudentLimitModifiedEvent(CourseId, newCapacity));
    }

    /// <summary>
    /// Subscribes a student to this course. Only enforces course-level capacity —
    /// student existence is validated at the endpoint level.
    /// </summary>
    /// <exception cref="InvalidOperationException">When the course is fully booked.</exception>
    public void SubscribeStudent(Guid studentId)
    {
        if (EnrollmentCount >= Capacity)
            throw new InvalidOperationException($"Course \"{CourseId}\" is already fully booked.");

        RecordEvent(new StudentEnrolledToCourseEvent(CourseId, studentId));
    }

    /// <summary>
    /// Returns and clears all domain events recorded since <see cref="Create"/> was called
    /// or since the last successful <see cref="CourseAggregateRepository.SaveAsync"/>.
    /// Pass the returned array to the repository to persist.
    /// </summary>
    public IEvent[] PullRecordedEvents()
    {
        var events = _recordedEvents.ToArray();
        _recordedEvents.Clear();
        return events;
    }

    private void RecordEvent(IEvent @event)
    {
        _recordedEvents.Add(@event);
        Apply(@event);
    }

    private void Apply(IEvent @event)
    {
        switch (@event)
        {
            case CourseCreatedEvent created:
                CourseId = created.CourseId;
                Name = created.Name;
                Description = created.Description;
                Capacity = created.MaxStudentCount;
                break;
            case CourseStudentLimitModifiedEvent modified:
                Capacity = modified.NewMaxStudentCount;
                break;
            case StudentEnrolledToCourseEvent:
                EnrollmentCount++;
                break;
        }
    }
}
