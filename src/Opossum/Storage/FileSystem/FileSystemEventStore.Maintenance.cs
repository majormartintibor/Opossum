using Opossum.Core;

namespace Opossum.Storage.FileSystem;

internal sealed partial class FileSystemEventStore : IEventStoreMaintenance
{
    public async Task<TagMigrationResult> AddTagsAsync(
        string eventType,
        Func<SequencedEvent, IReadOnlyList<Tag>> tagFactory,
        string? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentNullException.ThrowIfNull(tagFactory);

        if (_options.Contexts.Count == 0)
            throw new InvalidOperationException("No contexts configured.");

        var contextPath = GetContextPath(_options.Contexts[0]);
        var eventsPath = GetEventsPath(contextPath);

        var positions = await _indexManager.GetPositionsByEventTypeAsync(contextPath, eventType).ConfigureAwait(false);

        var totalTagsAdded = 0;

        foreach (var position in positions)
        {
            await _appendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var existing = await _eventFileManager.ReadEventAsync(eventsPath, position).ConfigureAwait(false);
                var requested = tagFactory(existing);
                var newTags = ComputeNewTags(existing.Event.Tags, requested);

                if (newTags.Count == 0)
                    continue;

                var patched = existing with
                {
                    Event = existing.Event with
                    {
                        Tags = [..existing.Event.Tags, ..newTags]
                    }
                };

                await _eventFileManager.WriteEventAsync(eventsPath, patched).ConfigureAwait(false);
                await _indexManager.AddTagsToIndexAsync(contextPath, newTags, position).ConfigureAwait(false);

                totalTagsAdded += newTags.Count;
            }
            finally
            {
                _appendLock.Release();
            }
        }

        return new TagMigrationResult(totalTagsAdded, positions.Length);
    }

    private static IReadOnlyList<Tag> ComputeNewTags(IReadOnlyList<Tag> existing, IReadOnlyList<Tag> requested)
    {
        var existingKeys = existing.Select(t => t.Key).ToHashSet(StringComparer.Ordinal);
        return [..requested.Where(t => !existingKeys.Contains(t.Key))];
    }
}
