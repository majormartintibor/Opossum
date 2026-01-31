using Opossum.Core;
using System.Text.Json;

namespace Opossum.Storage.FileSystem;

/// <summary>
/// Manages an index of event positions by event type.
/// Stores positions in JSON files organized by event type.
/// </summary>
internal class EventTypeIndex
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Adds a position to the index for a specific event type.
    /// Creates the index file if it doesn't exist.
    /// </summary>
    public async Task AddPositionAsync(string indexPath, string eventType, long position)
    {
        ArgumentNullException.ThrowIfNull(indexPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(position);

        // Ensure index directory exists
        Directory.CreateDirectory(indexPath);

        var indexFilePath = GetIndexFilePath(indexPath, eventType);

        // Read existing positions or create new list
        var positions = await ReadPositionsAsync(indexFilePath);

        // Add position if not already present
        if (!positions.Contains(position))
        {
            positions.Add(position);
            positions.Sort(); // Keep positions sorted
        }

        // Write updated positions atomically
        await WritePositionsAsync(indexFilePath, positions);
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

        var positions = await ReadPositionsAsync(indexFilePath);
        return positions.ToArray();
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
        // Replace invalid characters with underscores
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeFileName = string.Join("_", eventType.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
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

        try
        {
            var json = await File.ReadAllTextAsync(indexFilePath);
            var indexData = JsonSerializer.Deserialize<IndexData>(json);
            return indexData?.Positions ?? [];
        }
        catch (JsonException)
        {
            // Handle corrupted index file by returning empty list
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
        await File.WriteAllTextAsync(tempFilePath, json);

        // Atomic replace
        File.Move(tempFilePath, indexFilePath, overwrite: true);
    }

    /// <summary>
    /// Data structure for index file.
    /// </summary>
    private class IndexData
    {
        public List<long> Positions { get; set; } = [];
    }
}
