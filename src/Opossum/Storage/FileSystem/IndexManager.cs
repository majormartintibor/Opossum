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
        await _eventTypeIndex.AddPositionAsync(indexPath, sequencedEvent.Event.EventType, sequencedEvent.Position);

        // Add to Tag indices
        if (sequencedEvent.Event.Tags != null)
        {
            foreach (var tag in sequencedEvent.Event.Tags)
            {
                await _tagIndex.AddPositionAsync(indexPath, tag, sequencedEvent.Position);
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
        return await _eventTypeIndex.GetPositionsAsync(indexPath, eventType);
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

        var indexPath = GetIndexPath(contextPath);
        var allPositions = new HashSet<long>();

        foreach (var eventType in eventTypes)
        {
            var positions = await _eventTypeIndex.GetPositionsAsync(indexPath, eventType);
            foreach (var position in positions)
            {
                allPositions.Add(position);
            }
        }

        var result = allPositions.ToArray();
        Array.Sort(result);
        return result;
    }

    /// <summary>
    /// Gets all positions for a specific tag.
    /// </summary>
    public async Task<long[]> GetPositionsByTagAsync(string contextPath, Tag tag)
    {
        ArgumentNullException.ThrowIfNull(contextPath);
        ArgumentNullException.ThrowIfNull(tag);

        var indexPath = GetIndexPath(contextPath);
        return await _tagIndex.GetPositionsAsync(indexPath, tag);
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

        var indexPath = GetIndexPath(contextPath);
        var allPositions = new HashSet<long>();

        foreach (var tag in tags)
        {
            var positions = await _tagIndex.GetPositionsAsync(indexPath, tag);
            foreach (var position in positions)
            {
                allPositions.Add(position);
            }
        }

        var result = allPositions.ToArray();
        Array.Sort(result);
        return result;
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
    /// Gets the index directory path for a context.
    /// </summary>
    private static string GetIndexPath(string contextPath)
    {
        return Path.Combine(contextPath, "index");
    }
}
