using Opossum.Core;
using Opossum.DecisionModel;

namespace Opossum.UnitTests.DecisionModel;

/// <summary>
/// Unit tests for the composed <see cref="DecisionModelExtensions.BuildDecisionModelAsync{T1,T2}"/>
/// and <see cref="DecisionModelExtensions.BuildDecisionModelAsync{T1,T2,T3}"/> overloads.
/// All tests are pure — no file system, no event store, no DI container.
/// The composition logic is verified by simulating the fold+filter logic over a pre-built
/// event array, mirroring what the extension methods do internally.
/// </summary>
public class ComposeProjectionsTests
{
    #region Helpers

    private record CourseCreatedEvent(Guid CourseId) : IEvent;
    private record StudentRegisteredEvent(Guid StudentId) : IEvent;
    private record EnrolledEvent(Guid CourseId, Guid StudentId) : IEvent;
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

    // Mirrors BuildDecisionModelAsync<T1, T2> composition logic without I/O.
    private static (T1 First, T2 Second, AppendCondition Condition) SimulateCompose<T1, T2>(
        IDecisionProjection<T1> p1,
        IDecisionProjection<T2> p2,
        SequencedEvent[] allEvents)
    {
        var unionQuery = BuildUnionQuery([p1.Query, p2.Query]);

        var s1 = allEvents
            .Where(e => p1.Query.Matches(e))
            .OrderBy(e => e.Position)
            .Aggregate(p1.InitialState, p1.Apply);

        var s2 = allEvents
            .Where(e => p2.Query.Matches(e))
            .OrderBy(e => e.Position)
            .Aggregate(p2.InitialState, p2.Apply);

        var condition = new AppendCondition
        {
            FailIfEventsMatch = unionQuery,
            AfterSequencePosition = allEvents.Length > 0 ? allEvents.Max(e => e.Position) : null
        };

        return (s1, s2, condition);
    }

    // Mirrors BuildDecisionModelAsync<T1, T2, T3> composition logic without I/O.
    private static (T1 First, T2 Second, T3 Third, AppendCondition Condition) SimulateCompose<T1, T2, T3>(
        IDecisionProjection<T1> p1,
        IDecisionProjection<T2> p2,
        IDecisionProjection<T3> p3,
        SequencedEvent[] allEvents)
    {
        var unionQuery = BuildUnionQuery([p1.Query, p2.Query, p3.Query]);

        var s1 = allEvents.Where(e => p1.Query.Matches(e)).OrderBy(e => e.Position).Aggregate(p1.InitialState, p1.Apply);
        var s2 = allEvents.Where(e => p2.Query.Matches(e)).OrderBy(e => e.Position).Aggregate(p2.InitialState, p2.Apply);
        var s3 = allEvents.Where(e => p3.Query.Matches(e)).OrderBy(e => e.Position).Aggregate(p3.InitialState, p3.Apply);

        var condition = new AppendCondition
        {
            FailIfEventsMatch = unionQuery,
            AfterSequencePosition = allEvents.Length > 0 ? allEvents.Max(e => e.Position) : null
        };

        return (s1, s2, s3, condition);
    }

    private static Query BuildUnionQuery(IEnumerable<Query> queries)
    {
        var allItems = new List<QueryItem>();
        foreach (var q in queries)
        {
            if (q.QueryItems.Count == 0)
                return Query.All();
            allItems.AddRange(q.QueryItems);
        }
        return Query.FromItems([.. allItems]);
    }

    #endregion

    #region Non-overlapping queries — states are independent

    [Fact]
    public void Compose2_NonOverlappingQueries_EachStateUpdatedByOwnEvents()
    {
        var courseId = Guid.NewGuid();
        var studentId = Guid.NewGuid();

        var courseProjection = new DecisionProjection<bool>(
            initialState: false,
            query: Query.FromEventTypes(nameof(CourseCreatedEvent)),
            apply: (_, evt) => evt.Event.Event is CourseCreatedEvent);

        var studentProjection = new DecisionProjection<bool>(
            initialState: false,
            query: Query.FromEventTypes(nameof(StudentRegisteredEvent)),
            apply: (_, evt) => evt.Event.Event is StudentRegisteredEvent);

        var events = new[]
        {
            MakeEvent(new CourseCreatedEvent(courseId), position: 1),
            MakeEvent(new StudentRegisteredEvent(studentId), position: 2)
        };

        var (courseExists, studentExists, _) = SimulateCompose(courseProjection, studentProjection, events);

        Assert.True(courseExists);
        Assert.True(studentExists);
    }

