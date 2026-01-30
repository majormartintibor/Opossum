using Opossum.Core;

namespace Opossum.Storage.FileSystem;

/// <summary>
/// Manages reading and writing of individual event files in the file system.
/// Each event is stored as a separate JSON file with a zero-padded position-based name.
/// </summary>
internal sealed class EventFileManager
{
    private readonly JsonEventSerializer _serializer;
    private const int PositionPadding = 10; // Supports up to 10 billion events

    public EventFileManager()
    {
        _serializer = new JsonEventSerializer();
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
            await File.WriteAllTextAsync(tempPath, json);

            // Atomic move
            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            // Cleanup temp file on error
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* Ignore cleanup errors */ }
            }
            throw;
        }
    }

    /// <summary>
    /// Reads a SequencedEvent from a file at the specified position.
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

        var json = await File.ReadAllTextAsync(filePath);
        return _serializer.Deserialize(json);
    }

    /// <summary>
    /// Reads multiple SequencedEvents from files at the specified positions.
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

        var events = new SequencedEvent[positions.Length];

        for (int i = 0; i < positions.Length; i++)
        {
            events[i] = await ReadEventAsync(eventsPath, positions[i]);
        }

        return events;
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

        var fileName = $"{position.ToString($"D{PositionPadding}")}.json";
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
}
