using Opossum.Core;

namespace Opossum.UnitTests.Core;

/// <summary>
/// Unit tests for <see cref="Query.Matches(SequencedEvent)"/>.
/// Verifies that in-memory matching mirrors the OR/AND semantics used by the event store.
/// </summary>
public class QueryMatchesTests
{
    #region Helpers

    private static SequencedEvent MakeEvent(string eventType, params (string Key, string Value)[] tags) =>
        new()
        {
            Position = 1,
            Event = new DomainEvent
            {
                EventType = eventType,
                Event = new TestEvent(),
                Tags = tags.Select(t => new Tag { Key = t.Key, Value = t.Value }).ToList()
            }
        };

    private record TestEvent : IEvent;

    #endregion

    #region Query.All

    [Fact]
    public void Matches_QueryAll_MatchesEveryEvent()
    {
        var query = Query.All();
        var evt = MakeEvent("AnyType", ("anyKey", "anyValue"));

        Assert.True(query.Matches(evt));
    }

    [Fact]
    public void Matches_QueryAll_MatchesEventWithNoTags()
    {
        var query = Query.All();
        var evt = MakeEvent("AnyType");

        Assert.True(query.Matches(evt));
    }

    #endregion

    #region EventType filtering

    [Fact]
    public void Matches_ExactEventType_ReturnsTrue()
    {
        var query = Query.FromEventTypes("StudentRegistered");
        var evt = MakeEvent("StudentRegistered");

        Assert.True(query.Matches(evt));
    }

    [Fact]
    public void Matches_DifferentEventType_ReturnsFalse()
    {
        var query = Query.FromEventTypes("StudentRegistered");
        var evt = MakeEvent("CourseCreated");

        Assert.False(query.Matches(evt));
    }

    [Fact]
    public void Matches_MultipleTypesInQueryItem_MatchesAny()
    {
        var query = Query.FromEventTypes("StudentRegistered", "CourseCreated");

        Assert.True(query.Matches(MakeEvent("StudentRegistered")));
        Assert.True(query.Matches(MakeEvent("CourseCreated")));
        Assert.False(query.Matches(MakeEvent("SomethingElse")));
    }

    [Fact]
    public void Matches_EmptyEventTypesList_MatchesAnyType()
    {
        var query = Query.FromItems(new QueryItem { EventTypes = [] });
        var evt = MakeEvent("AnythingAtAll");

        Assert.True(query.Matches(evt));
    }

    #endregion

    #region Tag filtering

    [Fact]
    public void Matches_SingleTagMatch_ReturnsTrue()
    {
        var query = Query.FromTags(new Tag { Key = "courseId", Value = "abc" });
        var evt = MakeEvent("CourseCreated", ("courseId", "abc"));

        Assert.True(query.Matches(evt));
    }

    [Fact]
    public void Matches_SingleTag_KeyMismatch_ReturnsFalse()
    {
        var query = Query.FromTags(new Tag { Key = "courseId", Value = "abc" });
        var evt = MakeEvent("CourseCreated", ("studentId", "abc"));

        Assert.False(query.Matches(evt));
    }

    [Fact]
    public void Matches_SingleTag_ValueMismatch_ReturnsFalse()
    {
        var query = Query.FromTags(new Tag { Key = "courseId", Value = "abc" });
        var evt = MakeEvent("CourseCreated", ("courseId", "xyz"));

        Assert.False(query.Matches(evt));
    }

    [Fact]
    public void Matches_AllTagsPresent_ReturnsTrue()
    {
        var query = Query.FromItems(new QueryItem
        {
            Tags = [
                new Tag { Key = "courseId", Value = "c1" },
                new Tag { Key = "studentId", Value = "s1" }
            ]
        });
        var evt = MakeEvent("Enrolled", ("courseId", "c1"), ("studentId", "s1"));

        Assert.True(query.Matches(evt));
    }

    [Fact]
    public void Matches_OneTagMissing_ReturnsFalse()
    {
        var query = Query.FromItems(new QueryItem
        {
            Tags = [
                new Tag { Key = "courseId", Value = "c1" },
                new Tag { Key = "studentId", Value = "s1" }
            ]
        });
        var evt = MakeEvent("Enrolled", ("courseId", "c1")); // studentId tag missing

        Assert.False(query.Matches(evt));
    }

