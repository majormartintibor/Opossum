using Opossum.Core;
using Opossum.Samples.CourseManagement.CourseAnnouncement;
using Opossum.Samples.CourseManagement.CourseAnnouncementRetraction;
using Opossum.Samples.CourseManagement.Events;

namespace Opossum.Samples.CourseManagement.UnitTests;

/// <summary>
/// Unit tests for the Course Announcement Decision Model projections.
///
/// Covers the DCB "Prevent Record Duplication" pattern:
/// https://dcb.events/examples/prevent-record-duplication/
///
/// All tests are pure in-memory folds — no event store, no file system.
/// </summary>
public class CourseAnnouncementProjectionTests
{
    private readonly Guid _courseId = Guid.NewGuid();
    private readonly Guid _token = Guid.NewGuid();
    private readonly Guid _otherToken = Guid.NewGuid();
    private readonly Guid _announcementId = Guid.NewGuid();

    private static SequencedEvent MakeEvent(IEvent payload, long position = 1, params (string Key, string Value)[] tags) =>
        new()
        {
            Position = position,
            Event = new DomainEvent
            {
                EventType = payload.GetType().Name,
                Event = payload,
                Tags = [.. tags.Select(t => new Tag(t.Key, t.Value))]
            }
        };

    // -------------------------------------------------------------------------
    // CourseExists projection
    // -------------------------------------------------------------------------

    [Fact]
    public void CourseExists_InitialState_IsFalse()
    {
        var projection = CourseAnnouncementProjections.CourseExists(_courseId);

        Assert.False(projection.InitialState);
    }

    [Fact]
    public void CourseExists_Query_ContainsCourseIdTag()
    {
        var projection = CourseAnnouncementProjections.CourseExists(_courseId);

        var tag = projection.Query.QueryItems
            .SelectMany(qi => qi.Tags)
            .Single(t => t.Key == "courseId");

        Assert.Equal(_courseId.ToString(), tag.Value);
    }

    [Fact]
    public void CourseExists_Apply_CourseCreatedEvent_ReturnsTrue()
    {
        var projection = CourseAnnouncementProjections.CourseExists(_courseId);
        var evt = MakeEvent(new CourseCreatedEvent(_courseId, "Intro to CS", "Basics", 30),
            tags: ("courseId", _courseId.ToString()));

        var state = projection.Apply(projection.InitialState, evt);

        Assert.True(state);
    }

    [Fact]
    public void CourseExists_Query_DoesNotMatchEventWithDifferentCourseIdTag()
    {
        // Filtering is the query's job — the apply function only receives events the query matches.
        // Verify that an event carrying a different courseId tag is excluded by the query.
        var projection = CourseAnnouncementProjections.CourseExists(_courseId);
        var evt = MakeEvent(new CourseCreatedEvent(Guid.NewGuid(), "Other", "Other", 10),
            tags: ("courseId", Guid.NewGuid().ToString()));

        Assert.False(projection.Query.Matches(evt));
    }

    // -------------------------------------------------------------------------
    // IdempotencyTokenWasUsed projection
    // -------------------------------------------------------------------------

    [Fact]
    public void IdempotencyTokenWasUsed_InitialState_IsFalse()
    {
        var projection = CourseAnnouncementProjections.IdempotencyTokenWasUsed(_token);

        Assert.False(projection.InitialState);
    }

    [Fact]
    public void IdempotencyTokenWasUsed_Query_ContainsIdempotencyTag()
    {
        var projection = CourseAnnouncementProjections.IdempotencyTokenWasUsed(_token);

        var tag = projection.Query.QueryItems
            .SelectMany(qi => qi.Tags)
            .Single(t => t.Key == "idempotency");

        Assert.Equal(_token.ToString(), tag.Value);
    }

    [Fact]
    public void IdempotencyTokenWasUsed_NoEvents_ReturnsFalse()
    {
        var projection = CourseAnnouncementProjections.IdempotencyTokenWasUsed(_token);

        Assert.False(projection.InitialState);
    }

    [Fact]
    public void IdempotencyTokenWasUsed_PostedEvent_ReturnsTrue()
    {
        var projection = CourseAnnouncementProjections.IdempotencyTokenWasUsed(_token);
        var evt = MakeEvent(
            new CourseAnnouncementPostedEvent(_announcementId, _courseId, "Title", "Body", _token),
            tags: ("idempotency", _token.ToString()));

        var state = projection.Apply(projection.InitialState, evt);

        Assert.True(state);
    }

