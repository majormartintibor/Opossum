namespace Opossum.Storage.FileSystem;

/// <summary>
/// Manages an index of event positions by event type.
/// Stores positions in JSON files organized by event type.
/// Thread-safe: Uses internal locking to protect Read-Modify-Write operations.
/// </summary>
internal class EventTypeIndex
{
    private static readonly char[] _invalidFileNameChars = Path.GetInvalidFileNameChars();

    // Per-instance lock for defense in depth
    // Protects Read-Modify-Write operations on index files
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly bool _flushImmediately;

    public EventTypeIndex(bool flushImmediately = false)
    {
        _flushImmediately = flushImmediately;
    }

    /// <summary>
    /// Adds a position to the index for a specific event type.
    /// Creates the index file if it doesn't exist.
    /// Thread-safe: Protected by internal semaphore.
    /// </summary>
    public async Task AddPositionAsync(string indexPath, string eventType, long position)
    {
        ArgumentNullException.ThrowIfNull(indexPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(position);

        // Ensure index directory exists
        Directory.CreateDirectory(indexPath);

        var indexFilePath = GetIndexFilePath(indexPath, eventType);

        // Acquire lock for Read-Modify-Write atomic operation
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Read existing positions or create new list
            var positions = await PositionIndexFile.ReadPositionsAsync(indexFilePath).ConfigureAwait(false);

            // Add position if not already present
            if (!positions.Contains(position))
            {
                positions.Add(position);
                positions.Sort(); // Keep positions sorted
            }

            // Write updated positions atomically
            await PositionIndexFile.WritePositionsAsync(indexFilePath, positions, _flushImmediately).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets all positions for a specific event type.
    /// Returns empty array if the index doesn't exist.
    /// </summary>
    public async Task<long[]> GetPositionsAsync(string indexPath, string eventType)
    {
        ArgumentNullException.ThrowIfNull(indexPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        var indexFilePath = GetIndexFilePath(indexPath, eventType);
        var positions = await PositionIndexFile.ReadPositionsAsync(indexFilePath).ConfigureAwait(false);
        return [.. positions];
    }

    /// <summary>
    /// Checks if an index file exists for the specified event type.
    /// </summary>
    public bool IndexExists(string indexPath, string eventType)
    {
        ArgumentNullException.ThrowIfNull(indexPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        var indexFilePath = GetIndexFilePath(indexPath, eventType);
        return File.Exists(indexFilePath);
    }

    /// <summary>
    /// Gets the file path for an event type index.
    /// Uses a safe file name based on the event type.
    /// </summary>
    private static string GetIndexFilePath(string indexPath, string eventType)
    {
        // Create a safe file name from event type
        var safeFileName = GetSafeFileName(eventType);
        return Path.Combine(indexPath, "EventType", $"{safeFileName}.json");
    }

    /// <summary>
    /// Converts an event type to a safe file name.
    /// </summary>
    private static string GetSafeFileName(string eventType)
    {
        var safeFileName = string.Join("_", eventType.Split(_invalidFileNameChars, StringSplitOptions.RemoveEmptyEntries));
        return safeFileName;
    }
}
