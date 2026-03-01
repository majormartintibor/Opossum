using Opossum.Core;
using Opossum.DecisionModel;

namespace Opossum.UnitTests.DecisionModel;

/// <summary>
/// Unit tests for <see cref="IDecisionProjection{TState}"/> and <see cref="DecisionProjection{TState}"/>.
/// All tests are pure — no file system, no event store, no DI container.
/// </summary>
public class DecisionProjectionTests
{
    #region Helpers

    private record TestEvent(string Name) : IEvent;
    private record OtherEvent(int Value) : IEvent;

    private static SequencedEvent MakeSequencedEvent(IEvent payload, long position = 1, params (string Key, string Value)[] tags) =>
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

    private static Query AnyQuery() => Query.All();

    #endregion

    #region Constructor guard clauses

    [Fact]
    public void Constructor_NullQuery_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DecisionProjection<bool>(false, null!, (s, _) => s));
    }

    [Fact]
    public void Constructor_NullApply_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DecisionProjection<bool>(false, AnyQuery(), null!));
    }

    [Fact]
    public void Constructor_NullInitialState_IsAllowed_ForReferenceTypes()
    {
        // null is a valid initial state for reference types (projection not yet started)
        var projection = new DecisionProjection<string?>(
            initialState: null,
            query: AnyQuery(),
            apply: (s, _) => s);

        Assert.Null(projection.InitialState);
    }

    #endregion

    #region Property accessors

    [Fact]
    public void InitialState_ReturnsValuePassedToConstructor()
    {
        var projection = new DecisionProjection<int>(42, AnyQuery(), (s, _) => s);

        Assert.Equal(42, projection.InitialState);
    }

    [Fact]
    public void InitialState_False_ReturnsCorrectly()
    {
        var projection = new DecisionProjection<bool>(false, AnyQuery(), (s, _) => s);

        Assert.False(projection.InitialState);
    }

    [Fact]
    public void Query_ReturnsQueryPassedToConstructor()
    {
        var query = Query.FromEventTypes("CourseCreated");
        var projection = new DecisionProjection<bool>(false, query, (s, _) => s);

        Assert.Same(query, projection.Query);
    }

    #endregion

    #region Apply — single event

    [Fact]
    public void Apply_DelegateIsInvokedWithCorrectStateAndEvent()
    {
        bool capturedState = true;
        SequencedEvent? capturedEvt = null;

        var projection = new DecisionProjection<bool>(
            initialState: false,
            query: AnyQuery(),
            apply: (s, e) =>
            {
                capturedState = s;
                capturedEvt = e;
                return true;
            });

        var evt = MakeSequencedEvent(new TestEvent("x"), position: 5);
        projection.Apply(false, evt);

        Assert.False(capturedState);
        Assert.Same(evt, capturedEvt);
    }

    [Fact]
    public void Apply_ReturnsValueFromDelegate()
    {
        var projection = new DecisionProjection<int>(
            initialState: 0,
            query: AnyQuery(),
            apply: (s, _) => s + 10);

        var result = projection.Apply(0, MakeSequencedEvent(new TestEvent("x")));

        Assert.Equal(10, result);
    }

    [Fact]
    public void Apply_WhenEventTypeDoesNotMatch_CanReturnUnchangedState()
    {
        var projection = new DecisionProjection<bool>(
            initialState: false,
            query: AnyQuery(),
            apply: (state, evt) => evt.Event.Event is TestEvent || state);

        var result = projection.Apply(false, MakeSequencedEvent(new OtherEvent(99)));

        Assert.False(result);
    }

    #endregion

    #region Apply — sequential fold (multiple events)

    [Fact]
    public void Apply_MultipleCalls_FoldsStateCorrectly()
    {
        var projection = new DecisionProjection<int>(
            initialState: 0,
            query: AnyQuery(),
            apply: (state, evt) => evt.Event.Event is TestEvent ? state + 1 : state);

        var events = new[]
        {
            MakeSequencedEvent(new TestEvent("a"), position: 1),
            MakeSequencedEvent(new TestEvent("b"), position: 2),
            MakeSequencedEvent(new OtherEvent(1), position: 3),  // ignored
            MakeSequencedEvent(new TestEvent("c"), position: 4)
        };

        var state = events.Aggregate(projection.InitialState, projection.Apply);

        Assert.Equal(3, state);
    }

    [Fact]
    public void Apply_EmptyEventSequence_ReturnsInitialState()
    {
        var projection = new DecisionProjection<int>(
            initialState: 7,
            query: AnyQuery(),
            apply: (state, _) => state + 1);

        var state = Array.Empty<SequencedEvent>().Aggregate(projection.InitialState, projection.Apply);

        Assert.Equal(7, state);
    }

    [Fact]
    public void Apply_BoolProjection_FullSequence_ReflectsLastRelevantEvent()
    {
        // Projection: course exists (true on Created, false on Archived)
        var projection = new DecisionProjection<bool>(
            initialState: false,
            query: AnyQuery(),
            apply: (_, evt) => evt.Event.Event switch
            {
                TestEvent { Name: "Created" } => true,
                TestEvent { Name: "Archived" } => false,
                _ => false
            });

        var events = new[]
        {
            MakeSequencedEvent(new TestEvent("Created"), position: 1),
            MakeSequencedEvent(new TestEvent("Archived"), position: 2),
            MakeSequencedEvent(new TestEvent("Created"), position: 3)
        };

        var state = events.Aggregate(projection.InitialState, projection.Apply);

        Assert.True(state);
    }

    #endregion

    #region Factory function pattern

    [Fact]
    public void FactoryFunction_ProducesCorrectProjection()
    {
        static IDecisionProjection<bool> CourseExists(Guid courseId) =>
            new DecisionProjection<bool>(
                initialState: false,
                query: Query.FromItems(new QueryItem
                {
                    EventTypes = ["CourseCreated"],
                    Tags = [new Tag("courseId", courseId.ToString())]
                }),
                apply: (state, evt) => evt.Event.Event is TestEvent { Name: "created" } || state);

        var id = Guid.NewGuid();
        var projection = CourseExists(id);

        Assert.False(projection.InitialState);
        Assert.Single(projection.Query.QueryItems);
        Assert.Contains("CourseCreated", projection.Query.QueryItems[0].EventTypes);
        Assert.Equal("courseId", projection.Query.QueryItems[0].Tags[0].Key);
        Assert.Equal(id.ToString(), projection.Query.QueryItems[0].Tags[0].Value);
    }

    [Fact]
    public void TwoFactoryInstances_WithDifferentIds_HaveDistinctQueries()
    {
        static IDecisionProjection<bool> CourseExists(Guid courseId) =>
            new DecisionProjection<bool>(
                initialState: false,
                query: Query.FromTags(new Tag("courseId", courseId.ToString())),
                apply: (s, _) => s);

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var p1 = CourseExists(id1);
        var p2 = CourseExists(id2);

        Assert.NotEqual(
            p1.Query.QueryItems[0].Tags[0].Value,
            p2.Query.QueryItems[0].Tags[0].Value);
    }

    #endregion

    #region Two independent projections over the same events

    [Fact]
    public void TwoProjections_SameEvents_ProduceIndependentStates()
    {
        // Projection A: count TestEvents
        var countProjection = new DecisionProjection<int>(
            initialState: 0,
            query: Query.FromEventTypes(nameof(TestEvent)),
            apply: (state, evt) => evt.Event.Event is TestEvent ? state + 1 : state);

        // Projection B: track last OtherEvent value
        var lastValueProjection = new DecisionProjection<int>(
            initialState: -1,
            query: Query.FromEventTypes(nameof(OtherEvent)),
            apply: (state, evt) => evt.Event.Event is OtherEvent o ? o.Value : state);

        var events = new[]
        {
            MakeSequencedEvent(new TestEvent("a"), position: 1),
            MakeSequencedEvent(new OtherEvent(10), position: 2),
            MakeSequencedEvent(new TestEvent("b"), position: 3),
            MakeSequencedEvent(new OtherEvent(42), position: 4)
        };

        var countState = events
            .Where(e => countProjection.Query.Matches(e))
            .Aggregate(countProjection.InitialState, countProjection.Apply);

        var lastValueState = events
            .Where(e => lastValueProjection.Query.Matches(e))
            .Aggregate(lastValueProjection.InitialState, lastValueProjection.Apply);

        Assert.Equal(2, countState);
        Assert.Equal(42, lastValueState);
    }

    #endregion

    #region TimeProvider constructor overload

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static SequencedEvent MakeTimestampedEvent(IEvent payload, DateTimeOffset timestamp, long position = 1) =>
        new()
        {
            Position = position,
            Event = new DomainEvent
            {
                EventType = payload.GetType().Name,
                Event = payload,
                Tags = []
            },
            Metadata = new Opossum.Core.Metadata { Timestamp = timestamp }
        };

    [Fact]
    public void TimeProviderOverload_NullQuery_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DecisionProjection<bool>(false, null!, (s, _, _) => s));
    }

    [Fact]
    public void TimeProviderOverload_NullApply_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DecisionProjection<bool>(false, AnyQuery(), (Func<bool, SequencedEvent, TimeProvider, bool>)null!));
    }

    [Fact]
    public void TimeProviderOverload_NullTimeProvider_DefaultsToSystemTimeProvider()
    {
        // The apply delegate must be called (no NullReferenceException) when timeProvider is null
        var called = false;
        var projection = new DecisionProjection<bool>(
            initialState: false,
            query: AnyQuery(),
            apply: (_, _, tp) =>
            {
                called = true;
                Assert.NotNull(tp);
                return true;
            },
            timeProvider: null);

        projection.Apply(false, MakeSequencedEvent(new TestEvent("x")));

        Assert.True(called);
    }

    [Fact]
    public void TimeProviderOverload_CustomTimeProvider_IsPassedToApply()
    {
        var fixedTime = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var tp = new FixedTimeProvider(fixedTime);
        DateTimeOffset? capturedNow = null;

        var projection = new DecisionProjection<bool>(
            initialState: false,
            query: AnyQuery(),
            apply: (_, _, provider) =>
            {
                capturedNow = provider.GetUtcNow();
                return true;
            },
            timeProvider: tp);

        projection.Apply(false, MakeSequencedEvent(new TestEvent("x")));

        Assert.Equal(fixedTime, capturedNow);
    }

    [Fact]
    public void TimeProviderOverload_WithinGracePeriod_ReturnsNewState()
    {
        var eventTime = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var gracePeriod = TimeSpan.FromMinutes(30);
        // "now" is 5 minutes after the event — within grace
        var tp = new FixedTimeProvider(eventTime + TimeSpan.FromMinutes(5));

        var projection = new DecisionProjection<bool>(
            initialState: false,
            query: AnyQuery(),
            apply: (_, evt, provider) =>
            {
                var age = provider.GetUtcNow() - evt.Metadata.Timestamp;
                return age <= gracePeriod;
            },
            timeProvider: tp);

        var evt = MakeTimestampedEvent(new TestEvent("priceChanged"), eventTime);
        var result = projection.Apply(projection.InitialState, evt);

        Assert.True(result);
    }

    [Fact]
    public void TimeProviderOverload_AfterGracePeriodExpired_ReturnsOldState()
    {
        var eventTime = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var gracePeriod = TimeSpan.FromMinutes(30);
        // "now" is 2 hours after the event — grace period expired
        var tp = new FixedTimeProvider(eventTime + TimeSpan.FromHours(2));

        var projection = new DecisionProjection<bool>(
            initialState: false,
            query: AnyQuery(),
            apply: (_, evt, provider) =>
            {
                var age = provider.GetUtcNow() - evt.Metadata.Timestamp;
                return age <= gracePeriod;
            },
            timeProvider: tp);

        var evt = MakeTimestampedEvent(new TestEvent("priceChanged"), eventTime);
        var result = projection.Apply(projection.InitialState, evt);

        Assert.False(result);
    }

    [Fact]
    public void TimeProviderOverload_InitialStateAndQueryAccessible()
    {
        var query = Query.FromEventTypes("TestEvent");
        var projection = new DecisionProjection<int>(
            initialState: 42,
            query: query,
            apply: (s, _, _) => s,
            timeProvider: null);

        Assert.Equal(42, projection.InitialState);
        Assert.Same(query, projection.Query);
    }

    #endregion
}
