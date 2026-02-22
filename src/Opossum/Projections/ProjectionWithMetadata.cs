namespace Opossum.Projections;

/// <summary>
/// Internal wrapper that combines projection data with metadata.
/// Used for serialization/deserialization to/from JSON files.
/// </summary>
/// <typeparam name="TState">The projection state type</typeparam>
internal sealed record ProjectionWithMetadata<TState>
{
    /// <summary>
    /// The actual projection data/state.
    /// </summary>
    public required TState Data { get; init; }

    /// <summary>
    /// Metadata about the projection lifecycle.
    /// </summary>
    public required ProjectionMetadata Metadata { get; init; }
}