    [Fact]
    public void Matches_EmptyTagsList_MatchesAnyTags()
    {
        var query = Query.FromItems(new QueryItem { Tags = [] });
        var evt = MakeEvent("SomeType", ("anyKey", "anyValue"));

        Assert.True(query.Matches(evt));
    }

    [Fact]
    public void Matches_EventHasExtraUnrelatedTags_StillMatchesRequired()
    {
        var query = Query.FromTags(new Tag { Key = "courseId", Value = "c1" });
        var evt = MakeEvent("CourseCreated", ("courseId", "c1"), ("region", "EU"), ("tenantId", "t1"));

        Assert.True(query.Matches(evt));
    }

    #endregion

    #region Combined type + tag filtering (AND within QueryItem)

    [Fact]
    public void Matches_CorrectTypeAndTag_ReturnsTrue()
    {
        var query = Query.FromItems(new QueryItem
        {
            EventTypes = ["StudentEnrolled"],
            Tags = [new Tag { Key = "courseId", Value = "c1" }]
        });
        var evt = MakeEvent("StudentEnrolled", ("courseId", "c1"));

        Assert.True(query.Matches(evt));
    }

    [Fact]
    public void Matches_CorrectTypeButWrongTag_ReturnsFalse()
    {
        var query = Query.FromItems(new QueryItem
        {
            EventTypes = ["StudentEnrolled"],
            Tags = [new Tag { Key = "courseId", Value = "c1" }]
        });
        var evt = MakeEvent("StudentEnrolled", ("courseId", "c2"));

        Assert.False(query.Matches(evt));
    }

    [Fact]
    public void Matches_CorrectTagButWrongType_ReturnsFalse()
    {
        var query = Query.FromItems(new QueryItem
        {
            EventTypes = ["StudentEnrolled"],
            Tags = [new Tag { Key = "courseId", Value = "c1" }]
        });
        var evt = MakeEvent("CourseCreated", ("courseId", "c1"));

        Assert.False(query.Matches(evt));
    }

    #endregion

    #region Multiple QueryItems (OR logic)

    [Fact]
    public void Matches_SecondQueryItemMatches_ReturnsTrue()
    {
        var query = Query.FromItems(
            new QueryItem { EventTypes = ["StudentRegistered"] },
            new QueryItem { EventTypes = ["CourseCreated"] });

        Assert.True(query.Matches(MakeEvent("StudentRegistered")));
        Assert.True(query.Matches(MakeEvent("CourseCreated")));
    }

    [Fact]
    public void Matches_NeitherQueryItemMatches_ReturnsFalse()
    {
        var query = Query.FromItems(
            new QueryItem { EventTypes = ["StudentRegistered"] },
            new QueryItem { EventTypes = ["CourseCreated"] });

        Assert.False(query.Matches(MakeEvent("PaymentProcessed")));
    }

    [Fact]
    public void Matches_ComplexMultiItemQuery_CorrectlyEvaluates()
    {
        // Query: (type=StudentEnrolled AND courseId=c1) OR (type=StudentRegistered AND studentId=s1)
        var query = Query.FromItems(
            new QueryItem
            {
                EventTypes = ["StudentEnrolled"],
                Tags = [new Tag { Key = "courseId", Value = "c1" }]
            },
            new QueryItem
            {
                EventTypes = ["StudentRegistered"],
                Tags = [new Tag { Key = "studentId", Value = "s1" }]
            });

        // Matches item 1
        Assert.True(query.Matches(MakeEvent("StudentEnrolled", ("courseId", "c1"))));
        // Matches item 2
        Assert.True(query.Matches(MakeEvent("StudentRegistered", ("studentId", "s1"))));
        // Matches neither
        Assert.False(query.Matches(MakeEvent("StudentEnrolled", ("courseId", "c2"))));
        Assert.False(query.Matches(MakeEvent("StudentRegistered", ("studentId", "s2"))));
    }

    #endregion

    #region Guard clauses

    [Fact]
    public void Matches_NullEvent_ThrowsArgumentNullException()
    {
        var query = Query.All();

        Assert.Throws<ArgumentNullException>(() => query.Matches(null!));
    }

    #endregion
}
