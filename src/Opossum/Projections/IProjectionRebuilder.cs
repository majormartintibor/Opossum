namespace Opossum.Projections;

/// <summary>
/// Manages projection rebuild operations, including full rebuilds, selective rebuilds,
/// and crash recovery. This interface is the single entry point for all rebuild-related
/// functionality, cleanly separated from the live event processing managed by
/// <see cref="IProjectionManager"/>.
/// </summary>
public interface IProjectionRebuilder
{
    /// <summary>
    /// Scans for interrupted rebuild journals and resumes any rebuilds that were in progress
    /// when the application last shut down or crashed. Completed rebuilds whose journals are
    /// still present are committed; orphaned temp directories with no matching journal are
    /// cleaned up.
    /// <para>
    /// This method is called automatically by <c>ProjectionDaemon</c> on startup before
    /// <see cref="RebuildAllAsync"/> and is not intended for direct use by application code.
    /// Calling it manually is safe but unnecessary when the daemon is running.
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to abort the recovery operation.</param>
    /// <returns>A task that completes when all interrupted rebuilds have been resumed or discarded.</returns>
    Task ResumeInterruptedRebuildsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds a single projection from scratch by replaying all events from position 0.
    /// The existing projection data is replaced atomically on successful completion.
    /// </summary>
    /// <param name="projectionName">
    /// The name of the registered projection to rebuild. Must match a projection previously
    /// registered via <see cref="IProjectionManager.RegisterProjection{TState}"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token to abort the rebuild.</param>
    /// <returns>
    /// A <see cref="ProjectionRebuildResult"/> containing timing, event count, and
    /// success/failure details for the rebuilt projection.
    /// </returns>
    Task<ProjectionRebuildResult> RebuildAsync(
        string projectionName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds all registered projections in parallel, respecting the
    /// <c>MaxConcurrentRebuilds</c> configuration limit.
    /// </summary>
    /// <param name="forceRebuild">
    /// If <see langword="true"/>, rebuilds every registered projection regardless of its
    /// current checkpoint. If <see langword="false"/>, only rebuilds projections whose
    /// checkpoint is at position 0 (i.e., never built or previously cleared).
    /// </param>
    /// <param name="cancellationToken">Cancellation token to abort the rebuild.</param>
    /// <returns>
    /// A <see cref="ProjectionRebuildResult"/> summarising all individual projection rebuilds,
    /// including which succeeded and which failed.
    /// </returns>
    Task<ProjectionRebuildResult> RebuildAllAsync(
        bool forceRebuild = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds a specific set of projections in parallel. Useful for selectively rebuilding
    /// projections after a bug fix without affecting other projections.
    /// Respects the <c>MaxConcurrentRebuilds</c> configuration limit.
    /// </summary>
    /// <param name="projectionNames">
    /// Names of the projections to rebuild. Each name must match a projection previously
    /// registered via <see cref="IProjectionManager.RegisterProjection{TState}"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token to abort the rebuild.</param>
    /// <returns>
    /// A <see cref="ProjectionRebuildResult"/> summarising all individual projection rebuilds,
    /// including which succeeded and which failed.
    /// </returns>
    Task<ProjectionRebuildResult> RebuildAsync(
        string[] projectionNames,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current rebuild status, including which projections are currently being rebuilt,
    /// which are queued, and estimated completion time.
    /// </summary>
    /// <returns>
    /// A <see cref="ProjectionRebuildStatus"/> snapshot of the current rebuild state.
    /// </returns>
    Task<ProjectionRebuildStatus> GetRebuildStatusAsync();
}
