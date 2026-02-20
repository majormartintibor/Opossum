using Opossum.Core;

namespace Opossum.DecisionModel;

/// <summary>
/// Delegate-based implementation of <see cref="IDecisionProjection{TState}"/>.
/// Provides a concise way to define decision model projections via lambda expressions,
/// enabling the factory function pattern recommended by the DCB Projections specification.
/// </summary>
/// <remarks>
/// <para>
/// Typical usage via a static factory method on the domain layer:
/// </para>
/// <code>
/// public static class CourseProjections
/// {
///     public static IDecisionProjection&lt;bool&gt; CourseExists(Guid courseId) =>
///         new DecisionProjection&lt;bool&gt;(
///             initialState: false,
///             query: Query.FromItems(new QueryItem
///             {
///                 EventTypes = [nameof(CourseCreatedEvent)],
///                 Tags = [new Tag { Key = "courseId", Value = courseId.ToString() }]
///             }),
///             apply: (state, evt) => evt.Event.Event is CourseCreatedEvent ? true : state);
///
///     public static IDecisionProjection&lt;int&gt; EnrollmentCount(Guid courseId) =>
///         new DecisionProjection&lt;int&gt;(
///             initialState: 0,
///             query: Query.FromItems(new QueryItem
///             {
///                 EventTypes = [nameof(StudentEnrolledToCourseEvent)],
///                 Tags = [new Tag { Key = "courseId", Value = courseId.ToString() }]
///             }),
///             apply: (state, evt) => evt.Event.Event is StudentEnrolledToCourseEvent
///                 ? state + 1
///                 : state);
/// }
/// </code>
/// </remarks>
/// <typeparam name="TState">
/// The type that represents the decision model state.
/// May be a value type (e.g. <see langword="bool"/>, <see langword="int"/>) or a reference type.
/// </typeparam>
public sealed class DecisionProjection<TState> : IDecisionProjection<TState>
{
    private readonly Func<TState, SequencedEvent, TState> _apply;

    /// <summary>
    /// Creates a new <see cref="DecisionProjection{TState}"/>.
    /// </summary>
    /// <param name="initialState">The starting state before any events are applied.</param>
    /// <param name="query">
    /// The query that selects relevant events and guards the append condition.
    /// </param>
    /// <param name="apply">
    /// A pure function that folds a single event into the current state.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="query"/> or <paramref name="apply"/> is <see langword="null"/>.
    /// </exception>
    public DecisionProjection(
        TState initialState,
        Query query,
        Func<TState, SequencedEvent, TState> apply)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(apply);

        InitialState = initialState;
        Query = query;
        _apply = apply;
    }

    /// <inheritdoc/>
    public TState InitialState { get; }

    /// <inheritdoc/>
    public Query Query { get; }

    /// <inheritdoc/>
    public TState Apply(TState state, SequencedEvent evt) => _apply(state, evt);
}
