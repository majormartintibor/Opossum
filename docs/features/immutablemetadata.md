// Fully immutable read-side hierarchy
public record Tag(string Key, string Value);     // already planned for #5

public record Metadata(
    DateTimeOffset Timestamp,
    Guid? CorrelationId = null,
    Guid? CausationId = null,
    Guid? OperationId = null,
    Guid? UserId = null);

public record DomainEvent(
    IEvent Event,
    IReadOnlyList<Tag> Tags,
    string? EventType = null)   // null → auto-derives from Event.GetType().Name
{
    public string EventType { get; init; } =
        EventType ?? Event.GetType().Name;
}

public record SequencedEvent(
    DomainEvent Event,
    long Position,
    Metadata Metadata);

// Framework-internal — consumer code never touches this
var patchedEvent = original with
{
    Event = original.Event with
    {
        Tags = [..original.Event.Tags, ..newTagsToAdd]
    }
};

// IEventStoreMaintenance implementation — inside FileSystemEventStore
private static IReadOnlyList<Tag> ComputeNewTags(
    IReadOnlyList<Tag> existing,
    IReadOnlyList<Tag> requested)
{
    var existingKeys = existing.Select(t => t.Key).ToHashSet(StringComparer.Ordinal);

    // Only tags whose key genuinely does not exist yet
    return [..requested.Where(t => !existingKeys.Contains(t.Key))];
}

public interface IEventStoreMaintenance
{
    // Parameter name is "tagsToAdd", return type says "added count" — not "modified"
    // There is no RemoveTagsAsync. There is no UpdateTagsAsync.
    // The only operation that exists is additive.
    Task<TagMigrationResult> AddTagsAsync(
        string eventType,
        Func<SequencedEvent, IReadOnlyList<Tag>> tagFactory,
        string? context = null,
        CancellationToken cancellationToken = default);
}