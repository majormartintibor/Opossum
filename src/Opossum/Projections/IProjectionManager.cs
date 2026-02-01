using Opossum.Core;

namespace Opossum.Projections;

/// <summary>
/// Manages projection lifecycle, registration, and updates
/// </summary>
public interface IProjectionManager
{
    /// <summary>
    /// Registers a projection definition
    /// </summary>
    /// <typeparam name="TState">The projection state type</typeparam>
    /// <param name="definition">The projection definition</param>
    void RegisterProjection<TState>(IProjectionDefinition<TState> definition) where TState : class;

    /// <summary>
    /// Rebuilds a projection from scratch by replaying all events
    /// </summary>
    /// <param name="projectionName">Name of the projection to rebuild</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RebuildAsync(string projectionName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies new events to all registered projections
    /// </summary>
    /// <param name="events">Events to apply</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateAsync(SequencedEvent[] events, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the last processed event position for a projection
    /// </summary>
    /// <param name="projectionName">Name of the projection</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Last processed position, or 0 if never processed</returns>
    Task<long> GetCheckpointAsync(string projectionName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the checkpoint for a projection
    /// </summary>
    /// <param name="projectionName">Name of the projection</param>
    /// <param name="position">Last processed event position</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SaveCheckpointAsync(string projectionName, long position, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all registered projection names
    /// </summary>
    IReadOnlyList<string> GetRegisteredProjections();
}
