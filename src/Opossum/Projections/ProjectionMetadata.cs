namespace Opossum.Projections;

/// <summary>
/// Metadata tracked for each projection instance.
/// Enables intelligent features like cache warming, retention policies, and archiving.
/// </summary>
public sealed record ProjectionMetadata
{
    /// <summary>
    /// When this projection was first created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }
    
    /// <summary>
    /// When this projection was last updated.
    /// </summary>
    public required DateTimeOffset LastUpdatedAt { get; init; }
    
    /// <summary>
    /// Number of times this projection has been updated (starts at 1).
    /// Incremented on each save operation.
    /// </summary>
    public required long Version { get; init; }
    
    /// <summary>
    /// Size of the projection file in bytes.
    /// Updated on each save operation.
    /// </summary>
    public required long SizeInBytes { get; init; }
}
