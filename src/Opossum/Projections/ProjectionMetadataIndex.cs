namespace Opossum.Projections;

/// <summary>
/// Manages a centralized index of projection metadata for fast queries.
/// Stores metadata in /Projections/{ProjectionName}/Metadata/index.json
/// Thread-safe for concurrent operations.
/// </summary>
internal sealed class ProjectionMetadataIndex
{
    private readonly ConcurrentDictionary<string, ProjectionMetadata> _cache = new();
    private readonly SemaphoreSlim _indexLock = new(1, 1);
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Saves or updates metadata for a projection.
    /// Thread-safe operation.
    /// </summary>
    /// <param name="projectionPath">Path to the projection folder</param>
    /// <param name="key">Projection key</param>
    /// <param name="metadata">Metadata to save</param>
    public async Task SaveAsync(string projectionPath, string key, ProjectionMetadata metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(metadata);

        await _indexLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Update cache
            _cache[key] = metadata;

            // Persist to disk
            await PersistIndexAsync(projectionPath).ConfigureAwait(false);
        }
        finally
        {
            _indexLock.Release();
        }
    }

    /// <summary>
    /// Gets metadata for a specific projection.
    /// Returns null if not found.
    /// </summary>
    /// <param name="projectionPath">Path to the projection folder</param>
    /// <param name="key">Projection key</param>
    public async Task<ProjectionMetadata?> GetAsync(string projectionPath, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        // Try cache first
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        // Load from disk if not in cache
        await LoadIndexAsync(projectionPath).ConfigureAwait(false);

        return _cache.TryGetValue(key, out var metadata) ? metadata : null;
    }

    /// <summary>
    /// Gets all metadata entries for a projection type.
    /// </summary>
    /// <param name="projectionPath">Path to the projection folder</param>
    public async Task<IReadOnlyDictionary<string, ProjectionMetadata>> GetAllAsync(string projectionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionPath);

        await LoadIndexAsync(projectionPath).ConfigureAwait(false);

        return _cache.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>
    /// Gets projections updated since a specific date.
    /// </summary>
    /// <param name="projectionPath">Path to the projection folder</param>
    /// <param name="since">Date threshold</param>
    public async Task<IReadOnlyList<(string Key, ProjectionMetadata Metadata)>> GetUpdatedSinceAsync(
        string projectionPath,
        DateTimeOffset since)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionPath);

        await LoadIndexAsync(projectionPath).ConfigureAwait(false);

        return _cache
            .Where(kvp => kvp.Value.LastUpdatedAt >= since)
            .Select(kvp => (kvp.Key, kvp.Value))
            .ToList();
    }

    /// <summary>
    /// Deletes metadata for a projection.
    /// Thread-safe operation.
    /// </summary>
    /// <param name="projectionPath">Path to the projection folder</param>
    /// <param name="key">Projection key to remove</param>
    public async Task DeleteAsync(string projectionPath, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await _indexLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Remove from cache
            _cache.TryRemove(key, out _);

            // Persist to disk
            await PersistIndexAsync(projectionPath).ConfigureAwait(false);
        }
        finally
        {
            _indexLock.Release();
        }
    }

    /// <summary>
    /// Saves multiple metadata entries in a single atomic disk write.
    /// More efficient than calling <see cref="SaveAsync"/> per entry — used during rebuilds.
    /// </summary>
    /// <param name="projectionPath">Path to the projection folder</param>
    /// <param name="entries">All metadata entries to persist</param>
    public async Task BatchSaveAsync(string projectionPath, IReadOnlyDictionary<string, ProjectionMetadata> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionPath);
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
        {
            return;
        }

        await _indexLock.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var (key, metadata) in entries)
            {
                _cache[key] = metadata;
            }

            await PersistIndexAsync(projectionPath).ConfigureAwait(false);
        }
        finally
        {
            _indexLock.Release();
        }
    }

    /// <summary>
    /// Clears all metadata (used during projection rebuild).
    /// Thread-safe operation.
    /// </summary>
    /// <param name="projectionPath">Path to the projection folder</param>
    public async Task ClearAsync(string projectionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionPath);

        await _indexLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _cache.Clear();

            var indexFilePath = GetIndexFilePath(projectionPath);
            if (File.Exists(indexFilePath))
            {
                File.Delete(indexFilePath);
            }
        }
        finally
        {
            _indexLock.Release();
        }
    }

    /// <summary>
    /// Loads the metadata index from disk into cache.
    /// </summary>
    private async Task LoadIndexAsync(string projectionPath)
    {
        var indexFilePath = GetIndexFilePath(projectionPath);

        if (!File.Exists(indexFilePath))
        {
            return; // No index file yet
        }

        await _indexLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var json = await File.ReadAllTextAsync(indexFilePath).ConfigureAwait(false);
            var index = JsonSerializer.Deserialize<Dictionary<string, ProjectionMetadata>>(json, _jsonOptions);

            if (index != null)
            {
                foreach (var kvp in index)
                {
                    _cache[kvp.Key] = kvp.Value;
                }
            }
        }
        finally
        {
            _indexLock.Release();
        }
    }

    /// <summary>
    /// Persists the current cache to disk.
    /// Must be called within lock.
    /// </summary>
    private async Task PersistIndexAsync(string projectionPath)
    {
        var indexFilePath = GetIndexFilePath(projectionPath);
        var indexDirectory = Path.GetDirectoryName(indexFilePath)!;

        Directory.CreateDirectory(indexDirectory);

        var json = JsonSerializer.Serialize(_cache.ToDictionary(kvp => kvp.Key, kvp => kvp.Value), _jsonOptions);

        // Write atomically: temp file + rename prevents corrupt index on crash
        var tempPath = indexFilePath + $".tmp.{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
            File.Move(tempPath, indexFilePath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            { try { File.Delete(tempPath); } catch { /* ignore cleanup errors */ } }
            throw;
        }
    }

    /// <summary>
    /// Gets the path to the metadata index file.
    /// </summary>
    private static string GetIndexFilePath(string projectionPath)
    {
        return Path.Combine(projectionPath, "Metadata", "index.json");
    }
}
