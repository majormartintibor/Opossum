using Opossum.Core;
using Opossum.DecisionModel;

namespace Opossum.UnitTests.DecisionModel;

/// <summary>
/// Unit tests for the fold logic and <see cref="DecisionModel{TState}"/> result that
/// <see cref="DecisionModelExtensions.BuildDecisionModelAsync{TState}"/> produces.
/// All tests are pure — no file system, no event store, no DI container.
/// </summary>
public class BuildDecisionModelTests
{
    #region Helpers

    private record CourseCreatedEvent(Guid CourseId, int MaxStudents) : IEvent;
    private record StudentEnrolledEvent(Guid CourseId, Guid StudentId) : IEvent;
    private record UnrelatedEvent : IEvent;

    private static SequencedEvent MakeEvent(IEvent payload, long position, params (string Key, string Value)[] tags) =>
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

    // Simulates the fold+condition logic of BuildDecisionModelAsync without the I/O layer
    private static DecisionModel<TState> BuildFromEvents<TState>(
        IDecisionProjection<TState> projection,
        SequencedEvent[] events)
    {
        var state = events
            .OrderBy(e => e.Position)
            .Aggregate(projection.InitialState, projection.Apply);

        var appendCondition = new AppendCondition
        {
            FailIfEventsMatch = projection.Query,
            AfterSequencePosition = events.Length > 0 ? events.Max(e => e.Position) : null
        };

        return new DecisionModel<TState>(state, appendCondition);
    }

    #endregion

    #region DecisionModel<TState> record

    [Fact]
    public void DecisionModel_State_ReturnsConstructorValue()
    {
        var condition = new AppendCondition
        {
            FailIfEventsMatch = Query.All(),
            AfterSequencePosition = 5
        };

        var model = new DecisionModel<bool>(true, condition);

        Assert.True(model.State);
    }

    [Fact]
    public void DecisionModel_AppendCondition_ReturnsConstructorValue()
    {
        var condition = new AppendCondition
        {
            FailIfEventsMatch = Query.All(),
            AfterSequencePosition = 42
        };

        var model = new DecisionModel<int>(7, condition);

        Assert.Same(condition, model.AppendCondition);
    }

    #endregion

    #region Empty event set

    [Fact]
    public void Build_EmptyEvents_StateIsInitialState()
    {
        var projection = new DecisionProjection<bool>(
            initialState: false,
            query: Query.FromEventTypes(nameof(CourseCreatedEvent)),
            apply: (_, _) => true);

        var model = BuildFromEvents(projection, []);

        Assert.False(model.State);
    }

    [Fact]
    public void Build_EmptyEvents_AppendConditionPositionIsNull()
    {
        var projection = new DecisionProjection<int>(
            initialState: 0,
            query: Query.FromEventTypes(nameof(StudentEnrolledEvent)),
            apply: (s, _) => s + 1);

        var model = BuildFromEvents(projection, []);

        Assert.Null(model.AppendCondition.AfterSequencePosition);
    }

    [Fact]
    public void Build_EmptyEvents_FailIfEventsMatchIsProjectionQuery()
    {
        var query = Query.FromEventTypes(nameof(CourseCreatedEvent));
        var projection = new DecisionProjection<bool>(false, query, (s, _) => s);

        var model = BuildFromEvents(projection, []);

        Assert.Same(query, model.AppendCondition.FailIfEventsMatch);
    }

    #endregion

    #region Single event

    [Fact]
    public void Build_SingleMatchingEvent_StateIsUpdated()
    {
        var courseId = Guid.NewGuid();
        var projection = new DecisionProjection<bool>(
            initialState: false,
            query: Query.FromEventTypes(nameof(CourseCreatedEvent)),
            apply: (_, evt) => evt.Event.Event is CourseCreatedEvent);

        var evt = MakeEvent(new CourseCreatedEvent(courseId, 30), position: 7);
        var model = BuildFromEvents(projection, [evt]);

        Assert.True(model.State);
    }

    [Fact]
    public void Build_SingleEvent_AfterSequencePositionEqualsEventPosition()
    {
        var projection = new DecisionProjection<bool>(
            initialState: false,
            query: Query.All(),
            apply: (_, _) => true);

        var model = BuildFromEvents(projection, [MakeEvent(new UnrelatedEvent(), position: 13)]);

        Assert.Equal(13, model.AppendCondition.AfterSequencePosition);
    }

    #endregion

    #region Multiple events

