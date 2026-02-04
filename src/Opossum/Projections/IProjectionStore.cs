using Opossum.Core;

namespace Opossum.Projections;

/// <summary>
/// Storage interface for reading projection state
/// </summary>
/// <typeparam name="TState">The projection state type</typeparam>
public interface IProjectionStore<TState> where TState : class
{
    /// <summary>
    /// Retrieves a single projection instance by key
    /// </summary>
    /// <param name="key">The projection instance key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The projection state, or null if not found</returns>
    Task<TState?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all projection instances
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of all projection instances</returns>
    Task<IReadOnlyList<TState>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries projection instances with a predicate filter
    /// </summary>
    /// <param name="predicate">Filter predicate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Filtered list of projection instances</returns>
    Task<IReadOnlyList<TState>> QueryAsync(Func<TState, bool> predicate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries projection instances by a single tag using the tag index.
    /// Much more efficient than GetAllAsync when projections have tag providers configured.
    /// Returns empty list if tag index doesn't exist.
    /// </summary>
    /// <param name="tag">The tag to query (case-insensitive comparison)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of projection instances matching the tag</returns>
    Task<IReadOnlyList<TState>> QueryByTagAsync(Tag tag, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries projection instances by multiple tags using tag indices (AND logic).
    /// Returns only projections that match ALL specified tags.
    /// Much more efficient than GetAllAsync when projections have tag providers configured.
    /// Returns empty list if any tag index doesn't exist.
    /// </summary>
    /// <param name="tags">The tags to query (all must match, case-insensitive comparison)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of projection instances matching all tags</returns>
    Task<IReadOnlyList<TState>> QueryByTagsAsync(IEnumerable<Tag> tags, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves or updates a projection instance (internal use by ProjectionManager)
    /// </summary>
    Task SaveAsync(string key, TState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a projection instance (internal use by ProjectionManager)
    /// </summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
