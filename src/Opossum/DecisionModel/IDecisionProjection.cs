using Opossum.Core;

namespace Opossum.DecisionModel;

/// <summary>
/// Defines an in-memory, ephemeral projection used to build a Decision Model for enforcing
/// consistency during command handling — the DCB write-side pattern.
/// </summary>
/// <remarks>
/// <para>
/// Decision Model projections are fundamentally different from Read Model projections
/// (<see cref="Opossum.Projections.IProjectionDefinition{TState}"/>):
/// </para>
/// <list type="table">
///   <listheader>
///     <term>Aspect</term>
///     <description>Decision Model vs Read Model</description>
///   </listheader>
///   <item>
///     <term>Consistency</term>
///     <description>Strong — built from the live event store at command handling time.</description>
///   </item>
///   <item>
///     <term>Persistence</term>
///     <description>Never — in-memory only, discarded after the command completes.</description>
///   </item>
///   <item>
///     <term>Lifecycle</term>
///     <description>Born and dies within a single command handler invocation.</description>
///   </item>
///   <item>
///     <term>Purpose</term>
///     <description>Enforce business invariants before appending new events.</description>
///   </item>
/// </list>
/// <para>
/// Use the factory function pattern to create parameterised projection instances:
/// </para>
/// <code>
/// public static IDecisionProjection&lt;bool&gt; CourseExists(Guid courseId) =>
///     new DecisionProjection&lt;bool&gt;(
///         initialState: false,
///         query: Query.FromItems(new QueryItem
///         {
///             EventTypes = [nameof(CourseCreatedEvent)],
///             Tags = [new Tag { Key = "courseId", Value = courseId.ToString() }]
///         }),
///         apply: (state, evt) => evt.Event.Event is CourseCreatedEvent ? true : state);
/// </code>
/// <para>
/// The <see cref="Query"/> declared here serves a dual role:
/// it selects which events to load from the store, and it becomes the
/// <see cref="AppendCondition.FailIfEventsMatch"/> query that guards the decision
/// against concurrent writes.
/// </para>
/// </remarks>
/// <typeparam name="TState">
/// The type that represents the decision model state.
/// May be a value type (e.g. <see langword="bool"/>, <see langword="int"/>) or a reference type.
/// </typeparam>
public interface IDecisionProjection<TState>
{
    /// <summary>
    /// The starting state before any events are applied.
    /// </summary>
    TState InitialState { get; }

    /// <summary>
    /// The query that selects events relevant to this projection from the event store.
    /// This same query is used as <see cref="AppendCondition.FailIfEventsMatch"/> to guard
    /// the decision against concurrent writes — ensuring the decision model is still valid
    /// at the moment of appending.
    /// </summary>
    Query Query { get; }

    /// <summary>
    /// Folds a single event into the current state. Must be a pure function with no side effects.
    /// </summary>
    /// <param name="state">The current accumulated state.</param>
    /// <param name="evt">The next sequenced event to apply.</param>
    /// <returns>The new state after applying the event.</returns>
    TState Apply(TState state, SequencedEvent evt);
}
