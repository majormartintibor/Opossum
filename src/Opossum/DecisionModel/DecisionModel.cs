using Opossum.Core;

namespace Opossum.DecisionModel;

/// <summary>
/// The result of building a Decision Model via
/// <see cref="DecisionModelExtensions.BuildDecisionModelAsync{TState}"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="State"/> is the accumulated projection state built by folding all relevant
/// events from the event store.
/// </para>
/// <para>
/// <see cref="AppendCondition"/> must be passed to
/// <see cref="IEventStore.AppendAsync"/> to enforce the Dynamic Consistency Boundary:
/// if any event matching the projection query was appended between the read and the
/// append, the operation is rejected and the command handler should retry.
/// </para>
/// </remarks>
/// <typeparam name="TState">The type of the decision model state.</typeparam>
/// <param name="State">The projected state built from all relevant events.</param>
/// <param name="AppendCondition">
/// The guard that ties this decision model to a specific position in the event stream.
/// Pass this to <see cref="IEventStore.AppendAsync"/> unchanged.
/// </param>
public sealed record DecisionModel<TState>(TState State, AppendCondition AppendCondition);
