using Opossum.Core;

namespace Opossum.Storage.FileSystem;

/// <summary>
/// Manages an index of event positions by tag.
/// Stores positions in JSON files organized by tag key-value pairs.
/// Thread-safe: Uses internal locking to protect Read-Modify-Write operations.
/// </summary>
internal class TagIndex
{
    private static readonly char[] _invalidFileNameChars = Path.GetInvalidFileNameChars();

    // Per-instance lock for defense in depth
    // Protects Read-Modify-Write operations on index files
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Adds a position to the index for a specific tag.
    /// Creates the index file if it doesn't exist.
    /// Thread-safe: Protected by internal semaphore.
    /// </summary>
    public async Task AddPositionAsync(string indexPath, Tag tag, long position)
    {
        ArgumentNullException.ThrowIfNull(indexPath);
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentException.ThrowIfNullOrWhiteSpace(tag.Key);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(position);

        // Ensure index directory exists
        Directory.CreateDirectory(indexPath);

        var indexFilePath = GetIndexFilePath(indexPath, tag);

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
            await PositionIndexFile.WritePositionsAsync(indexFilePath, positions).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets all positions for a specific tag.
    /// Returns empty array if the index doesn't exist.
    /// </summary>
    public async Task<long[]> GetPositionsAsync(string indexPath, Tag tag)
    {
        ArgumentNullException.ThrowIfNull(indexPath);
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentException.ThrowIfNullOrWhiteSpace(tag.Key);

        var indexFilePath = GetIndexFilePath(indexPath, tag);

        if (!File.Exists(indexFilePath))
        {
            return [];
        }

        var positions = await PositionIndexFile.ReadPositionsAsync(indexFilePath).ConfigureAwait(false);
        return [.. positions];
    }

    /// <summary>
    /// Checks if an index file exists for the specified tag.
    /// </summary>
    public bool IndexExists(string indexPath, Tag tag)
    {
        ArgumentNullException.ThrowIfNull(indexPath);
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentException.ThrowIfNullOrWhiteSpace(tag.Key);

        var indexFilePath = GetIndexFilePath(indexPath, tag);
        return File.Exists(indexFilePath);
    }

    /// <summary>
    /// Gets the file path for a tag index.
    /// Uses a safe file name based on the tag key and value.
    /// </summary>
    private static string GetIndexFilePath(string indexPath, Tag tag)
    {
        // Create a safe file name from tag key and value
        var safeKey = GetSafeFileName(tag.Key);
        var safeValue = GetSafeFileName(tag.Value ?? "null");
        return Path.Combine(indexPath, "Tags", $"{safeKey}_{safeValue}.json");
    }

    /// <summary>
    /// Converts a string to a safe file name.
    /// </summary>
    private static string GetSafeFileName(string input)
    {
        var safeFileName = string.Join("_", input.Split(_invalidFileNameChars, StringSplitOptions.RemoveEmptyEntries));
        return safeFileName;
    }
}
