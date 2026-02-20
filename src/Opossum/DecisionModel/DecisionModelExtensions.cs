using Opossum.Core;
using Opossum.Exceptions;

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

    /// <summary>
    /// Executes the complete DCB read → decide → append cycle with automatic retry on
    /// optimistic concurrency failures.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <paramref name="operation"/> delegate is retried whenever an
    /// <see cref="AppendConditionFailedException"/> or <see cref="ConcurrencyException"/> is
    /// thrown — both indicate that another writer modified the relevant event stream between
    /// the read and the append. Retries use exponential back-off.
    /// </para>
    /// <code>
    /// return await eventStore.ExecuteDecisionAsync(async (store, ct) =>
    /// {
    ///     var (capacity, condition) = await store.BuildDecisionModelAsync(
    ///         CourseProjections.Capacity(command.CourseId), ct);
    ///
    ///     if (capacity.IsFull)
    ///         return CommandResult.Fail("Course is full.");
    ///
    ///     await store.AppendAsync(enrollmentEvent, condition);
    ///     return CommandResult.Ok();
    /// });
    /// </code>
    /// <para>
    /// If all <paramref name="maxRetries"/> attempts fail, the last exception is re-thrown
    /// so the caller can decide how to handle an exhausted retry budget.
    /// </para>
    /// </remarks>
    /// <typeparam name="TResult">The return type of the operation.</typeparam>
    /// <param name="eventStore">The event store to pass into the operation.</param>
    /// <param name="operation">
    /// The delegate that performs the read → decide → append cycle. Receives the event store
    /// and a <see cref="CancellationToken"/>.
    /// </param>
    /// <param name="maxRetries">Total number of attempts. Defaults to <c>3</c>.</param>
    /// <param name="initialDelayMs">
    /// Initial delay in milliseconds for the exponential back-off. Defaults to <c>50</c>.
    /// Delay after attempt <c>n</c> is <c>initialDelayMs × 2^n</c>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result produced by <paramref name="operation"/>.</returns>
    /// <exception cref="AppendConditionFailedException">
    /// Re-thrown when max retries are exhausted due to append-condition failures.
    /// </exception>
    /// <exception cref="ConcurrencyException">
    /// Re-thrown when max retries are exhausted due to concurrency conflicts.
    /// </exception>
    public static async Task<TResult> ExecuteDecisionAsync<TResult>(
        this IEventStore eventStore,
        Func<IEventStore, CancellationToken, Task<TResult>> operation,
        int maxRetries = 3,
        int initialDelayMs = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(operation);

        for (int attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation(eventStore, cancellationToken).ConfigureAwait(false);
            }
            catch (AppendConditionFailedException) when (attempt < maxRetries - 1)
            {
                await Task.Delay(initialDelayMs * (int)Math.Pow(2, attempt), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ConcurrencyException) when (attempt < maxRetries - 1)
            {
                await Task.Delay(initialDelayMs * (int)Math.Pow(2, attempt), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
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