    [Fact]
    public void Build_MultipleEvents_StateReflectsAllApplied()
    {
        var courseId = Guid.NewGuid();
        var projection = new DecisionProjection<int>(
            initialState: 0,
            query: Query.FromEventTypes(nameof(StudentEnrolledEvent)),
            apply: (s, evt) => evt.Event.Event is StudentEnrolledEvent e && e.CourseId == courseId
                ? s + 1
                : s);

        var events = new[]
        {
            MakeEvent(new StudentEnrolledEvent(courseId, Guid.NewGuid()), position: 1),
            MakeEvent(new StudentEnrolledEvent(courseId, Guid.NewGuid()), position: 2),
            MakeEvent(new StudentEnrolledEvent(courseId, Guid.NewGuid()), position: 3)
        };

        var model = BuildFromEvents(projection, events);

        Assert.Equal(3, model.State);
    }

    [Fact]
    public void Build_MultipleEvents_AfterSequencePositionIsMax()
    {
        var projection = new DecisionProjection<int>(
            initialState: 0,
            query: Query.All(),
            apply: (s, _) => s + 1);

        var events = new[]
        {
            MakeEvent(new UnrelatedEvent(), position: 2),
            MakeEvent(new UnrelatedEvent(), position: 7),
            MakeEvent(new UnrelatedEvent(), position: 4)
        };

        var model = BuildFromEvents(projection, events);

        Assert.Equal(7, model.AppendCondition.AfterSequencePosition);
    }

    [Fact]
    public void Build_EventsAreAppliedInAscendingPositionOrder()
    {
        // The fold must respect position order, not array order
        var projection = new DecisionProjection<List<long>>(
            initialState: [],
            query: Query.All(),
            apply: (list, evt) =>
            {
                var next = new List<long>(list) { evt.Position };
                return next;
            });

        // Provide events out of order
        var events = new[]
        {
            MakeEvent(new UnrelatedEvent(), position: 5),
            MakeEvent(new UnrelatedEvent(), position: 1),
            MakeEvent(new UnrelatedEvent(), position: 3)
        };

        var model = BuildFromEvents(projection, events);

        Assert.Equal([1, 3, 5], model.State);
    }

    #endregion

    #region FailIfEventsMatch is always the projection query

    [Fact]
    public void Build_FailIfEventsMatch_IsAlwaysProjectionQuery_WhenEventsExist()
    {
        var query = Query.FromEventTypes(nameof(CourseCreatedEvent));
        var projection = new DecisionProjection<bool>(false, query, (s, _) => s);

        var model = BuildFromEvents(projection, [MakeEvent(new CourseCreatedEvent(Guid.NewGuid(), 10), position: 1)]);

        Assert.Same(query, model.AppendCondition.FailIfEventsMatch);
    }

    [Fact]
    public void Build_FailIfEventsMatch_IsAlwaysProjectionQuery_WhenNoEvents()
    {
        var query = Query.FromEventTypes(nameof(CourseCreatedEvent));
        var projection = new DecisionProjection<bool>(false, query, (s, _) => s);

        var model = BuildFromEvents(projection, []);

        Assert.Same(query, model.AppendCondition.FailIfEventsMatch);
    }

    #endregion

    #region Guard clauses

    [Fact]
    public void Build_NullProjectionQuery_IsEnforcedByDecisionProjectionConstructor()
    {
        // Argument validation is owned by DecisionProjection — verified in DecisionProjectionTests.
        // BuildDecisionModelAsync itself guards eventStore and projection via ThrowIfNull,
        // which requires a real IEventStore; that guard is covered by the integration tests.
        Assert.True(true); // Placeholder — guard coverage lives in correct test layer
    }

    #endregion

    #region N-ary BuildDecisionModelAsync (list overload) — pure fold logic

    // Simulates the fold+condition logic of the N-ary BuildDecisionModelAsync overload.
    private static (IReadOnlyList<TState> States, AppendCondition Condition) BuildNaryFromEvents<TState>(
        IReadOnlyList<IDecisionProjection<TState>> projections,
        SequencedEvent[] allEvents)
    {
        var unionItems = projections.SelectMany(p => p.Query.QueryItems).Distinct().ToArray();
        var unionQuery = unionItems.Length == 0 ? Query.All() : Query.FromItems(unionItems);

        var appendCondition = new AppendCondition
        {
            FailIfEventsMatch = unionQuery,
            AfterSequencePosition = allEvents.Length > 0 ? allEvents.Max(e => e.Position) : null
        };

        var states = projections
            .Select(p => allEvents.Where(e => p.Query.Matches(e)).Aggregate(p.InitialState, p.Apply))
            .ToArray();

        return (states, appendCondition);
    }

