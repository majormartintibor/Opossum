using Opossum.Core;
using Opossum.Projections;

namespace Opossum.UnitTests.Projections;

/// <summary>
/// Unit tests for IProjectionWithRelatedEvents behavior and framework integration.
/// Tests the projection manager's handling of related event queries.
/// </summary>
public class ProjectionWithRelatedEventsTests
{
    [Fact]
    public void ProjectionWithRelatedEvents_BaseApplyMethod_ThrowsNotImplementedException()
    {
        // Arrange
        var projection = new TestProjectionWithRelatedEvents();
        var evt = CreateSequencedEvent(new TestEvent("test"));

        // Act & Assert
        Assert.Throws<NotImplementedException>(() =>
            ((IProjectionDefinition<TestState>)projection).Apply(null, evt));
    }

    [Fact]
    public void ProjectionWithRelatedEvents_GetRelatedEventsQuery_ReturnsCorrectQuery()
    {
        // Arrange
        var projection = new TestProjectionWithRelatedEvents();
        var evt = CreateSequencedEvent(new TestEventWithRelation("test", Guid.NewGuid()));

        // Act
        var query = projection.GetRelatedEventsQuery(evt);

        // Assert
        Assert.NotNull(query);
        Assert.Single(query.QueryItems);
        Assert.Contains(nameof(RelatedEvent), query.QueryItems[0].EventTypes);
    }

    [Fact]
    public void ProjectionWithRelatedEvents_GetRelatedEventsQuery_WithNoRelationship_ReturnsNull()
    {
        // Arrange
        var projection = new TestProjectionWithRelatedEvents();
        var evt = CreateSequencedEvent(new TestEvent("test"));

        // Act
        var query = projection.GetRelatedEventsQuery(evt);

        // Assert
        Assert.Null(query);
    }

    [Fact]
    public void ProjectionWithRelatedEvents_Apply_WithRelatedEvents_UsesRelatedData()
    {
        // Arrange
        var projection = new TestProjectionWithRelatedEvents();
        var relatedId = Guid.NewGuid();
        var evt = CreateSequencedEvent(new TestEventWithRelation("main", relatedId));

        var relatedEvent = new SequencedEvent
        {
            Position = 1,
            Event = new DomainEvent
            {
                EventType = nameof(RelatedEvent),
                Event = new RelatedEvent(relatedId, "Related Data"),
                Tags = []
            },
            Metadata = new Metadata { Timestamp = DateTimeOffset.UtcNow }
        };

        var relatedEvents = new[] { relatedEvent };

        // Act
        var state = projection.Apply(null, evt, relatedEvents);

        // Assert
        Assert.NotNull(state);
        Assert.Equal("main", state.MainData);
        Assert.Equal("Related Data", state.RelatedData);
    }

    [Fact]
    public void ProjectionWithRelatedEvents_Apply_WithoutRequiredRelatedEvents_ThrowsException()
    {
        // Arrange
        var projection = new TestProjectionWithRelatedEvents();
        var evt = CreateSequencedEvent(new TestEventWithRelation("main", Guid.NewGuid()));
        var emptyRelatedEvents = Array.Empty<SequencedEvent>();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            projection.Apply(null, evt, emptyRelatedEvents));

