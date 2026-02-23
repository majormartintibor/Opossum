using Opossum.Core;

namespace Opossum;

/// <summary>
/// Additive-only maintenance operations for the event store.
/// All operations are strictly additive — no existing data can be modified or removed.
/// </summary>
public interface IEventStoreMaintenance
{
    /// <summary>
    /// Retroactively adds tags to all stored events of the specified <paramref name="eventType"/>.
    /// Only tags whose key does not already exist on an event are written; existing tags are never
    /// modified or deleted.
    /// </summary>
    /// <param name="eventType">The event type name (matches <see cref="DomainEvent.EventType"/>).</param>
    /// <param name="tagFactory">
    /// A delegate invoked for each matching event. Return the tags that should be added;
    /// the framework discards any tag whose key already exists on the event.
    /// </param>
    /// <param name="context">
    /// The context to target. When <see langword="null"/> the first configured context is used.
    /// </param>
    /// <param name="cancellationToken">Token that can cancel the operation between events.</param>
    /// <returns>
    /// A <see cref="TagMigrationResult"/> summarising how many tags were added and how many
    /// events were examined.
    /// </returns>
    Task<TagMigrationResult> AddTagsAsync(
        string eventType,
        Func<SequencedEvent, IReadOnlyList<Tag>> tagFactory,
        string? context = null,
        CancellationToken cancellationToken = default);
}
