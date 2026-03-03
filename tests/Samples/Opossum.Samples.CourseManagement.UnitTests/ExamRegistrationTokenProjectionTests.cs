using Opossum.Core;
using Opossum.Samples.CourseManagement.Events;
using Opossum.Samples.CourseManagement.ExamRegistration;

namespace Opossum.Samples.CourseManagement.UnitTests;

/// <summary>
/// Unit tests for the Exam Registration Token Decision Model projections.
///
/// Covers the DCB "Opt-In Token" pattern:
/// https://dcb.events/examples/opt-in-token/
///
/// All tests are pure in-memory folds — no event store, no file system.
/// </summary>
public class ExamRegistrationTokenProjectionTests
{
    private readonly Guid _tokenId = Guid.NewGuid();
    private readonly Guid _otherTokenId = Guid.NewGuid();
    private readonly Guid _examId = Guid.NewGuid();
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
                Tags = [.. tags.Select(t => new Tag(t.Key, t.Value))]
            }
        };

    // -------------------------------------------------------------------------
    // TokenStatus — initial state
    // -------------------------------------------------------------------------

    [Fact]
    public void TokenStatus_InitialState_IsNotIssued()
    {
        var projection = ExamRegistrationTokenProjections.TokenStatus(_tokenId);

        Assert.Equal(ExamTokenStatus.NotIssued, projection.InitialState.Status);
    }

    [Fact]
    public void TokenStatus_InitialState_ExamIdIsEmpty()
    {
        var projection = ExamRegistrationTokenProjections.TokenStatus(_tokenId);

        Assert.Equal(Guid.Empty, projection.InitialState.ExamId);
    }

    // -------------------------------------------------------------------------
    // TokenStatus — query
    // -------------------------------------------------------------------------

    [Fact]
    public void TokenStatus_Query_ContainsExamTokenTag()
    {
        var projection = ExamRegistrationTokenProjections.TokenStatus(_tokenId);

        var tag = projection.Query.QueryItems
            .SelectMany(qi => qi.Tags)
            .Single(t => t.Key == "examToken");

        Assert.Equal(_tokenId.ToString(), tag.Value);
    }

    [Fact]
    public void TokenStatus_Query_DoesNotMatchEventWithDifferentTokenTag()
    {
        var projection = ExamRegistrationTokenProjections.TokenStatus(_otherTokenId);
        var evt = MakeEvent(
            new ExamRegistrationTokenIssuedEvent(_tokenId, _examId, _courseId),
            tags: ("examToken", _tokenId.ToString()));

        Assert.False(projection.Query.Matches(evt));
    }

    // -------------------------------------------------------------------------
    // TokenStatus — apply: Issued
    // -------------------------------------------------------------------------

    [Fact]
    public void TokenStatus_ApplyIssuedEvent_StatusBecomesIssued()
    {
        var projection = ExamRegistrationTokenProjections.TokenStatus(_tokenId);
        var evt = MakeEvent(
            new ExamRegistrationTokenIssuedEvent(_tokenId, _examId, _courseId),
            tags: ("examToken", _tokenId.ToString()));

        var state = projection.Apply(projection.InitialState, evt);

        Assert.Equal(ExamTokenStatus.Issued, state.Status);
    }

    [Fact]
    public void TokenStatus_ApplyIssuedEvent_ExamIdIsSetFromEvent()
    {
        var projection = ExamRegistrationTokenProjections.TokenStatus(_tokenId);
        var evt = MakeEvent(
            new ExamRegistrationTokenIssuedEvent(_tokenId, _examId, _courseId),
            tags: ("examToken", _tokenId.ToString()));

        var state = projection.Apply(projection.InitialState, evt);

        Assert.Equal(_examId, state.ExamId);
    }

    // -------------------------------------------------------------------------
    // TokenStatus — apply: Issued → Redeemed
    // -------------------------------------------------------------------------

    [Fact]
    public void TokenStatus_ApplyIssuedThenRedeemed_StatusBecomesRedeemed()
    {
        var projection = ExamRegistrationTokenProjections.TokenStatus(_tokenId);
        var issued = MakeEvent(
            new ExamRegistrationTokenIssuedEvent(_tokenId, _examId, _courseId),
            position: 1,
            ("examToken", _tokenId.ToString()));
        var redeemed = MakeEvent(
            new ExamRegistrationTokenRedeemedEvent(_tokenId, _examId, _studentId),
            position: 2,
            ("examToken", _tokenId.ToString()));

        var afterIssue = projection.Apply(projection.InitialState, issued);
        var afterRedeem = projection.Apply(afterIssue, redeemed);

        Assert.Equal(ExamTokenStatus.Redeemed, afterRedeem.Status);
    }

    [Fact]
    public void TokenStatus_ApplyIssuedThenRedeemed_ExamIdIsPreserved()
    {
        var projection = ExamRegistrationTokenProjections.TokenStatus(_tokenId);
        var issued = MakeEvent(
            new ExamRegistrationTokenIssuedEvent(_tokenId, _examId, _courseId),
            position: 1,
            ("examToken", _tokenId.ToString()));
        var redeemed = MakeEvent(
            new ExamRegistrationTokenRedeemedEvent(_tokenId, _examId, _studentId),
            position: 2,
            ("examToken", _tokenId.ToString()));

        var afterIssue = projection.Apply(projection.InitialState, issued);
        var afterRedeem = projection.Apply(afterIssue, redeemed);

        Assert.Equal(_examId, afterRedeem.ExamId);
    }

    // -------------------------------------------------------------------------
    // TokenStatus — apply: Issued → Revoked
    // -------------------------------------------------------------------------

    [Fact]
    public void TokenStatus_ApplyIssuedThenRevoked_StatusBecomesRevoked()
    {
        var projection = ExamRegistrationTokenProjections.TokenStatus(_tokenId);
        var issued = MakeEvent(
            new ExamRegistrationTokenIssuedEvent(_tokenId, _examId, _courseId),
            position: 1,
            ("examToken", _tokenId.ToString()));
        var revoked = MakeEvent(
            new ExamRegistrationTokenRevokedEvent(_tokenId, _examId),
            position: 2,
            ("examToken", _tokenId.ToString()));

        var afterIssue = projection.Apply(projection.InitialState, issued);
        var afterRevoke = projection.Apply(afterIssue, revoked);

        Assert.Equal(ExamTokenStatus.Revoked, afterRevoke.Status);
    }

    [Fact]
    public void TokenStatus_ApplyIssuedThenRevoked_ExamIdIsPreserved()
    {
        var projection = ExamRegistrationTokenProjections.TokenStatus(_tokenId);
        var issued = MakeEvent(
            new ExamRegistrationTokenIssuedEvent(_tokenId, _examId, _courseId),
            position: 1,
            ("examToken", _tokenId.ToString()));
        var revoked = MakeEvent(
            new ExamRegistrationTokenRevokedEvent(_tokenId, _examId),
            position: 2,
            ("examToken", _tokenId.ToString()));

        var afterIssue = projection.Apply(projection.InitialState, issued);
        var afterRevoke = projection.Apply(afterIssue, revoked);

        Assert.Equal(_examId, afterRevoke.ExamId);
    }

    // -------------------------------------------------------------------------
    // CourseExists projection
    // -------------------------------------------------------------------------

    [Fact]
    public void CourseExists_InitialState_IsFalse()
    {
        var projection = ExamRegistrationTokenProjections.CourseExists(_courseId);

        Assert.False(projection.InitialState);
    }

    [Fact]
    public void CourseExists_Query_ContainsCourseIdTag()
    {
        var projection = ExamRegistrationTokenProjections.CourseExists(_courseId);

        var tag = projection.Query.QueryItems
            .SelectMany(qi => qi.Tags)
            .Single(t => t.Key == "courseId");

        Assert.Equal(_courseId.ToString(), tag.Value);
    }

    [Fact]
    public void CourseExists_Apply_CourseCreatedEvent_ReturnsTrue()
    {
        var projection = ExamRegistrationTokenProjections.CourseExists(_courseId);
        var evt = MakeEvent(
            new CourseCreatedEvent(_courseId, "Advanced Maths", "Exam prep", 30),
            tags: ("courseId", _courseId.ToString()));

        var state = projection.Apply(projection.InitialState, evt);

        Assert.True(state);
    }
}
