using Opossum.Core;

namespace Opossum.DecisionModel;

/// <summary>
/// Extension methods on <see cref="IEventStore"/> that implement the DCB
/// read → decide → append pattern via Decision Model projections.
/// </summary>
public static class DecisionModelExtensions
{
    /// <summary>
    /// Builds a Decision Model by reading all events relevant to the given projection
    /// from the event store, folding them into state, and returning both the state and
    /// the <see cref="AppendCondition"/> that guards the decision against concurrent writes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This implements the DCB write-side pattern in a single call:
    /// </para>
    /// <code>
    /// var model = await eventStore.BuildDecisionModelAsync(
    ///     CourseProjections.Capacity(command.CourseId));
    ///
    /// if (model.State.IsFull)
    ///     return CommandResult.Fail("Course is at capacity.");
    ///
    /// await eventStore.AppendAsync(newEvent, model.AppendCondition);
    /// // Throws AppendConditionFailedException if a concurrent write invalidated the model.
    /// </code>
    /// <para>
    /// The <see cref="AppendCondition"/> is constructed automatically:
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       <see cref="AppendCondition.FailIfEventsMatch"/> is set to
    ///       <see cref="IDecisionProjection{TState}.Query"/> — the same query used to read,
    ///       ensuring only semantically relevant writes can invalidate the decision.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="AppendCondition.AfterSequencePosition"/> is set to the maximum
    ///       position of all loaded events, or <see langword="null"/> when the store
    ///       contained no matching events.
    ///     </description>
    ///   </item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <typeparam name="TState">The decision model state type.</typeparam>
    /// <param name="eventStore">The event store to read from.</param>
    /// <param name="projection">
    /// The projection that defines the query, initial state, and fold function.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="DecisionModel{TState}"/> containing the folded state and the
    /// append condition to enforce the Dynamic Consistency Boundary.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="eventStore"/> or <paramref name="projection"/> is <see langword="null"/>.
    /// </exception>
    public static async Task<DecisionModel<TState>> BuildDecisionModelAsync<TState>(
        this IEventStore eventStore,
        IDecisionProjection<TState> projection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(projection);

        cancellationToken.ThrowIfCancellationRequested();

        var events = await eventStore.ReadAsync(projection.Query, null)
            .ConfigureAwait(false);

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

    /// <summary>
    /// Builds a Decision Model from two independent projections using a single read against
    /// the event store. The combined query is the union of both sub-queries; each projection
    /// folds only the events that match its own query.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One <see cref="IEventStore.ReadAsync"/> call is made with the union of both queries.
    /// Each projection then folds only the subset of events it cares about:
    /// </para>
    /// <code>
    /// var (courseCapacity, studentLimit, condition) = await eventStore.BuildDecisionModelAsync(
    ///     CourseProjections.Capacity(command.CourseId),
    ///     StudentProjections.EnrollmentLimit(command.StudentId));
    ///
    /// if (courseCapacity.IsFull || studentLimit.IsAtMax)
    ///     return CommandResult.Fail("...");
    ///
    /// await eventStore.AppendAsync(newEvent, condition);
    /// </code>
    /// <para>
    /// The single <see cref="AppendCondition"/> returned spans both projection queries —
    /// a concurrent write matching either query will invalidate the decision.
    /// </para>
    /// </remarks>
    /// <returns>
    /// A value tuple of <c>(First, Second, Condition)</c> where <c>Condition</c> is the
    /// <see cref="AppendCondition"/> spanning both projection queries.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any parameter is <see langword="null"/>.
    /// </exception>
    public static async Task<(T1 First, T2 Second, AppendCondition Condition)>
        BuildDecisionModelAsync<T1, T2>(
            this IEventStore eventStore,
            IDecisionProjection<T1> first,
            IDecisionProjection<T2> second,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        cancellationToken.ThrowIfCancellationRequested();

        var unionQuery = BuildUnionQuery([first.Query, second.Query]);
        var events = await eventStore.ReadAsync(unionQuery, null).ConfigureAwait(false);

        var appendCondition = new AppendCondition
        {
            FailIfEventsMatch = unionQuery,
            AfterSequencePosition = events.Length > 0 ? events.Max(e => e.Position) : null
        };

        return (FoldEvents(first, events), FoldEvents(second, events), appendCondition);
    }

    /// <summary>
    /// Builds a Decision Model from three independent projections using a single read
    /// against the event store. The combined query is the union of all three sub-queries.
    /// </summary>
    /// <inheritdoc cref="BuildDecisionModelAsync{T1,T2}(IEventStore,IDecisionProjection{T1},IDecisionProjection{T2},CancellationToken)"/>
    /// <returns>
    /// A value tuple of <c>(First, Second, Third, Condition)</c>.
    /// </returns>
    public static async Task<(T1 First, T2 Second, T3 Third, AppendCondition Condition)>
        BuildDecisionModelAsync<T1, T2, T3>(
            this IEventStore eventStore,
            IDecisionProjection<T1> first,
            IDecisionProjection<T2> second,
            IDecisionProjection<T3> third,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(third);

        cancellationToken.ThrowIfCancellationRequested();

        var unionQuery = BuildUnionQuery([first.Query, second.Query, third.Query]);
        var events = await eventStore.ReadAsync(unionQuery, null).ConfigureAwait(false);

        var appendCondition = new AppendCondition
        {
            FailIfEventsMatch = unionQuery,
            AfterSequencePosition = events.Length > 0 ? events.Max(e => e.Position) : null
        };

        return (FoldEvents(first, events), FoldEvents(second, events), FoldEvents(third, events), appendCondition);
    }

    // Folds only the events that match the projection's own query (for composed overloads).
    private static TState FoldEvents<TState>(IDecisionProjection<TState> projection, SequencedEvent[] events) =>
        events
            .Where(e => projection.Query.Matches(e))
            .OrderBy(e => e.Position)
            .Aggregate(projection.InitialState, projection.Apply);

    // Builds the union of multiple queries (OR of all QueryItems).
    // If any sub-query is Query.All() (empty QueryItems), the union is also Query.All().
    private static Query BuildUnionQuery(IEnumerable<Query> queries)
    {
        var allItems = new List<QueryItem>();
        foreach (var query in queries)
        {
            if (query.QueryItems.Count == 0)
                return Query.All();
            allItems.AddRange(query.QueryItems);
        }
        return Query.FromItems([.. allItems]);
    }
}
