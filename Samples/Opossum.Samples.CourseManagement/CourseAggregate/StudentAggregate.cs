using Opossum.Core;
using Opossum.Samples.CourseManagement.EnrollmentTier;
using Opossum.Samples.CourseManagement.Events;
using Tier = Opossum.Samples.CourseManagement.EnrollmentTier.EnrollmentTier;

namespace Opossum.Samples.CourseManagement.CourseAggregate;

/// <summary>
/// An Event-Sourced Aggregate representing a student's enrollment profile.
/// Used alongside <see cref="CourseAggregate"/> to enforce the cross-aggregate
/// tier-based enrollment-limit invariant in the Aggregate pattern approach.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a second aggregate is needed</b><br/>
/// The rule "a student may not exceed their tier-based course limit" is a
/// <em>cross-entity</em> invariant: it concerns both the course being enrolled in and
/// the student being enrolled. In traditional DDD each aggregate owns exactly one
/// consistency boundary, so enforcing this rule requires reconstituting <em>both</em>
/// aggregates — <see cref="CourseAggregate"/> for course capacity and
/// <see cref="StudentAggregate"/> (this class) for tier limit.
/// </para>
/// <para>
/// <b>Read-only in the subscription flow</b><br/>
/// <c>StudentAggregate</c> does not record new events. When a student subscribes to
/// a course, <see cref="CourseAggregate.SubscribeStudent"/> records the
/// <see cref="StudentEnrolledToCourseEvent"/>. Because that event is tagged with
/// <em>both</em> <c>courseId</c> and <c>studentId</c>, reconstituting the
/// <c>StudentAggregate</c> from events tagged <c>studentId</c> automatically picks
/// it up — giving the student-centric view of enrollment state without a second write.
/// </para>
/// <para>
/// <b>The cross-aggregate atomicity problem</b><br/>
/// Loading two aggregates via two separate <see cref="IEventStore.ReadAsync"/> calls
/// leaves a window between the reads where a concurrent writer could modify either
/// entity undetected. The solution — mirroring what
/// <see cref="Opossum.DecisionModel.DecisionModelExtensions.BuildDecisionModelAsync{T1,T2}"/>
/// does for DCB projections — is a <em>single unified read</em> over the union of both
/// tag queries. The maximum position across all returned events becomes the shared
/// <see cref="AppendCondition.AfterSequencePosition"/> watermark, and the
/// <see cref="AppendCondition.FailIfEventsMatch"/> query spans both entity tags.
/// See <see cref="CourseAggregateRepository.LoadBothForSubscriptionAsync"/> for the
/// implementation and <see cref="CourseAggregateRepository.SaveSubscriptionAsync"/>
/// for the compound append guard.
/// </para>
/// <para>
/// <b>Compare with the DCB Decision Model</b><br/>
/// The equivalent invariants in the DCB approach
/// (<c>CourseEnrollment/EnrollStudentToCourseCommand.cs</c>) are all enforced by a
/// single <see cref="Opossum.DecisionModel.DecisionModelExtensions.BuildDecisionModelAsync{T1,T2,T3}"/>
/// call with three ephemeral projections. No aggregate objects are reconstituted;
/// the compound <see cref="AppendCondition"/> is computed automatically. That approach
/// requires zero extra classes for cross-entity coordination. See
/// <c>docs/analysis/aggregate-vs-dcb-comparison.md</c> for the full side-by-side
/// analysis.
/// </para>
/// </remarks>
public sealed class StudentAggregate
{
    /// <summary>The identifier of this student.</summary>
    public Guid StudentId { get; private set; }

    /// <summary>The student's current enrollment tier.</summary>
    public Tier EnrollmentTier { get; private set; }

    /// <summary>Number of courses the student is currently enrolled in.</summary>
    public int CourseEnrollmentCount { get; private set; }

    /// <summary>
    /// The store-assigned position of the last event this aggregate was reconstituted from.
    /// </summary>
    /// <remarks>
    /// This is a <em>global</em> store position, not a per-aggregate counter.
    /// When used in the subscription flow, the relevant watermark is the
    /// <c>sharedPosition</c> returned by
    /// <see cref="CourseAggregateRepository.LoadBothForSubscriptionAsync"/>, which
    /// is the MAX position across <em>all</em> events in the unified read — not this
    /// field, which only reflects the last event tagged <c>studentId</c>.
    /// </remarks>
    public long Version { get; private set; }

    /// <summary>Maximum number of courses allowed for the student's current tier.</summary>
    public int MaxCoursesAllowed => StudentMaxCourseEnrollment.GetMaxCoursesAllowed(EnrollmentTier);

    /// <summary>True when the student has reached their tier-based enrollment limit.</summary>
    public bool IsAtEnrollmentLimit => CourseEnrollmentCount >= MaxCoursesAllowed;

    private StudentAggregate() { }

    /// <summary>
    /// Reconstitutes a student aggregate from previously persisted sequenced events.
    /// Sets <see cref="Version"/> to the position of the last event.
    /// </summary>
    public static StudentAggregate Reconstitute(SequencedEvent[] events)
    {
        var instance = new StudentAggregate();
        foreach (var sequencedEvent in events)
        {
            instance.Apply(sequencedEvent.Event.Event);
            instance.Version = sequencedEvent.Position;
        }
        return instance;
    }

    private void Apply(IEvent @event)
    {
        switch (@event)
        {
            case StudentRegisteredEvent registered:
                StudentId = registered.StudentId;
                EnrollmentTier = Tier.Basic;
                break;
            case StudentSubscriptionUpdatedEvent updated:
                EnrollmentTier = updated.EnrollmentTier;
                break;
            case StudentEnrolledToCourseEvent:
                CourseEnrollmentCount++;
                break;
        }
    }
}
