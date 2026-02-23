using Opossum.Core;

namespace Opossum.Projections;

/// <summary>
/// Defines how to extract indexable tags from a projection state.
/// Tag providers enable efficient querying of projections without loading all data into memory.
/// </summary>
/// <typeparam name="TState">The projection state type</typeparam>
public interface IProjectionTagProvider<in TState> where TState : class
{
    /// <summary>
    /// Extracts indexable tags from the projection state.
    /// This method is called whenever a projection is saved or updated.
    /// The returned tags are used to build indices for efficient querying.
    /// </summary>
    /// <param name="state">The projection state to extract tags from</param>
    /// <returns>Collection of tags that should be indexed for this projection</returns>
    /// <remarks>
    /// Tags should be stable and deterministic - the same state should always produce the same tags.
    /// Common tag patterns:
    /// - Status flags: new Tag("IsActive", "true")
    /// - Enum values: new Tag("Status", state.Status.ToString())
    /// - Boolean flags: new Tag("IsMaxedOut", state.IsMaxedOut.ToString())
    /// </remarks>
    IEnumerable<Tag> GetTags(TState state);
}