    [Fact]
    public void Compose2_EventForFirstProjectionOnly_SecondStateUnchanged()
    {
        var courseId = Guid.NewGuid();

        var courseProjection = new DecisionProjection<bool>(
            initialState: false,
            query: Query.FromEventTypes(nameof(CourseCreatedEvent)),
            apply: (_, _) => true);

        var studentProjection = new DecisionProjection<bool>(
            initialState: false,
            query: Query.FromEventTypes(nameof(StudentRegisteredEvent)),
            apply: (_, _) => true);

        var events = new[] { MakeEvent(new CourseCreatedEvent(courseId), position: 1) };

        var (courseExists, studentExists, _) = SimulateCompose(courseProjection, studentProjection, events);

        Assert.True(courseExists);
        Assert.False(studentExists); // Not updated — no StudentRegistered events
    }

    #endregion

    #region Overlapping queries — shared events applied to both projections

    [Fact]
    public void Compose2_SharedEvent_AppliedToBothProjections()
    {
        var courseId = Guid.NewGuid();
        var studentId = Guid.NewGuid();

        // Both projections care about EnrolledEvent
        var enrollmentCountForCourse = new DecisionProjection<int>(
            initialState: 0,
            query: Query.FromItems(new QueryItem
            {
                EventTypes = [nameof(EnrolledEvent)],
                Tags = [new Tag("courseId", courseId.ToString())]
            }),
            apply: (count, _) => count + 1);

        var enrollmentCountForStudent = new DecisionProjection<int>(
            initialState: 0,
            query: Query.FromItems(new QueryItem
            {
                EventTypes = [nameof(EnrolledEvent)],
                Tags = [new Tag("studentId", studentId.ToString())]
            }),
            apply: (count, _) => count + 1);

        var events = new[]
        {
            MakeEvent(new EnrolledEvent(courseId, studentId), position: 1,
                ("courseId", courseId.ToString()), ("studentId", studentId.ToString())),
            MakeEvent(new EnrolledEvent(courseId, Guid.NewGuid()), position: 2,
                ("courseId", courseId.ToString()), ("studentId", Guid.NewGuid().ToString()))
        };

        var (courseCount, studentCount, _) = SimulateCompose(enrollmentCountForCourse, enrollmentCountForStudent, events);

        Assert.Equal(2, courseCount);   // Both events have this courseId
        Assert.Equal(1, studentCount);  // Only event 1 has this studentId
    }

    #endregion

    #region Union query

    [Fact]
    public void Compose2_UnionQuery_ContainsItemsFromBothProjections()
    {
        var p1 = new DecisionProjection<bool>(false,
            Query.FromEventTypes("TypeA"), (s, _) => s);
        var p2 = new DecisionProjection<bool>(false,
            Query.FromEventTypes("TypeB"), (s, _) => s);

        var unionQuery = BuildUnionQuery([p1.Query, p2.Query]);

        Assert.True(unionQuery.Matches(new SequencedEvent
        {
            Position = 1,
            Event = new DomainEvent { EventType = "TypeA", Event = new UnrelatedEvent(), Tags = [] }
        }));
        Assert.True(unionQuery.Matches(new SequencedEvent
        {
            Position = 2,
            Event = new DomainEvent { EventType = "TypeB", Event = new UnrelatedEvent(), Tags = [] }
        }));
        Assert.False(unionQuery.Matches(new SequencedEvent
        {
            Position = 3,
            Event = new DomainEvent { EventType = "TypeC", Event = new UnrelatedEvent(), Tags = [] }
        }));
    }

    [Fact]
    public void Compose2_WhenEitherQueryIsAll_UnionIsAll()
    {
        var p1 = new DecisionProjection<bool>(false, Query.All(), (s, _) => s);
        var p2 = new DecisionProjection<bool>(false, Query.FromEventTypes("TypeA"), (s, _) => s);

        var unionQuery = BuildUnionQuery([p1.Query, p2.Query]);

        // Query.All() has empty QueryItems
        Assert.Empty(unionQuery.QueryItems);
    }

    #endregion

    #region AppendCondition covers union and max position

