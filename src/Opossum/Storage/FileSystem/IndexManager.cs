using Opossum.Core;

namespace Opossum.Storage.FileSystem;

/// <summary>
/// Manages indices for efficient event querying.
/// Coordinates EventTypeIndex and TagIndex to maintain fast lookups.
/// </summary>
internal class IndexManager
{
    private readonly EventTypeIndex _eventTypeIndex;
    private readonly TagIndex _tagIndex;

    public IndexManager()
    {
        _eventTypeIndex = new EventTypeIndex();
        _tagIndex = new TagIndex();
    }

    // Constructor for testing with dependency injection
    internal IndexManager(EventTypeIndex eventTypeIndex, TagIndex tagIndex)
    {
        _eventTypeIndex = eventTypeIndex;
        _tagIndex = tagIndex;
    }

    /// <summary>
    /// Adds an event to all relevant indices (EventType and Tags).
    /// </summary>
    public async Task AddEventToIndicesAsync(string contextPath, SequencedEvent sequencedEvent)
    {
        ArgumentNullException.ThrowIfNull(contextPath);
        ArgumentNullException.ThrowIfNull(sequencedEvent);
        ArgumentNullException.ThrowIfNull(sequencedEvent.Event);

        var indexPath = GetIndexPath(contextPath);

        // Add to EventType index
        await _eventTypeIndex.AddPositionAsync(indexPath, sequencedEvent.Event.EventType, sequencedEvent.Position).ConfigureAwait(false);

        // Add to Tag indices
        if (sequencedEvent.Event.Tags != null)
        {
            foreach (var tag in sequencedEvent.Event.Tags)
            {
                await _tagIndex.AddPositionAsync(indexPath, tag, sequencedEvent.Position).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Gets all positions for a specific event type.
    /// </summary>
    public async Task<long[]> GetPositionsByEventTypeAsync(string contextPath, string eventType)
    {
        ArgumentNullException.ThrowIfNull(contextPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        var indexPath = GetIndexPath(contextPath);
        return await _eventTypeIndex.GetPositionsAsync(indexPath, eventType).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets all positions that match any of the specified event types.
    /// Returns positions in sorted order without duplicates.
    /// </summary>
    public async Task<long[]> GetPositionsByEventTypesAsync(string contextPath, string[] eventTypes)
    {
        ArgumentNullException.ThrowIfNull(contextPath);
        ArgumentNullException.ThrowIfNull(eventTypes);

        if (eventTypes.Length == 0)
        {
            return [];
        }

        if (eventTypes.Length == 1)
        {
            return await GetPositionsByEventTypeAsync(contextPath, eventTypes[0]).ConfigureAwait(false);
        }

        var indexPath = GetIndexPath(contextPath);

        // Load all event-type position arrays concurrently to minimise wall-clock I/O time.
        // Each index file is independent so reads can safely run in parallel.
        var positionArrays = await Task.WhenAll(
            eventTypes.Select(et => _eventTypeIndex.GetPositionsAsync(indexPath, et))
        ).ConfigureAwait(false);

        return SortedMerge(positionArrays);
    }

    /// <summary>
    /// Gets all positions for a specific tag.
    /// </summary>
    public async Task<long[]> GetPositionsByTagAsync(string contextPath, Tag tag)
    {
        ArgumentNullException.ThrowIfNull(contextPath);
        ArgumentNullException.ThrowIfNull(tag);

        var indexPath = GetIndexPath(contextPath);
        return await _tagIndex.GetPositionsAsync(indexPath, tag).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets all positions that match any of the specified tags.
    /// Returns positions in sorted order without duplicates.
    /// </summary>
    public async Task<long[]> GetPositionsByTagsAsync(string contextPath, Tag[] tags)
    {
        ArgumentNullException.ThrowIfNull(contextPath);
        ArgumentNullException.ThrowIfNull(tags);

        if (tags.Length == 0)
        {
            return [];
        }

        if (tags.Length == 1)
        {
            return await GetPositionsByTagAsync(contextPath, tags[0]).ConfigureAwait(false);
        }

        var indexPath = GetIndexPath(contextPath);

        // Load all tag position arrays concurrently to minimise wall-clock I/O time.
        var positionArrays = await Task.WhenAll(
            tags.Select(t => _tagIndex.GetPositionsAsync(indexPath, t))
        ).ConfigureAwait(false);

        return SortedMerge(positionArrays);
    }

    /// <summary>
    /// Checks if an EventType index exists.
    /// </summary>
    public bool EventTypeIndexExists(string contextPath, string eventType)
    {
        ArgumentNullException.ThrowIfNull(contextPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        var indexPath = GetIndexPath(contextPath);
        return _eventTypeIndex.IndexExists(indexPath, eventType);
    }

    /// <summary>
    /// Checks if a Tag index exists.
    /// </summary>
    public bool TagIndexExists(string contextPath, Tag tag)
    {
        ArgumentNullException.ThrowIfNull(contextPath);
        ArgumentNullException.ThrowIfNull(tag);

        var indexPath = GetIndexPath(contextPath);
        return _tagIndex.IndexExists(indexPath, tag);
    }

    /// <summary>
    /// Adds the specified tags to the index for an already-persisted event position.
    /// Used by maintenance operations to retroactively index newly-added tags.
    /// </summary>
    public async Task AddTagsToIndexAsync(string contextPath, IReadOnlyList<Tag> tags, long position)
    {
        ArgumentNullException.ThrowIfNull(contextPath);
        ArgumentNullException.ThrowIfNull(tags);

        var indexPath = GetIndexPath(contextPath);
        foreach (var tag in tags)
        {
            await _tagIndex.AddPositionAsync(indexPath, tag, position).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Gets the index directory path for a context.
    /// </summary>
    private static string GetIndexPath(string contextPath)
    {
        return Path.Combine(contextPath, "Indices");
    }

    /// <summary>
    /// Merges multiple pre-sorted arrays into a single sorted array without duplicates.
    /// Uses a k-way merge — O(N × K) where N is the total number of positions and K is the
    /// number of arrays. Avoids the O(N log N) re-sort that a <see cref="HashSet{T}"/> approach requires.
    /// Positions are always positive (validated at write time), so <see cref="long.MinValue"/> is a
    /// safe sentinel for the deduplication guard.
    /// </summary>
    private static long[] SortedMerge(long[][] sortedArrays)
    {
        // Filter out empty arrays to simplify the merge loop.
        var nonEmpty = sortedArrays.Where(a => a.Length > 0).ToArray();

        if (nonEmpty.Length == 0) return [];
        if (nonEmpty.Length == 1) return nonEmpty[0];

        var totalCount = nonEmpty.Sum(a => a.Length);
        var result = new long[totalCount];
        var indices = new int[nonEmpty.Length];
        var resultIdx = 0;
        var last = long.MinValue;

        while (true)
        {
            var minVal = long.MaxValue;
            var minArr = -1;

            for (var i = 0; i < nonEmpty.Length; i++)
            {
                if (indices[i] < nonEmpty[i].Length && nonEmpty[i][indices[i]] < minVal)
                {
                    minVal = nonEmpty[i][indices[i]];
                    minArr = i;
                }
            }

            if (minArr < 0) break;

            indices[minArr]++;

            if (minVal != last)
            {
                result[resultIdx++] = minVal;
                last = minVal;
            }
        }

        return resultIdx == totalCount ? result : result[..resultIdx];
    }
}
