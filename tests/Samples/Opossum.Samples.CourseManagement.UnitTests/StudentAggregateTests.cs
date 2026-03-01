using Opossum.Core;
using Opossum.Samples.CourseManagement.EnrollmentTier;
using Opossum.Samples.CourseManagement.Events;
using Tier = Opossum.Samples.CourseManagement.EnrollmentTier.EnrollmentTier;
using StudentAggregate = Opossum.Samples.CourseManagement.CourseAggregate.StudentAggregate;

namespace Opossum.Samples.CourseManagement.UnitTests;

/// <summary>
/// Pure unit tests for <see cref="StudentAggregate"/>.
/// No I/O, no dependencies — only input/output assertions on the aggregate's
/// public API: reconstitution, state folding, and business properties.
/// </summary>
public class StudentAggregateTests
{
    private readonly Guid _studentId = Guid.NewGuid();
    private readonly Guid _courseId = Guid.NewGuid();

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

    // ── Reconstitute ─────────────────────────────────────────────────────────

    [Fact]
    public void Reconstitute_StudentRegisteredEvent_SetsBasicTierAndZeroCount()
    {
        var events = new[]
        {
            MakeEvent(new StudentRegisteredEvent(_studentId, "Jane", "Doe", "jane@example.com"), position: 3)
        };

        var aggregate = StudentAggregate.Reconstitute(events);

        Assert.Equal(_studentId, aggregate.StudentId);
        Assert.Equal(Tier.Basic, aggregate.EnrollmentTier);
        Assert.Equal(0, aggregate.CourseEnrollmentCount);
        Assert.Equal(3, aggregate.Version);
    }

    [Fact]
    public void Reconstitute_StudentSubscriptionUpdatedEvent_UpdatesTier()
    {
        var events = new[]
        {
            MakeEvent(new StudentRegisteredEvent(_studentId, "Jane", "Doe", "jane@example.com"), position: 1),
            MakeEvent(new StudentSubscriptionUpdatedEvent(_studentId, Tier.Professional), position: 5)
        };

        var aggregate = StudentAggregate.Reconstitute(events);

        Assert.Equal(Tier.Professional, aggregate.EnrollmentTier);
        Assert.Equal(5, aggregate.Version);
    }

    [Fact]
    public void Reconstitute_StudentEnrolledToCourseEvent_IncrementsCount()
    {
        var events = new[]
        {
            MakeEvent(new StudentRegisteredEvent(_studentId, "Jane", "Doe", "jane@example.com"), position: 1),
            MakeEvent(new StudentEnrolledToCourseEvent(Guid.NewGuid(), _studentId), position: 2),
            MakeEvent(new StudentEnrolledToCourseEvent(Guid.NewGuid(), _studentId), position: 3)
        };

        var aggregate = StudentAggregate.Reconstitute(events);

        Assert.Equal(2, aggregate.CourseEnrollmentCount);
        Assert.Equal(3, aggregate.Version);
    }

    [Fact]
    public void Reconstitute_VersionEqualsLastEventPosition()
    {
        var events = new[]
        {
            MakeEvent(new StudentRegisteredEvent(_studentId, "Jane", "Doe", "jane@example.com"), position: 10),
            MakeEvent(new StudentSubscriptionUpdatedEvent(_studentId, Tier.Standard), position: 77)
        };

        var aggregate = StudentAggregate.Reconstitute(events);

        Assert.Equal(77, aggregate.Version);
    }

    // ── MaxCoursesAllowed ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(Tier.Basic, 2)]
    [InlineData(Tier.Standard, 5)]
    [InlineData(Tier.Professional, 10)]
    [InlineData(Tier.Master, 25)]
    public void MaxCoursesAllowed_ReturnsCorrectLimitForTier(Tier tier, int expected)
    {
        var events = new[]
        {
            MakeEvent(new StudentRegisteredEvent(_studentId, "Jane", "Doe", "jane@example.com"), position: 1),
            MakeEvent(new StudentSubscriptionUpdatedEvent(_studentId, tier), position: 2)
        };

        var aggregate = StudentAggregate.Reconstitute(events);

        Assert.Equal(expected, aggregate.MaxCoursesAllowed);
    }

    // ── IsAtEnrollmentLimit ───────────────────────────────────────────────────

    [Fact]
    public void IsAtEnrollmentLimit_BasicTier_FalseWhenBelowLimit()
    {
        var events = new[]
        {
            MakeEvent(new StudentRegisteredEvent(_studentId, "Jane", "Doe", "jane@example.com"), position: 1),
            MakeEvent(new StudentEnrolledToCourseEvent(Guid.NewGuid(), _studentId), position: 2)
        };

        var aggregate = StudentAggregate.Reconstitute(events);

        // Basic allows 2; enrolled in 1 — not yet at limit
        Assert.False(aggregate.IsAtEnrollmentLimit);
    }

    [Fact]
    public void IsAtEnrollmentLimit_BasicTier_TrueWhenAtLimit()
    {
        var events = new[]
        {
            MakeEvent(new StudentRegisteredEvent(_studentId, "Jane", "Doe", "jane@example.com"), position: 1),
            MakeEvent(new StudentEnrolledToCourseEvent(Guid.NewGuid(), _studentId), position: 2),
            MakeEvent(new StudentEnrolledToCourseEvent(Guid.NewGuid(), _studentId), position: 3)
        };

        var aggregate = StudentAggregate.Reconstitute(events);

        // Basic allows 2; enrolled in 2 — at limit
        Assert.True(aggregate.IsAtEnrollmentLimit);
    }

    [Fact]
    public void IsAtEnrollmentLimit_FreshStudent_FalseWithNoEnrollments()
    {
        var events = new[]
        {
            MakeEvent(new StudentRegisteredEvent(_studentId, "Jane", "Doe", "jane@example.com"), position: 1)
        };

        var aggregate = StudentAggregate.Reconstitute(events);

        Assert.False(aggregate.IsAtEnrollmentLimit);
    }

    [Fact]
    public void IsAtEnrollmentLimit_AfterTierUpgrade_FalseWhenNewLimitNotReached()
    {
        // Student had 2 courses (Basic limit), but just upgraded to Standard (5 allowed).
        var events = new[]
        {
            MakeEvent(new StudentRegisteredEvent(_studentId, "Jane", "Doe", "jane@example.com"), position: 1),
            MakeEvent(new StudentEnrolledToCourseEvent(Guid.NewGuid(), _studentId), position: 2),
            MakeEvent(new StudentEnrolledToCourseEvent(Guid.NewGuid(), _studentId), position: 3),
            MakeEvent(new StudentSubscriptionUpdatedEvent(_studentId, Tier.Standard), position: 4)
        };

        var aggregate = StudentAggregate.Reconstitute(events);

        // Standard allows 5; enrolled in 2 — no longer at limit
        Assert.False(aggregate.IsAtEnrollmentLimit);
    }
}
