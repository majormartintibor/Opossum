namespace Opossum.Storage.FileSystem;

/// <summary>
/// Manages an index of event positions by event type.
/// Stores positions in JSON files organized by event type.
/// Thread-safe: Uses internal locking to protect Read-Modify-Write operations.
/// </summary>
internal class EventTypeIndex
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly char[] _invalidFileNameChars = Path.GetInvalidFileNameChars();

    // Per-instance lock for defense in depth
    // Protects Read-Modify-Write operations on index files
    private readonly SemaphoreSlim _lock = new(1, 1);

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
            var positions = await ReadPositionsAsync(indexFilePath).ConfigureAwait(false);

            // Add position if not already present
            if (!positions.Contains(position))
            {
                positions.Add(position);
                positions.Sort(); // Keep positions sorted
            }

            // Write updated positions atomically
            await WritePositionsAsync(indexFilePath, positions).ConfigureAwait(false);
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

        if (!File.Exists(indexFilePath))
        {
            return [];
        }

        var positions = await ReadPositionsAsync(indexFilePath).ConfigureAwait(false);
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

    /// <summary>
    /// Reads positions from an index file.
    /// Returns empty list if file doesn't exist or is corrupted.
    /// </summary>
    private static async Task<List<long>> ReadPositionsAsync(string indexFilePath)
    {
        if (!File.Exists(indexFilePath))
        {
            return [];
        }

        // Retry logic for concurrent read/write scenarios
        var maxRetries = 5;
        var retryDelay = 10;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                var json = await File.ReadAllTextAsync(indexFilePath).ConfigureAwait(false);
                var indexData = JsonSerializer.Deserialize<IndexData>(json);
                return indexData?.Positions ?? [];
            }
            catch (JsonException)
            {
                // Handle corrupted index file by returning empty list
                return [];
            }
            catch (IOException) when (attempt < maxRetries - 1)
            {
                // File might be locked by a writer, wait and retry
                await Task.Delay(retryDelay).ConfigureAwait(false);
                retryDelay *= 2;
            }
            catch (UnauthorizedAccessException) when (attempt < maxRetries - 1)
            {
                // File might be being replaced, wait and retry
                await Task.Delay(retryDelay).ConfigureAwait(false);
                retryDelay *= 2;
            }
        }

        // Final attempt without catching IO exceptions
        try
        {
            var json = await File.ReadAllTextAsync(indexFilePath).ConfigureAwait(false);
            var indexData = JsonSerializer.Deserialize<IndexData>(json);
            return indexData?.Positions ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Writes positions to an index file atomically using temp file strategy.
    /// </summary>
    private static async Task WritePositionsAsync(string indexFilePath, List<long> positions)
    {
        var indexData = new IndexData { Positions = positions };
        var json = JsonSerializer.Serialize(indexData, _jsonOptions);

        // Ensure directory exists
        var directory = Path.GetDirectoryName(indexFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write to temp file first
        var tempFilePath = $"{indexFilePath}.tmp.{Guid.NewGuid():N}";

        // Use FileStream to ensure file is properly closed before move
        await using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        await using (var writer = new StreamWriter(fileStream))
        {
            await writer.WriteAsync(json).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        } // FileStream is disposed here, ensuring file handle is released

        // Atomic replace with retry logic
        await AtomicMoveWithRetryAsync(tempFilePath, indexFilePath).ConfigureAwait(false);
    }

    /// <summary>
    /// Atomically moves a file with retry logic to handle concurrent access.
    /// </summary>
    private static async Task AtomicMoveWithRetryAsync(string sourcePath, string destinationPath, int maxRetries = 10)
    {
        var retryDelay = 20; // Start with 20ms

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                File.Move(sourcePath, destinationPath, overwrite: true);
                return; // Success
            }
            catch (UnauthorizedAccessException) when (attempt < maxRetries - 1)
            {
                // File might be locked by a reader, wait and retry
                await Task.Delay(retryDelay).ConfigureAwait(false);
                retryDelay *= 2; // Exponential backoff
            }
            catch (IOException) when (attempt < maxRetries - 1)
            {
                // File might be in use, wait and retry
                await Task.Delay(retryDelay).ConfigureAwait(false);
                retryDelay *= 2; // Exponential backoff
            }
        }

        // Final attempt without catching exceptions
        File.Move(sourcePath, destinationPath, overwrite: true);
    }

    /// <summary>
    /// Data structure for index file.
    /// </summary>
    private class IndexData
    {
        public List<long> Positions { get; set; } = [];
    }
}
