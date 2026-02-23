using Opossum.Core;

namespace Opossum.Projections;

/// <summary>
/// Defines how events are projected into a materialized view state
/// </summary>
/// <typeparam name="TState">The projection state type</typeparam>
public interface IProjectionDefinition<TState> where TState : class
{
    /// <summary>
    /// Unique name for this projection
    /// </summary>
    string ProjectionName { get; }

    /// <summary>
    /// Event types this projection subscribes to
    /// </summary>
    string[] EventTypes { get; }

    /// <summary>
    /// Extracts the key (partition key) from an event for this projection instance
    /// </summary>
    /// <param name="evt">The sequenced event</param>
    /// <returns>The key identifying which projection instance to update</returns>
    string KeySelector(SequencedEvent evt);

    /// <summary>
    /// Applies an event to the current projection state.
    /// The full event envelope (payload, tags, metadata, position) is available via
    /// <see cref="SequencedEvent.Event"/>, <see cref="SequencedEvent.Metadata"/>, and
    /// <see cref="SequencedEvent.Position"/>.
    /// </summary>
    /// <param name="current">Current state (null for new projection instance)</param>
    /// <param name="evt">The full sequenced event including payload, tags, metadata, and position</param>
    /// <returns>Updated state, or null to delete the projection instance</returns>
    TState? Apply(TState? current, SequencedEvent evt);
}