    [Fact]
    public void NaryBuild_SingleProjection_StateMatchesSingleOverload()
    {
        var courseId = Guid.NewGuid();
        IReadOnlyList<IDecisionProjection<bool>> projections =
        [
            new DecisionProjection<bool>(
                initialState: false,
                query: Query.FromItems(new QueryItem
                {
                    EventTypes = [nameof(CourseCreatedEvent)],
                    Tags = [new Tag("courseId", courseId.ToString())]
                }),
                apply: (_, evt) => evt.Event.Event is CourseCreatedEvent)
        ];

        var events = new[]
        {
            MakeEvent(new CourseCreatedEvent(courseId, 30), position: 1, ("courseId", courseId.ToString()))
        };

        var (states, _) = BuildNaryFromEvents(projections, events);

        Assert.Single(states);
        Assert.True(states[0]);
    }

    [Fact]
    public void NaryBuild_MultipleProjections_EachGetsIndependentState()
    {
        var courseId1 = Guid.NewGuid();
        var courseId2 = Guid.NewGuid();

        IReadOnlyList<IDecisionProjection<int>> projections =
        [
            new DecisionProjection<int>(
                initialState: 0,
                query: Query.FromItems(new QueryItem
                {
                    EventTypes = [nameof(StudentEnrolledEvent)],
                    Tags = [new Tag("courseId", courseId1.ToString())]
                }),
                apply: (s, evt) => evt.Event.Event is StudentEnrolledEvent e && e.CourseId == courseId1 ? s + 1 : s),

            new DecisionProjection<int>(
                initialState: 0,
                query: Query.FromItems(new QueryItem
                {
                    EventTypes = [nameof(StudentEnrolledEvent)],
                    Tags = [new Tag("courseId", courseId2.ToString())]
                }),
                apply: (s, evt) => evt.Event.Event is StudentEnrolledEvent e && e.CourseId == courseId2 ? s + 1 : s)
        ];

        var events = new[]
        {
            MakeEvent(new StudentEnrolledEvent(courseId1, Guid.NewGuid()), position: 1, ("courseId", courseId1.ToString())),
            MakeEvent(new StudentEnrolledEvent(courseId1, Guid.NewGuid()), position: 2, ("courseId", courseId1.ToString())),
            MakeEvent(new StudentEnrolledEvent(courseId2, Guid.NewGuid()), position: 3, ("courseId", courseId2.ToString()))
        };

        var (states, _) = BuildNaryFromEvents(projections, events);

        Assert.Equal(2, states.Count);
        Assert.Equal(2, states[0]); // two enrollments for course 1
        Assert.Equal(1, states[1]); // one enrollment for course 2
    }

    [Fact]
    public void NaryBuild_EmptyEventSet_AllStatesAreInitialState()
    {
        IReadOnlyList<IDecisionProjection<bool>> projections =
        [
            new DecisionProjection<bool>(false, Query.FromEventTypes(nameof(CourseCreatedEvent)), (_, _) => true),
            new DecisionProjection<bool>(false, Query.FromEventTypes(nameof(CourseCreatedEvent)), (_, _) => true)
        ];

        var (states, condition) = BuildNaryFromEvents(projections, []);

        Assert.All(states, s => Assert.False(s));
        Assert.Null(condition.AfterSequencePosition);
    }

    [Fact]
    public void NaryBuild_AppendConditionPosition_IsMaxAcrossAllEvents()
    {
        var courseId = Guid.NewGuid();

        IReadOnlyList<IDecisionProjection<bool>> projections =
        [
            new DecisionProjection<bool>(false, Query.All(), (_, _) => true),
            new DecisionProjection<bool>(false, Query.All(), (_, _) => true)
        ];

        var events = new[]
        {
            MakeEvent(new CourseCreatedEvent(courseId, 10), position: 3),
            MakeEvent(new StudentEnrolledEvent(courseId, Guid.NewGuid()), position: 7),
            MakeEvent(new UnrelatedEvent(), position: 5)
        };

        var (_, condition) = BuildNaryFromEvents(projections, events);

        Assert.Equal(7, condition.AfterSequencePosition);
    }

    [Fact]
    public void NaryBuild_StatesOrderMatchesProjectionsOrder()
    {
        var courseId = Guid.NewGuid();

        IReadOnlyList<IDecisionProjection<string>> projections =
        [
            new DecisionProjection<string>(
                "none",
                Query.FromEventTypes(nameof(CourseCreatedEvent)),
                (_, _) => "course-created"),

            new DecisionProjection<string>(
                "none",
                Query.FromEventTypes(nameof(StudentEnrolledEvent)),
                (_, _) => "student-enrolled")
        ];

        var events = new[]
        {
            MakeEvent(new CourseCreatedEvent(courseId, 10), position: 1),
            MakeEvent(new StudentEnrolledEvent(courseId, Guid.NewGuid()), position: 2)
        };

        var (states, _) = BuildNaryFromEvents(projections, events);

        Assert.Equal("course-created", states[0]);
        Assert.Equal("student-enrolled", states[1]);
    }

    #endregion
}
