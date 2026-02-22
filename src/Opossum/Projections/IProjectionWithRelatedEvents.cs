using Opossum.Core;

namespace Opossum.Projections;

/// <summary>
/// Defines a projection that fetches related events from the event store
/// to build its state. The framework will automatically load related events
/// before calling Apply.
/// </summary>
/// <typeparam name="TState">The projection state type</typeparam>
public interface IProjectionWithRelatedEvents<TState> : IProjectionDefinition<TState>
    where TState : class
{
    /// <summary>
    /// Determines what related events to load for a given event.
    /// Called by the framework before Apply to fetch additional context.
    /// </summary>
    /// <param name="evt">The current event being processed</param>
    /// <returns>Query for related events, or null if no related events needed</returns>
    Query? GetRelatedEventsQuery(IEvent evt);

    /// <summary>
    /// Applies an event to the current projection state with access to related events.
    /// The framework guarantees that related events (from GetRelatedEventsQuery) are loaded.
    /// </summary>
    /// <param name="current">Current state (null for new projection instance)</param>
    /// <param name="evt">Event to apply</param>
    /// <param name="relatedEvents">Related events loaded by the framework (empty array if none)</param>
    /// <returns>Updated state, or null to delete the projection instance</returns>
    TState? Apply(TState? current, IEvent evt, SequencedEvent[] relatedEvents);

    // Hide the base Apply method - projections with related events must use the overload
    TState? IProjectionDefinition<TState>.Apply(TState? current, IEvent evt) =>
        throw new NotImplementedException(
            $"Projection {ProjectionName} must implement Apply with relatedEvents parameter. " +
            "The framework should call the related-events Apply overload.");
}