    [Fact]
    public void IdempotencyTokenWasUsed_Query_DoesNotMatchEventWithDifferentToken()
    {
        // Filtering is the query's job — the apply function only receives events the query matches.
        // Verify that an event carrying a different idempotency tag is excluded by the query.
        var projection = CourseAnnouncementProjections.IdempotencyTokenWasUsed(_otherToken);
        var evt = MakeEvent(
            new CourseAnnouncementPostedEvent(_announcementId, _courseId, "Title", "Body", _token),
            tags: ("idempotency", _token.ToString()));

        Assert.False(projection.Query.Matches(evt));
    }

    [Fact]
    public void IdempotencyTokenWasUsed_PostedThenRetracted_ReturnsFalse()
    {
        var projection = CourseAnnouncementProjections.IdempotencyTokenWasUsed(_token);
        var posted = MakeEvent(
            new CourseAnnouncementPostedEvent(_announcementId, _courseId, "Title", "Body", _token),
            position: 1,
            ("idempotency", _token.ToString()));
        var retracted = MakeEvent(
            new CourseAnnouncementRetractedEvent(_announcementId, _courseId, _token),
            position: 2,
            ("idempotency", _token.ToString()));

        var afterPost = projection.Apply(projection.InitialState, posted);
        var afterRetract = projection.Apply(afterPost, retracted);

        Assert.True(afterPost);
        Assert.False(afterRetract);
    }

    [Fact]
    public void IdempotencyTokenWasUsed_PostRetractPost_ReturnsTrue()
    {
        var projection = CourseAnnouncementProjections.IdempotencyTokenWasUsed(_token);
        var posted1 = MakeEvent(
            new CourseAnnouncementPostedEvent(_announcementId, _courseId, "Title", "Body", _token),
            position: 1,
            ("idempotency", _token.ToString()));
        var retracted = MakeEvent(
            new CourseAnnouncementRetractedEvent(_announcementId, _courseId, _token),
            position: 2,
            ("idempotency", _token.ToString()));
        var posted2 = MakeEvent(
            new CourseAnnouncementPostedEvent(Guid.NewGuid(), _courseId, "Title v2", "Body v2", _token),
            position: 3,
            ("idempotency", _token.ToString()));

        var state = projection.Apply(projection.InitialState, posted1);
        state = projection.Apply(state, retracted);
        state = projection.Apply(state, posted2);

        Assert.True(state);
    }

    // -------------------------------------------------------------------------
    // RetractableAnnouncement projection
    // -------------------------------------------------------------------------

    [Fact]
    public void RetractableAnnouncement_InitialState_IsNull()
    {
        var projection = CourseAnnouncementRetractionProjection.RetractableAnnouncement(_token);

        Assert.Null(projection.InitialState);
    }

    [Fact]
    public void RetractableAnnouncement_NoEvents_ReturnsNull()
    {
        var projection = CourseAnnouncementRetractionProjection.RetractableAnnouncement(_token);

        Assert.Null(projection.InitialState);
    }

    [Fact]
    public void RetractableAnnouncement_PostedEvent_ReturnsStateWithIsRetractedFalse()
    {
        var projection = CourseAnnouncementRetractionProjection.RetractableAnnouncement(_token);
        var evt = MakeEvent(
            new CourseAnnouncementPostedEvent(_announcementId, _courseId, "Title", "Body", _token),
            tags: ("idempotency", _token.ToString()));

        var state = projection.Apply(projection.InitialState, evt);

        Assert.NotNull(state);
        Assert.Equal(_announcementId, state!.AnnouncementId);
        Assert.Equal(_courseId, state.CourseId);
        Assert.False(state.IsRetracted);
    }

    [Fact]
    public void RetractableAnnouncement_PostedThenRetracted_SetsIsRetractedTrue()
    {
        var projection = CourseAnnouncementRetractionProjection.RetractableAnnouncement(_token);
        var posted = MakeEvent(
            new CourseAnnouncementPostedEvent(_announcementId, _courseId, "Title", "Body", _token),
            position: 1,
            ("idempotency", _token.ToString()));
        var retracted = MakeEvent(
            new CourseAnnouncementRetractedEvent(_announcementId, _courseId, _token),
            position: 2,
            ("idempotency", _token.ToString()));

        var state = projection.Apply(projection.InitialState, posted);
        state = projection.Apply(state, retracted);

        Assert.NotNull(state);
        Assert.True(state!.IsRetracted);
        Assert.Equal(_announcementId, state.AnnouncementId);
    }

    [Fact]
    public void RetractableAnnouncement_RetractedWithoutPost_ReturnsNull()
    {
        var projection = CourseAnnouncementRetractionProjection.RetractableAnnouncement(_token);
        var evt = MakeEvent(
            new CourseAnnouncementRetractedEvent(_announcementId, _courseId, _token),
            tags: ("idempotency", _token.ToString()));

        var state = projection.Apply(projection.InitialState, evt);

        Assert.Null(state);
    }
}
