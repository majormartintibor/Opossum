using Opossum.Core;
using System.Text.Json;

namespace Opossum.Storage.FileSystem;

/// <summary>
/// Manages an index of event positions by tag.
/// Stores positions in JSON files organized by tag key-value pairs.
/// </summary>
internal class TagIndex
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Adds a position to the index for a specific tag.
    /// Creates the index file if it doesn't exist.
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

        var positions = await ReadPositionsAsync(indexFilePath);
        return positions.ToArray();
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
        // Replace invalid characters with underscores
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeFileName = string.Join("_", input.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
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
