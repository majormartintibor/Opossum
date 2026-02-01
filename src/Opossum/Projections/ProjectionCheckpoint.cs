namespace Opossum.Projections;

/// <summary>
/// Checkpoint tracking for projections
/// </summary>
internal sealed class ProjectionCheckpoint
{
    /// <summary>
    /// Name of the projection
    /// </summary>
    public string ProjectionName { get; set; } = string.Empty;

    /// <summary>
    /// Last processed event position
    /// </summary>
    public long LastProcessedPosition { get; set; }

    /// <summary>
    /// When the checkpoint was last updated
    /// </summary>
    public DateTimeOffset LastUpdated { get; set; }

    /// <summary>
    /// Total number of events processed
    /// </summary>
    public long TotalEventsProcessed { get; set; }
}
