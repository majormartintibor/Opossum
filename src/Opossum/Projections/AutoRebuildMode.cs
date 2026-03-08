namespace Opossum.Projections;

/// <summary>
/// Controls how the projection daemon handles missing or stale projections at startup.
/// </summary>
public enum AutoRebuildMode
{
    /// <summary>
    /// The daemon starts but never triggers a rebuild on startup.
    /// Use in production when projections are managed manually or via admin endpoints.
    /// </summary>
    None,

    /// <summary>
    /// Only projections whose checkpoint file is absent are rebuilt.
    /// Projections that were already rebuilt (even against an empty store) are skipped.
    /// This is the recommended default for most deployments.
    /// </summary>
    MissingCheckpointsOnly,

    /// <summary>
    /// All projections are rebuilt from scratch on every startup, regardless of checkpoints.
    /// Use in development when iterating on projection logic and wanting a clean slate
    /// on each restart, or after deploying a projection fix that requires a full replay.
    /// </summary>
    ForceFullRebuild
}
