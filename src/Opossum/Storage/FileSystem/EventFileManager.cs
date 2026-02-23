using System.Buffers;
using System.Text;
using Opossum.Core;

namespace Opossum.Storage.FileSystem;

/// <summary>
/// Manages reading and writing of individual event files in the file system.
/// Each event is stored as a separate JSON file with a zero-padded position-based name.
/// </summary>
internal sealed class EventFileManager
{
    private readonly JsonEventSerializer _serializer;
    private readonly bool _flushImmediately;
    private readonly bool _writeProtect;
    private const int PositionPadding = 10; // Supports up to 10 billion events

    public EventFileManager(bool flushImmediately = true, bool writeProtect = true)
    {
        _serializer = new JsonEventSerializer();
        _flushImmediately = flushImmediately;
        _writeProtect = writeProtect;
    }

    /// <summary>
    /// Writes a SequencedEvent to a file in the specified events directory.
    /// File name format: {position:D10}.json (e.g., 0000000001.json)
    /// </summary>
    /// <param name="eventsPath">Path to the Events directory</param>
    /// <param name="sequencedEvent">The event to write</param>
    /// <exception cref="ArgumentNullException">When parameters are null</exception>
    /// <exception cref="IOException">When file write fails</exception>
    public async Task WriteEventAsync(string eventsPath, SequencedEvent sequencedEvent)
    {
        ArgumentNullException.ThrowIfNull(eventsPath);
        ArgumentNullException.ThrowIfNull(sequencedEvent);

        if (sequencedEvent.Position <= 0)
        {
            throw new ArgumentException("Event position must be greater than 0", nameof(sequencedEvent));
        }

        // Ensure events directory exists
        Directory.CreateDirectory(eventsPath);

        var filePath = GetEventFilePath(eventsPath, sequencedEvent.Position);
        var json = _serializer.Serialize(sequencedEvent);

        // Write atomically using temp file strategy
        var tempPath = filePath + $".tmp.{Guid.NewGuid():N}";

        try
        {
            // Write to temp file
            await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);

            // DURABILITY GUARANTEE: Flush to disk before making event visible
            // This ensures event is physically on disk before we move it to final location.
            // Critical for maintaining DCB guarantees and preventing data loss on power failure.
            if (_flushImmediately)
            {
                await FlushFileToDiskAsync(tempPath).ConfigureAwait(false);
            }

            // If write protection is enabled and the destination already exists (maintenance
            // rewrite via AddTagsAsync), remove the read-only attribute before the overwrite.
            // File.Move with overwrite:true throws UnauthorizedAccessException on Windows
            // when the destination file is read-only.
            if (_writeProtect && File.Exists(filePath))
            {
                var existing = File.GetAttributes(filePath);
                if ((existing & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(filePath, existing & ~FileAttributes.ReadOnly);
            }

            // Atomic move (now safe - data is on disk if flushing is enabled)
            File.Move(tempPath, filePath, overwrite: true);

            // Mark committed event file as read-only so it cannot be accidentally
            // modified or deleted. All Opossum read operations use FileAccess.Read
            // and are unaffected by this attribute.
            if (_writeProtect)
                File.SetAttributes(filePath, FileAttributes.ReadOnly);
        }
        catch
        {
            // Cleanup temp file on error
            if (File.Exists(tempPath))
            {
                try
                { File.Delete(tempPath); }
                catch { /* Ignore cleanup errors */ }
            }
            throw;
        }
    }

    /// <summary>
    /// Reads a SequencedEvent from a file at the specified position.
    /// Uses optimized FileStream with 1KB buffer for small JSON files (Strategy 3).
    /// Uses ArrayPool to reduce GC pressure from buffer allocations (Strategy 4).
    /// </summary>
    /// <param name="eventsPath">Path to the Events directory</param>
    /// <param name="position">The sequence position to read</param>
    /// <returns>The SequencedEvent at that position</returns>
    /// <exception cref="ArgumentNullException">When eventsPath is null</exception>
    /// <exception cref="FileNotFoundException">When event file doesn't exist</exception>
    /// <exception cref="JsonException">When event file is corrupt</exception>
    public async Task<SequencedEvent> ReadEventAsync(string eventsPath, long position)
    {
        ArgumentNullException.ThrowIfNull(eventsPath);

        if (position <= 0)
        {
            throw new ArgumentException("Position must be greater than 0", nameof(position));
        }

        var filePath = GetEventFilePath(eventsPath, position);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Event file not found for position {position}", filePath);
        }

        // Use FileStream with 1KB buffer for small JSON files (reduces GC pressure)
        // FileOptions.SequentialScan hints to Windows to optimize read-ahead buffering
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        // Use ArrayPool to reduce allocations (Strategy 4: 30-50% fewer Gen0 collections)
        const int maxBufferSize = 16 * 1024; // 16KB max for event files
        var pool = ArrayPool<byte>.Shared;
        byte[] buffer = pool.Rent(maxBufferSize);

        try
        {
            int totalBytesRead = 0;
            int bytesRead;

            // Read the file in chunks
            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(totalBytesRead, maxBufferSize - totalBytesRead)).ConfigureAwait(false)) > 0)
            {
                totalBytesRead += bytesRead;

                if (totalBytesRead >= maxBufferSize)
                {
                    // File is larger than expected, fall back to StreamReader
                    stream.Position = 0;
                    using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
                    var json = await reader.ReadToEndAsync().ConfigureAwait(false);
                    return _serializer.Deserialize(json);
                }
            }

            // Convert bytes to string and deserialize
            var jsonString = Encoding.UTF8.GetString(buffer, 0, totalBytesRead);
            return _serializer.Deserialize(jsonString);
        }
        finally
        {
            // Always return buffer to pool
            pool.Return(buffer);
        }
    }

    /// <summary>
    /// Reads multiple SequencedEvents from files at the specified positions.
    /// Uses parallel reads to saturate SSD I/O and utilize multiple CPU cores (Strategy 1).
    /// Returns events in the same order as the positions array.
    /// </summary>
    /// <param name="eventsPath">Path to the Events directory</param>
    /// <param name="positions">Array of positions to read</param>
    /// <returns>Array of SequencedEvents in the same order as positions</returns>
    /// <exception cref="ArgumentNullException">When parameters are null</exception>
    /// <exception cref="FileNotFoundException">When any event file doesn't exist</exception>
    public async Task<SequencedEvent[]> ReadEventsAsync(string eventsPath, long[] positions)
    {
        ArgumentNullException.ThrowIfNull(eventsPath);
        ArgumentNullException.ThrowIfNull(positions);

        if (positions.Length == 0)
        {
            return [];
        }

        // For small batches, sequential read is more efficient (avoid parallelization overhead)
        if (positions.Length < 10)
        {
            var events = new SequencedEvent[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                events[i] = await ReadEventAsync(eventsPath, positions[i]).ConfigureAwait(false);
            }
            return events;
        }

        // Parallel read for larger batches using Parallel.ForEachAsync (optimal for I/O-bound work)
        var parallelEvents = new SequencedEvent[positions.Length];
        var options = new ParallelOptions
        {
            // 2x CPU count for I/O-bound work to keep SSD saturated
            // Note: Increasing beyond 2x shows diminishing returns due to file system contention
            // and context switching overhead. Benchmark results show 2x is optimal.
            MaxDegreeOfParallelism = Environment.ProcessorCount * 2
        };

        await Parallel.ForEachAsync(
            Enumerable.Range(0, positions.Length),
            options,
            async (i, ct) =>
            {
                parallelEvents[i] = await ReadEventAsync(eventsPath, positions[i]).ConfigureAwait(false);
            }).ConfigureAwait(false);

        return parallelEvents;
    }

    /// <summary>
    /// Gets the file path for an event at the specified position.
    /// Format: {eventsPath}/{position:D10}.json
    /// </summary>
    /// <param name="eventsPath">Path to the Events directory</param>
    /// <param name="position">The sequence position</param>
    /// <returns>Full file path for the event file</returns>
    public string GetEventFilePath(string eventsPath, long position)
    {
        ArgumentNullException.ThrowIfNull(eventsPath);

        if (position <= 0)
        {
            throw new ArgumentException("Position must be greater than 0", nameof(position));
        }

        var fileName = $"{position:D10}.json";
        return Path.Combine(eventsPath, fileName);
    }

    /// <summary>
    /// Checks if an event file exists for the specified position.
    /// </summary>
    /// <param name="eventsPath">Path to the Events directory</param>
    /// <param name="position">The sequence position</param>
    /// <returns>True if the event file exists, false otherwise</returns>
    public bool EventFileExists(string eventsPath, long position)
    {
        ArgumentNullException.ThrowIfNull(eventsPath);

        if (position <= 0)
        {
            return false;
        }

        var filePath = GetEventFilePath(eventsPath, position);
        return File.Exists(filePath);
    }

    /// <summary>
    /// Flushes file data to physical disk, ensuring durability.
    /// Uses RandomAccess.FlushToDisk for maximum safety on .NET 10.
    /// </summary>
    /// <param name="filePath">Path to the file to flush</param>
    private static async Task FlushFileToDiskAsync(string filePath)
    {
        // Use SafeFileHandle for proper async I/O in .NET 10
        using var handle = File.OpenHandle(
            filePath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None,
            FileOptions.None);

        // RandomAccess.FlushToDisk ensures data is physically written to storage
        // This is critical for durability - prevents data loss on power failure
        await Task.Run(() => RandomAccess.FlushToDisk(handle)).ConfigureAwait(false);
    }
}
