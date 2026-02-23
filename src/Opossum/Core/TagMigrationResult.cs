namespace Opossum.Core;

/// <summary>
/// Result of an <see cref="IEventStoreMaintenance.AddTagsAsync"/> operation.
/// </summary>
/// <param name="TagsAdded">Total number of individual tag entries written to the store and its indices.</param>
/// <param name="EventsProcessed">Number of events of the requested type that were examined.</param>
public record TagMigrationResult(int TagsAdded, int EventsProcessed);
