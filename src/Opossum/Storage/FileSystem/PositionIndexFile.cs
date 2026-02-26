namespace Opossum.Storage.FileSystem;

/// <summary>
/// Shared I/O utility for position-based index files.
/// Handles atomic reads, atomic writes (temp-file + move strategy), and retry logic.
/// Used by <see cref="TagIndex"/> and <see cref="EventTypeIndex"/> to eliminate duplicated
/// file-system plumbing.
/// </summary>
internal static class PositionIndexFile
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Reads positions from an index file.
    /// Returns empty list if the file does not exist or is corrupted.
    /// Retries on transient I/O contention with exponential back-off.
    /// </summary>
    public static async Task<List<long>> ReadPositionsAsync(string indexFilePath)
    {
        if (!File.Exists(indexFilePath))
        {
            return [];
        }

        var maxRetries = 3;  // reads are non-destructive — fail fast
        var retryDelay = 1;   // 1 ms is sufficient for transient read conflicts

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
                return [];
            }
            catch (IOException) when (attempt < maxRetries - 1)
            {
                await Task.Delay(retryDelay).ConfigureAwait(false);
                retryDelay *= 2;
            }
            catch (UnauthorizedAccessException) when (attempt < maxRetries - 1)
            {
                await Task.Delay(retryDelay).ConfigureAwait(false);
                retryDelay *= 2;
            }
        }

        // Final attempt — let exceptions propagate
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
    /// Writes positions to an index file atomically using a temp-file + move strategy.
    /// Ensures readers always see either the old or the new complete file, never a partial write.
    /// When <paramref name="flushToDisk"/> is <see langword="true"/>, the temp file is physically
    /// flushed to storage via <see cref="RandomAccess.FlushToDisk"/> before the atomic rename,
    /// providing the same durability guarantee as event and ledger files.
    /// </summary>
    public static async Task WritePositionsAsync(string indexFilePath, List<long> positions, bool flushToDisk = false)
    {
        var indexData = new IndexData { Positions = positions };
        var json = JsonSerializer.Serialize(indexData, _jsonOptions);

        var directory = Path.GetDirectoryName(indexFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempFilePath = $"{indexFilePath}.tmp.{Guid.NewGuid():N}";

        await using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        await using (var writer = new StreamWriter(fileStream))
        {
            await writer.WriteAsync(json).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        }

        if (flushToDisk)
        {
            using var handle = File.OpenHandle(tempFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            await Task.Run(() => RandomAccess.FlushToDisk(handle)).ConfigureAwait(false);
        }

        await AtomicMoveWithRetryAsync(tempFilePath, indexFilePath).ConfigureAwait(false);
    }

    /// <summary>
    /// Atomically moves a file with exponential back-off retry to handle transient access conflicts.
    /// </summary>
    public static async Task AtomicMoveWithRetryAsync(string sourcePath, string destinationPath, int maxRetries = 10)
    {
        var retryDelay = 20;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                File.Move(sourcePath, destinationPath, overwrite: true);
                return;
            }
            catch (UnauthorizedAccessException) when (attempt < maxRetries - 1)
            {
                await Task.Delay(retryDelay).ConfigureAwait(false);
                retryDelay *= 2;
            }
            catch (IOException) when (attempt < maxRetries - 1)
            {
                await Task.Delay(retryDelay).ConfigureAwait(false);
                retryDelay *= 2;
            }
        }

        File.Move(sourcePath, destinationPath, overwrite: true);
    }

    private sealed class IndexData
    {
        public List<long> Positions { get; set; } = [];
    }
}
