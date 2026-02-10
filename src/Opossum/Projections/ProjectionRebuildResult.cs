namespace Opossum.Projections;

/// <summary>
/// Result of a projection rebuild operation.
/// </summary>
public sealed record ProjectionRebuildResult
{
    /// <summary>
    /// Total number of projections that were rebuilt.
    /// </summary>
    public int TotalRebuilt { get; init; }

    /// <summary>
    /// Total duration of the rebuild operation.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Individual projection rebuild details.
    /// </summary>
    public required List<ProjectionRebuildDetail> Details { get; init; }

    /// <summary>
    /// Whether all projections rebuilt successfully.
    /// </summary>
    public bool Success => Details.All(d => d.Success);

    /// <summary>
    /// List of projections that failed to rebuild.
    /// </summary>
    public List<string> FailedProjections => 
        Details.Where(d => !d.Success).Select(d => d.ProjectionName).ToList();
}

/// <summary>
/// Details of a single projection rebuild.
/// </summary>
public sealed record ProjectionRebuildDetail
{
    /// <summary>
    /// Name of the projection that was rebuilt.
    /// </summary>
    public required string ProjectionName { get; init; }

    /// <summary>
    /// Whether the rebuild completed successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Duration of the rebuild operation.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Number of events processed during rebuild.
    /// </summary>
    public int EventsProcessed { get; init; }

    /// <summary>
    /// Error message if the rebuild failed, null otherwise.
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Current status of projection rebuilding.
/// </summary>
public sealed record ProjectionRebuildStatus
{
    /// <summary>
    /// Whether a rebuild is currently in progress.
    /// </summary>
    public bool IsRebuilding { get; init; }

    /// <summary>
    /// Projections currently being rebuilt.
    /// </summary>
    public required List<string> InProgressProjections { get; init; }

    /// <summary>
    /// Projections waiting to be rebuilt.
    /// </summary>
    public required List<string> QueuedProjections { get; init; }

    /// <summary>
    /// When the current rebuild started.
    /// </summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>
    /// Estimated completion time (null if not rebuilding).
    /// </summary>
    public DateTimeOffset? EstimatedCompletionAt { get; init; }
}
