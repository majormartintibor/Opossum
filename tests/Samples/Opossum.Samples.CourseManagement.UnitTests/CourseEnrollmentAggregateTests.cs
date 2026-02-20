using Opossum.Core;
using Opossum.Samples.CourseManagement.CourseEnrollment;
using Opossum.Samples.CourseManagement.Events;
using Tier = Opossum.Samples.CourseManagement.EnrollmentTier.EnrollmentTier;

namespace Opossum.Samples.CourseManagement.UnitTests;

public class CourseEnrollmentProjectionTests
{
    private readonly Guid _courseId = Guid.NewGuid();
    private readonly Guid _studentId = Guid.NewGuid();

    private static SequencedEvent MakeEvent(IEvent payload, long position = 1, params (string Key, string Value)[] tags) =>
        new()
        {
            Position = position,
            Event = new DomainEvent
            {
                EventType = payload.GetType().Name,
                Event = payload,
                Tags = tags.Select(t => new Tag { Key = t.Key, Value = t.Value }).ToList()
            }
        };

    [Fact]
    public void CourseCapacity_InitialState_IsNull()
    {
        var projection = CourseEnrollmentProjections.CourseCapacity(_courseId);

        Assert.Null(projection.InitialState);
    }

    [Fact]
    public void CourseCapacity_Query_ContainsCourseIdTag()
    {
        var projection = CourseEnrollmentProjections.CourseCapacity(_courseId);

        var tag = projection.Query.QueryItems
            .SelectMany(qi => qi.Tags)
            .First(t => t.Key == "courseId");

        Assert.Equal(_courseId.ToString(), tag.Value);
    }

    [Fact]
    public void CourseCapacity_Apply_CourseCreatedEvent_SetsMaxCapacityAndZeroCount()
    {
        var projection = CourseEnrollmentProjections.CourseCapacity(_courseId);
        var evt = MakeEvent(new CourseCreatedEvent(_courseId, "Math 101", "Basic Math", 30));

        var state = projection.Apply(projection.InitialState, evt);

        Assert.NotNull(state);
        Assert.Equal(30, state.MaxCapacity);
        Assert.Equal(0, state.CurrentEnrollmentCount);
    }

    [Fact]
    public void CourseCapacity_Apply_CourseStudentLimitModifiedEvent_UpdatesMaxCapacity()
    {
        var projection = CourseEnrollmentProjections.CourseCapacity(_courseId);
        var created = projection.Apply(projection.InitialState,
            MakeEvent(new CourseCreatedEvent(_courseId, "Math 101", "Basic Math", 30)));

        var state = projection.Apply(created,
            MakeEvent(new CourseStudentLimitModifiedEvent(_courseId, 50), position: 2));

        Assert.NotNull(state);
        Assert.Equal(50, state!.MaxCapacity);
        Assert.Equal(0, state.CurrentEnrollmentCount);
    }

    [Fact]
    public void CourseCapacity_Apply_CourseStudentLimitModifiedEvent_WhenStateNull_ReturnsNull()
    {
        var projection = CourseEnrollmentProjections.CourseCapacity(_courseId);

        var state = projection.Apply(null, MakeEvent(new CourseStudentLimitModifiedEvent(_courseId, 50)));

        Assert.Null(state);
    }

    [Fact]
    public void CourseCapacity_Apply_StudentEnrolledToCourseEvent_IncrementsCount()
    {
        var projection = CourseEnrollmentProjections.CourseCapacity(_courseId);
        var created = projection.Apply(projection.InitialState,
            MakeEvent(new CourseCreatedEvent(_courseId, "Math 101", "Basic Math", 30)));

        var state = projection.Apply(created,
            MakeEvent(new StudentEnrolledToCourseEvent(_courseId, _studentId), position: 2));

        Assert.NotNull(state);
        Assert.Equal(1, state!.CurrentEnrollmentCount);
    }

    [Fact]
    public void CourseCapacity_Apply_StudentEnrolledToCourseEvent_WhenStateNull_ReturnsNull()
    {
        var projection = CourseEnrollmentProjections.CourseCapacity(_courseId);

        var state = projection.Apply(null, MakeEvent(new StudentEnrolledToCourseEvent(_courseId, _studentId)));

        Assert.Null(state);
    }

    [Fact]
    public void CourseCapacity_Apply_MultipleEnrollments_AccumulatesCount()
    {
        var projection = CourseEnrollmentProjections.CourseCapacity(_courseId);
        var state = projection.Apply(projection.InitialState,
            MakeEvent(new CourseCreatedEvent(_courseId, "Math 101", "Basic Math", 5)));

        for (var i = 0; i < 3; i++)
            state = projection.Apply(state, MakeEvent(new StudentEnrolledToCourseEvent(_courseId, Guid.NewGuid()), i + 2));

        Assert.NotNull(state);
        Assert.Equal(3, state!.CurrentEnrollmentCount);
    }

    [Fact]
    public void CourseCapacity_IsFull_WhenCurrentEqualsMax_ReturnsTrue()
    {
        var state = new CourseCapacityState(MaxCapacity: 2, CurrentEnrollmentCount: 2);

        Assert.True(state.IsFull);
    }

    [Fact]
    public void CourseCapacity_IsFull_WhenCurrentLessThanMax_ReturnsFalse()
    {
        var state = new CourseCapacityState(MaxCapacity: 10, CurrentEnrollmentCount: 5);

        Assert.False(state.IsFull);
    }