    [Fact]
    public void Compose2_AppendCondition_PositionIsMaxAcrossAllUnionEvents()
    {
        var p1 = new DecisionProjection<int>(0,
            Query.FromEventTypes(nameof(CourseCreatedEvent)),
            (s, _) => s + 1);

        var p2 = new DecisionProjection<int>(0,
            Query.FromEventTypes(nameof(StudentRegisteredEvent)),
            (s, _) => s + 1);

        var events = new[]
        {
            MakeEvent(new CourseCreatedEvent(Guid.NewGuid()), position: 1),   // p1 only
            MakeEvent(new StudentRegisteredEvent(Guid.NewGuid()), position: 5) // p2 only
        };

        var (_, _, condition) = SimulateCompose(p1, p2, events);

        // AfterSequencePosition must be 5 (max of ALL union events), not 1 (max for p1)
        Assert.Equal(5, condition.AfterSequencePosition);
    }

    [Fact]
    public void Compose2_EmptyEvents_PositionIsNull()
    {
        var p1 = new DecisionProjection<bool>(false, Query.FromEventTypes(nameof(CourseCreatedEvent)), (s, _) => s);
        var p2 = new DecisionProjection<bool>(false, Query.FromEventTypes(nameof(StudentRegisteredEvent)), (s, _) => s);

        var (first, second, condition) = SimulateCompose(p1, p2, []);

        Assert.False(first);
        Assert.False(second);
        Assert.Null(condition.AfterSequencePosition);
    }

    #endregion

    #region Three-projection composition

    [Fact]
    public void Compose3_AllThreeStatesUpdatedByOwnEvents()
    {
        var courseId = Guid.NewGuid();
        var studentId = Guid.NewGuid();

        var p1 = new DecisionProjection<bool>(false,
            Query.FromEventTypes(nameof(CourseCreatedEvent)),
            (_, evt) => evt.Event.Event is CourseCreatedEvent);

        var p2 = new DecisionProjection<bool>(false,
            Query.FromEventTypes(nameof(StudentRegisteredEvent)),
            (_, evt) => evt.Event.Event is StudentRegisteredEvent);

        var p3 = new DecisionProjection<int>(0,
            Query.FromEventTypes(nameof(EnrolledEvent)),
            (count, _) => count + 1);

        var events = new[]
        {
            MakeEvent(new CourseCreatedEvent(courseId), position: 1),
            MakeEvent(new StudentRegisteredEvent(studentId), position: 2),
            MakeEvent(new EnrolledEvent(courseId, studentId), position: 3),
            MakeEvent(new EnrolledEvent(courseId, Guid.NewGuid()), position: 4)
        };

        var (courseExists, studentExists, enrollCount, _) = SimulateCompose(p1, p2, p3, events);

        Assert.True(courseExists);
        Assert.True(studentExists);
        Assert.Equal(2, enrollCount);
    }

    [Fact]
    public void Compose3_AppendCondition_PositionIsMaxAcrossAllThree()
    {
        var p1 = new DecisionProjection<int>(0, Query.FromEventTypes(nameof(CourseCreatedEvent)), (s, _) => s + 1);
        var p2 = new DecisionProjection<int>(0, Query.FromEventTypes(nameof(StudentRegisteredEvent)), (s, _) => s + 1);
        var p3 = new DecisionProjection<int>(0, Query.FromEventTypes(nameof(EnrolledEvent)), (s, _) => s + 1);

        var events = new[]
        {
            MakeEvent(new CourseCreatedEvent(Guid.NewGuid()), position: 2),
            MakeEvent(new StudentRegisteredEvent(Guid.NewGuid()), position: 9),
            MakeEvent(new EnrolledEvent(Guid.NewGuid(), Guid.NewGuid()), position: 4)
        };

        var (_, _, _, condition) = SimulateCompose(p1, p2, p3, events);

        Assert.Equal(9, condition.AfterSequencePosition);
    }

    #endregion

    #region Events applied in ascending position order

    [Fact]
    public void Compose2_EventsAppliedInPositionOrder_NotArrayOrder()
    {
        var p1 = new DecisionProjection<List<long>>(
            initialState: [],
            query: Query.All(),
            apply: (list, evt) => new List<long>(list) { evt.Position });

        var p2 = new DecisionProjection<List<long>>(
            initialState: [],
            query: Query.All(),
            apply: (list, evt) => new List<long>(list) { evt.Position });

        // Events provided out of position order
        var events = new[]
        {
            MakeEvent(new UnrelatedEvent(), position: 5),
            MakeEvent(new UnrelatedEvent(), position: 1),
            MakeEvent(new UnrelatedEvent(), position: 3)
        };

        var (positions1, positions2, _) = SimulateCompose(p1, p2, events);

        Assert.Equal([1L, 3L, 5L], positions1);
        Assert.Equal([1L, 3L, 5L], positions2);
    }

    #endregion
}
