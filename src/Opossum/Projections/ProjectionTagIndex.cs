using Opossum.Core;

namespace Opossum.Projections;

/// <summary>
/// Manages tag-based indices for projections, enabling efficient querying without loading all projections into memory.
/// Similar to TagIndex for events, but stores projection keys instead of event positions.
/// </summary>
internal sealed class ProjectionTagIndex
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _tagLocks = new();
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Adds a projection key to the index for a specific tag.
    /// Thread-safe: Multiple concurrent calls for different tags won't block each other.
    /// </summary>
    /// <param name="projectionPath">Path to the projection folder (e.g., /Projections/StudentShortInfo)</param>
    /// <param name="tag">The tag to index</param>
    /// <param name="projectionKey">The projection key (e.g., student GUID)</param>
    public async Task AddProjectionAsync(string projectionPath, Tag tag, string projectionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionPath);
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionKey);

        var indexPath = GetIndexPath(projectionPath);
        Directory.CreateDirectory(indexPath);

        var indexFile = GetIndexFilePath(indexPath, tag);
        var tagLockKey = GetTagLockKey(projectionPath, tag);
        var semaphore = _tagLocks.GetOrAdd(tagLockKey, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            // Read existing keys
            HashSet<string> keys;
            if (File.Exists(indexFile))
            {
                var json = await File.ReadAllTextAsync(indexFile).ConfigureAwait(false);
                keys = JsonSerializer.Deserialize<HashSet<string>>(json, _jsonOptions) ?? [];
            }
            else
            {
                keys = [];
            }

            // Add new key (HashSet ensures no duplicates)
            keys.Add(projectionKey);

            // Write atomically: temp file + rename ensures readers always see a complete file
            var updatedJson = JsonSerializer.Serialize(keys, _jsonOptions);
            var tempFile = $"{indexFile}.tmp.{Guid.NewGuid():N}";
            try
            {
                await File.WriteAllTextAsync(tempFile, updatedJson).ConfigureAwait(false);
                File.Move(tempFile, indexFile, overwrite: true);
            }
            catch
            {
                if (File.Exists(tempFile))
                { try { File.Delete(tempFile); } catch { /* ignore cleanup errors */ } }
                throw;
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Removes a projection key from the index for a specific tag.
    /// Thread-safe operation.
    /// </summary>
    /// <param name="projectionPath">Path to the projection folder</param>
    /// <param name="tag">The tag to remove from</param>
    /// <param name="projectionKey">The projection key to remove</param>
    public async Task RemoveProjectionAsync(string projectionPath, Tag tag, string projectionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionPath);
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionKey);

        var indexPath = GetIndexPath(projectionPath);
        var indexFile = GetIndexFilePath(indexPath, tag);

        if (!File.Exists(indexFile))
        {
            return; // Nothing to remove
        }

        var tagLockKey = GetTagLockKey(projectionPath, tag);
        var semaphore = _tagLocks.GetOrAdd(tagLockKey, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            var json = await File.ReadAllTextAsync(indexFile).ConfigureAwait(false);
            var keys = JsonSerializer.Deserialize<HashSet<string>>(json, _jsonOptions) ?? [];

            if (keys.Remove(projectionKey))
            {
                if (keys.Count > 0)
                {
                    // Write atomically: temp file + rename ensures readers always see a complete file
                    var updatedJson = JsonSerializer.Serialize(keys, _jsonOptions);
                    var tempFile = $"{indexFile}.tmp.{Guid.NewGuid():N}";
                    try
                    {
                        await File.WriteAllTextAsync(tempFile, updatedJson).ConfigureAwait(false);
                        File.Move(tempFile, indexFile, overwrite: true);
                    }
                    catch
                    {
                        if (File.Exists(tempFile))
                        { try { File.Delete(tempFile); } catch { /* ignore cleanup errors */ } }
                        throw;
                    }
                }
                else
                {
                    // Delete empty index file
                    File.Delete(indexFile);
                }
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Retrieves all projection keys that match a specific tag.
    /// Case-insensitive comparison for tag key and value.
    /// </summary>
    /// <param name="projectionPath">Path to the projection folder</param>
    /// <param name="tag">The tag to query</param>
    /// <returns>Array of projection keys matching the tag</returns>
    public async Task<string[]> GetProjectionKeysByTagAsync(string projectionPath, Tag tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionPath);
        ArgumentNullException.ThrowIfNull(tag);

        var indexPath = GetIndexPath(projectionPath);
        var indexFile = GetIndexFilePath(indexPath, tag);

        if (!File.Exists(indexFile))
        {
            return [];
        }

        // No lock needed for reads: writes use atomic file moves (temp+rename), so readers
        // always see either the old or new complete file, never a partial write.
        try
        {
            var json = await File.ReadAllTextAsync(indexFile).ConfigureAwait(false);
            var keys = JsonSerializer.Deserialize<HashSet<string>>(json, _jsonOptions) ?? [];
            return [.. keys];
        }
        catch (JsonException)
        {
            // Corrupted index file — treat as empty; will be corrected on next write
            return [];
        }
    }

    /// <summary>
    /// Retrieves projection keys that match ALL specified tags (AND logic).
    /// Optimized to query smallest index first, then intersect with others.
    /// </summary>
    /// <param name="projectionPath">Path to the projection folder</param>
    /// <param name="tags">Tags to query (all must match)</param>
    /// <returns>Array of projection keys matching all tags</returns>
    public async Task<string[]> GetProjectionKeysByTagsAsync(string projectionPath, IEnumerable<Tag> tags)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionPath);
        ArgumentNullException.ThrowIfNull(tags);

        var tagArray = tags.ToArray();
        if (tagArray.Length == 0)
        {
            return [];
        }

        if (tagArray.Length == 1)
        {
            return await GetProjectionKeysByTagAsync(projectionPath, tagArray[0]).ConfigureAwait(false);
        }

        // Query each tag index
        var keySets = new List<HashSet<string>>();
        foreach (var tag in tagArray)
        {
            var keys = await GetProjectionKeysByTagAsync(projectionPath, tag).ConfigureAwait(false);
            if (keys.Length == 0)
            {
                // If any tag returns no results, intersection is empty
                return [];
            }
            keySets.Add([.. keys]);
        }

        // Find intersection (AND logic)
        // Start with smallest set for efficiency
        var sortedSets = keySets.OrderBy(s => s.Count).ToList();
        var result = sortedSets[0];
        foreach (var set in sortedSets.Skip(1))
        {
            result.IntersectWith(set);
            if (result.Count == 0)
            {
                return [];
            }
        }

        return [.. result];
    }

    /// <summary>
    /// Atomically updates projection tags by removing old tags and adding new tags.
    /// Used when a projection is updated and its tags change.
    /// </summary>
    /// <param name="projectionPath">Path to the projection folder</param>
    /// <param name="projectionKey">The projection key</param>
    /// <param name="oldTags">Previous tags to remove</param>
    /// <param name="newTags">New tags to add</param>
    public async Task UpdateProjectionTagsAsync(
        string projectionPath,
        string projectionKey,
        IEnumerable<Tag> oldTags,
        IEnumerable<Tag> newTags)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionKey);
        ArgumentNullException.ThrowIfNull(oldTags);
        ArgumentNullException.ThrowIfNull(newTags);

        var oldTagsList = oldTags.ToList();
        var newTagsList = newTags.ToList();

        // Find tags to remove (in old but not in new)
        var tagsToRemove = oldTagsList.Where(oldTag =>
            !newTagsList.Any(newTag =>
                string.Equals(oldTag.Key, newTag.Key, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(oldTag.Value, newTag.Value, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // Find tags to add (in new but not in old)
        var tagsToAdd = newTagsList.Where(newTag =>
            !oldTagsList.Any(oldTag =>
                string.Equals(oldTag.Key, newTag.Key, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(oldTag.Value, newTag.Value, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // Remove old tags
        foreach (var tag in tagsToRemove)
        {
            await RemoveProjectionAsync(projectionPath, tag, projectionKey).ConfigureAwait(false);
        }

        // Add new tags
        foreach (var tag in tagsToAdd)
        {
            await AddProjectionAsync(projectionPath, tag, projectionKey).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Deletes the entire Indices folder for a projection.
    /// Used during rebuild to ensure clean state.
    /// </summary>
    /// <param name="projectionPath">Path to the projection folder</param>
    public void DeleteAllIndices(string projectionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionPath);

        var indexPath = GetIndexPath(projectionPath);
        if (Directory.Exists(indexPath))
        {
            Directory.Delete(indexPath, recursive: true);
        }
    }

    private static string GetIndexPath(string projectionPath)
    {
        return Path.Combine(projectionPath, "Indices");
    }

    private static string GetIndexFilePath(string indexPath, Tag tag)
    {
        // Normalize to lowercase for case-insensitive matching across platforms
        // This ensures consistent behavior on Windows (case-insensitive) and Linux (case-sensitive)
        var safeKey = string.Join("_", tag.Key.ToLowerInvariant().Split(Path.GetInvalidFileNameChars()));
        var safeValue = string.Join("_", tag.Value.ToLowerInvariant().Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(indexPath, $"{safeKey}_{safeValue}.json");
    }

    private static string GetTagLockKey(string projectionPath, Tag tag)
    {
        // Use case-insensitive key for lock to ensure thread safety across case variations
        return $"{projectionPath}|{tag.Key.ToLowerInvariant()}|{tag.Value.ToLowerInvariant()}";
    }
}