    [Fact]
    public void StudentEnrollmentLimit_InitialState_IsNull()
    {
        var projection = CourseEnrollmentProjections.StudentEnrollmentLimit(_studentId);

        Assert.Null(projection.InitialState);
    }

    [Fact]
    public void StudentEnrollmentLimit_Query_ContainsStudentIdTag()
    {
        var projection = CourseEnrollmentProjections.StudentEnrollmentLimit(_studentId);

        var tag = projection.Query.QueryItems
            .SelectMany(qi => qi.Tags)
            .First(t => t.Key == "studentId");

        Assert.Equal(_studentId.ToString(), tag.Value);
    }

    [Fact]
    public void StudentEnrollmentLimit_Apply_StudentRegisteredEvent_InitializesWithBasicTier()
    {
        var projection = CourseEnrollmentProjections.StudentEnrollmentLimit(_studentId);
        var evt = MakeEvent(new StudentRegisteredEvent(_studentId, "Jane", "Doe", "jane@example.com"));

        var state = projection.Apply(projection.InitialState, evt);

        Assert.NotNull(state);
        Assert.Equal(Tier.Basic, state!.Tier);
        Assert.Equal(0, state.CurrentCourseCount);
    }

    [Fact]
    public void StudentEnrollmentLimit_Apply_StudentSubscriptionUpdatedEvent_UpdatesTier()
    {
        var projection = CourseEnrollmentProjections.StudentEnrollmentLimit(_studentId);
        var registered = projection.Apply(projection.InitialState,
            MakeEvent(new StudentRegisteredEvent(_studentId, "Jane", "Doe", "jane@example.com")));

        var state = projection.Apply(registered,
            MakeEvent(new StudentSubscriptionUpdatedEvent(_studentId, Tier.Professional), position: 2));

        Assert.NotNull(state);
        Assert.Equal(Tier.Professional, state!.Tier);
    }

    [Fact]
    public void StudentEnrollmentLimit_Apply_StudentSubscriptionUpdatedEvent_WhenStateNull_ReturnsNull()
    {
        var projection = CourseEnrollmentProjections.StudentEnrollmentLimit(_studentId);

        var state = projection.Apply(null,
            MakeEvent(new StudentSubscriptionUpdatedEvent(_studentId, Tier.Professional)));

        Assert.Null(state);
    }

    [Fact]
    public void StudentEnrollmentLimit_Apply_StudentEnrolledToCourseEvent_IncrementsCount()
    {
        var projection = CourseEnrollmentProjections.StudentEnrollmentLimit(_studentId);
        var registered = projection.Apply(projection.InitialState,
            MakeEvent(new StudentRegisteredEvent(_studentId, "Jane", "Doe", "jane@example.com")));

        var state = projection.Apply(registered,
            MakeEvent(new StudentEnrolledToCourseEvent(Guid.NewGuid(), _studentId), position: 2));

        Assert.NotNull(state);
        Assert.Equal(1, state!.CurrentCourseCount);
    }

    [Fact]
    public void StudentEnrollmentLimit_Apply_StudentEnrolledToCourseEvent_WhenStateNull_ReturnsNull()
    {
        var projection = CourseEnrollmentProjections.StudentEnrollmentLimit(_studentId);

        var state = projection.Apply(null,
            MakeEvent(new StudentEnrolledToCourseEvent(Guid.NewGuid(), _studentId)));

        Assert.Null(state);
    }

    [Theory]
    [InlineData(Tier.Basic, 2)]
    [InlineData(Tier.Standard, 5)]
    [InlineData(Tier.Professional, 10)]
    [InlineData(Tier.Master, 25)]
    public void StudentEnrollmentLimit_MaxAllowed_ReturnsCorrectLimitForTier(Tier tier, int expected)
    {
        var state = new StudentEnrollmentLimitState(tier, 0);

        Assert.Equal(expected, state.MaxAllowed);
    }

    [Fact]
    public void StudentEnrollmentLimit_IsAtLimit_WhenCurrentEqualsMax_ReturnsTrue()
    {
        var state = new StudentEnrollmentLimitState(Tier.Basic, 2);

        Assert.True(state.IsAtLimit);
    }

    [Fact]
    public void StudentEnrollmentLimit_IsAtLimit_WhenCurrentLessThanMax_ReturnsFalse()
    {
        var state = new StudentEnrollmentLimitState(Tier.Standard, 3);

        Assert.False(state.IsAtLimit);
    }

    [Fact]
    public void AlreadyEnrolled_InitialState_IsFalse()
    {
        var projection = CourseEnrollmentProjections.AlreadyEnrolled(_courseId, _studentId);

        Assert.False(projection.InitialState);
    }

    [Fact]
    public void AlreadyEnrolled_Query_ContainsBothCourseIdAndStudentIdTags()
    {
        var projection = CourseEnrollmentProjections.AlreadyEnrolled(_courseId, _studentId);
        var tags = projection.Query.QueryItems.SelectMany(qi => qi.Tags).ToList();

        Assert.Contains(tags, t => t.Key == "courseId" && t.Value == _courseId.ToString());
        Assert.Contains(tags, t => t.Key == "studentId" && t.Value == _studentId.ToString());
    }

    [Fact]
    public void AlreadyEnrolled_Apply_StudentEnrolledToCourseEvent_ReturnsTrue()
    {
        var projection = CourseEnrollmentProjections.AlreadyEnrolled(_courseId, _studentId);
        var evt = MakeEvent(new StudentEnrolledToCourseEvent(_courseId, _studentId));

        var state = projection.Apply(false, evt);

        Assert.True(state);
    }
}