        Assert.Contains("Related event not found", exception.Message);
    }

    [Fact]
    public void ProjectionWithRelatedEvents_Apply_UpdatesExistingState()
    {
        // Arrange
        var projection = new TestProjectionWithRelatedEvents();
        var existingState = new TestState
        {
            MainData = "old",
            RelatedData = "old related",
            UpdateCount = 1
        };

        var evt = CreateSequencedEvent(new TestEvent("updated"));

        // Act
        var newState = projection.Apply(existingState, evt, []);

        // Assert
        Assert.NotNull(newState);
        Assert.Equal("updated", newState.MainData);
        Assert.Equal("old related", newState.RelatedData); // Preserved
        Assert.Equal(2, newState.UpdateCount); // Incremented
    }

    [Fact]
    public void ProjectionWithRelatedEvents_Apply_WithMultipleRelatedEvents_UsesLatest()
    {
        // Arrange
        var projection = new TestProjectionWithRelatedEvents();
        var relatedId = Guid.NewGuid();
        var evt = CreateSequencedEvent(new TestEventWithRelation("main", relatedId));

        var evt1 = new SequencedEvent
        {
            Position = 1,
            Event = new DomainEvent { EventType = nameof(RelatedEvent), Event = new RelatedEvent(relatedId, "First"), Tags = [] },
            Metadata = new Metadata { Timestamp = DateTimeOffset.UtcNow }
        };

        var evt2 = new SequencedEvent
        {
            Position = 2,
            Event = new DomainEvent { EventType = nameof(RelatedEvent), Event = new RelatedEvent(relatedId, "Second"), Tags = [] },
            Metadata = new Metadata { Timestamp = DateTimeOffset.UtcNow }
        };

        var evt3 = new SequencedEvent
        {
            Position = 3,
            Event = new DomainEvent { EventType = nameof(RelatedEvent), Event = new RelatedEvent(relatedId, "Latest"), Tags = [] },
            Metadata = new Metadata { Timestamp = DateTimeOffset.UtcNow }
        };

        var relatedEvents = new[] { evt1, evt2, evt3 };

        // Act
        var state = projection.Apply(null, evt, relatedEvents);

        // Assert
        Assert.NotNull(state);
        Assert.Equal("Latest", state.RelatedData); // Should use last one
    }

    private static SequencedEvent CreateSequencedEvent(IEvent innerEvent) =>
        new()
        {
            Position = 1,
            Event = new DomainEvent
            {
                EventType = innerEvent.GetType().Name,
                Event = innerEvent,
                Tags = []
            },
            Metadata = new Metadata { Timestamp = DateTimeOffset.UtcNow }
        };
}

// ============================================================================
// TEST TYPES
// ============================================================================

public record TestState
{
    public string MainData { get; init; } = string.Empty;
    public string RelatedData { get; init; } = string.Empty;
    public int UpdateCount { get; init; }
}

public record TestEvent(string Data) : IEvent;
public record TestEventWithRelation(string Data, Guid RelatedId) : IEvent;
public record RelatedEvent(Guid Id, string Data) : IEvent;

public class TestProjectionWithRelatedEvents : IProjectionWithRelatedEvents<TestState>
{
    public string ProjectionName => "TestProjectionWithRelatedEvents";

    public string[] EventTypes => [
        nameof(TestEvent),
        nameof(TestEventWithRelation)
    ];

    public string KeySelector(SequencedEvent evt)
    {
        return evt.Position.ToString(); // Simple key for testing
    }

    public Query? GetRelatedEventsQuery(SequencedEvent evt)
    {
        return evt.Event.Event switch
        {
            TestEventWithRelation twr => Query.FromItems(new QueryItem
            {
                Tags = [new Tag("relatedId", twr.RelatedId.ToString())],
                EventTypes = [nameof(RelatedEvent)]
            }),
            _ => null
        };
    }

    public TestState? Apply(TestState? current, SequencedEvent evt, SequencedEvent[] relatedEvents)
    {
        return evt.Event.Event switch
        {
            TestEvent te => ApplyTestEvent(current, te),
            TestEventWithRelation twr => ApplyTestEventWithRelation(twr, relatedEvents),
            _ => current
        };
    }

    private TestState ApplyTestEvent(TestState? current, TestEvent evt)
    {
        if (current == null)
        {
            return new TestState
            {
                MainData = evt.Data,
                UpdateCount = 1
            };
        }

        return current with
        {
            MainData = evt.Data,
            UpdateCount = current.UpdateCount + 1
        };
    }

    private TestState ApplyTestEventWithRelation(TestEventWithRelation evt, SequencedEvent[] relatedEvents)
    {
        // Find the related event (use latest if multiple)
        var relatedEvent = relatedEvents
            .OrderByDescending(e => e.Position)
            .Select(e => e.Event.Event)
            .OfType<RelatedEvent>()
            .FirstOrDefault() ?? throw new InvalidOperationException("Related event not found");

        return new TestState
        {
            MainData = evt.Data,
            RelatedData = relatedEvent.Data,
            UpdateCount = 1
        };
    }
}
