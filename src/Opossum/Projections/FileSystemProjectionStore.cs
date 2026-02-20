using System.Text.Json;
using Opossum.Configuration;
using Opossum.Core;

namespace Opossum.Projections;

/// <summary>
/// File-based projection store implementation
/// </summary>
/// <typeparam name="TState">The projection state type</typeparam>
internal sealed class FileSystemProjectionStore<TState> : IProjectionStore<TState> where TState : class
{
    private readonly string _projectionPath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ProjectionTagIndex _tagIndex;
    private readonly ProjectionMetadataIndex _metadataIndex;
    private readonly IProjectionTagProvider<TState>? _tagProvider;
    private readonly Dictionary<string, List<Tag>> _projectionTags = new();

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false, // Minified for performance (40% smaller files, faster I/O)
        PropertyNameCaseInsensitive = true
    };

    public FileSystemProjectionStore(
        OpossumOptions options, 
        string projectionName,
        IProjectionTagProvider<TState>? tagProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionName);

        if (options.Contexts.Count == 0)
        {
            throw new InvalidOperationException("No contexts configured");
        }

        var contextPath = Path.Combine(options.RootPath, options.Contexts[0]);
        _projectionPath = Path.Combine(contextPath, "Projections", projectionName);
        _tagProvider = tagProvider;
        _tagIndex = new ProjectionTagIndex();
        _metadataIndex = new ProjectionMetadataIndex();

        Directory.CreateDirectory(_projectionPath);
    }

    public async Task<TState?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        // Ensure directory exists (might not exist for new projection types)
        if (!Directory.Exists(_projectionPath))
        {
            return null;
        }

        var filePath = GetFilePath(key);

        if (!File.Exists(filePath))
        {
            return null;
        }

        // No lock needed for reads - file system reads are inherently thread-safe
        // and we want to allow parallel reads for performance
        var json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);

        // Deserialize wrapper (all new projections have metadata)
        var wrapper = JsonSerializer.Deserialize<ProjectionWithMetadata<TState>>(json, _jsonOptions);

        if (wrapper == null)
        {
            return null;
        }

        return wrapper.Data;
    }

    public async Task<IReadOnlyList<TState>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        // Ensure directory exists (might not exist for new projection types during rebuild)
        if (!Directory.Exists(_projectionPath))
        {
            return Array.Empty<TState>();
        }

        var files = Directory.GetFiles(_projectionPath, "*.json");

        if (files.Length == 0)
        {
            return Array.Empty<TState>();
        }

        // For small sets, sequential read is more efficient
        if (files.Length < 10)
        {
            var results = new List<TState>(files.Length);
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var json = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                    var wrapper = JsonSerializer.Deserialize<ProjectionWithMetadata<TState>>(json, _jsonOptions);

                    if (wrapper?.Data != null)
                    {
                        results.Add(wrapper.Data);
                    }
                }
                catch (Exception)
                {
                    // Skip corrupted files
                    continue;
                }
            }
            return results;
        }

        // Parallel read for larger sets (Strategy 1: 4-6x speedup expected)
        var parallelResults = new TState?[files.Length];
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount * 2,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(
            files.Select((file, index) => (file, index)),
            options,
            async (item, ct) =>
            {
                try
                {
                    var json = await File.ReadAllTextAsync(item.file, ct).ConfigureAwait(false);
                    var wrapper = JsonSerializer.Deserialize<ProjectionWithMetadata<TState>>(json, _jsonOptions);
                    parallelResults[item.index] = wrapper?.Data;
                }
                catch (Exception)
                {
                    // Skip corrupted files
                    parallelResults[item.index] = null;
                }
            }).ConfigureAwait(false);

        // Filter out nulls and return
        return parallelResults.Where(r => r != null).ToList()!;
    }

    public async Task<IReadOnlyList<TState>> QueryAsync(Func<TState, bool> predicate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var all = await GetAllAsync(cancellationToken).ConfigureAwait(false);
        return all.Where(predicate).ToList();
    }

    public async Task<IReadOnlyList<TState>> QueryByTagAsync(Tag tag, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tag);

        if (_tagProvider == null)
        {
            // No tag provider configured - fall back to GetAllAsync with filter
            return Array.Empty<TState>();
        }

        var keys = await _tagIndex.GetProjectionKeysByTagAsync(_projectionPath, tag).ConfigureAwait(false);

        if (keys.Length == 0)
        {
            return Array.Empty<TState>();
        }

        // For small sets, sequential read is more efficient
        if (keys.Length < 10)
        {
            var results = new List<TState>(keys.Length);
            foreach (var key in keys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var state = await GetAsync(key, cancellationToken).ConfigureAwait(false);
                if (state != null)
                {
                    results.Add(state);
                }
            }
            return results;
        }

        // Parallel read for larger sets
        var parallelResults = new TState?[keys.Length];
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount * 2,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(
            keys.Select((key, index) => (key, index)),
            options,
            async (item, ct) =>
            {
                parallelResults[item.index] = await GetAsync(item.key, ct).ConfigureAwait(false);
            }).ConfigureAwait(false);

        return parallelResults.Where(r => r != null).ToList()!;
    }

    public async Task<IReadOnlyList<TState>> QueryByTagsAsync(IEnumerable<Tag> tags, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tags);

        var tagArray = tags.ToArray();
        if (tagArray.Length == 0)
        {
            return Array.Empty<TState>();
        }

        if (_tagProvider == null)
        {
            // No tag provider configured - return empty
            return Array.Empty<TState>();
        }

        if (tagArray.Length == 1)
        {
            return await QueryByTagAsync(tagArray[0], cancellationToken).ConfigureAwait(false);
        }

        // Multi-tag query (AND logic)
        var keys = await _tagIndex.GetProjectionKeysByTagsAsync(_projectionPath, tagArray).ConfigureAwait(false);

        if (keys.Length == 0)
        {
            return Array.Empty<TState>();
        }

        // For small sets, sequential read is more efficient
        if (keys.Length < 10)
        {
            var results = new List<TState>(keys.Length);
            foreach (var key in keys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var state = await GetAsync(key, cancellationToken).ConfigureAwait(false);
                if (state != null)
                {
                    results.Add(state);
                }
            }
            return results;
        }

        // Parallel read for larger sets
        var parallelResults = new TState?[keys.Length];
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount * 2,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(
            keys.Select((key, index) => (key, index)),
            options,
            async (item, ct) =>
            {
                parallelResults[item.index] = await GetAsync(item.key, ct).ConfigureAwait(false);
            }).ConfigureAwait(false);

        return parallelResults.Where(r => r != null).ToList()!;
    }

    public async Task SaveAsync(string key, TState state, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(state);

        // Ensure directory exists (create if first save after rebuild/initialization)
        Directory.CreateDirectory(_projectionPath);

        var filePath = GetFilePath(key);

        // Extract tags if provider is configured
        List<Tag>? newTags = null;
        if (_tagProvider != null)
        {
            try
            {
                newTags = _tagProvider.GetTags(state).ToList();
            }
            catch (Exception ex)
            {
                // Fail-fast: tag extraction failure prevents save
                throw new InvalidOperationException(
                    $"Failed to extract tags from projection state for key '{key}'. " +
                    $"Tag provider: {_tagProvider.GetType().Name}",
                    ex);
            }
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // If _projectionTags has no entry for this key (e.g. after an application restart)
            // but the projection file already exists on disk, reconstruct old tags from the
            // persisted state so UpdateProjectionTagsAsync can correctly remove stale index entries.
            // This MUST happen before File.WriteAllTextAsync overwrites the file.
            if (newTags != null && !_projectionTags.ContainsKey(key) && File.Exists(filePath))
            {
                try
                {
                    var existingJson = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
                    var existingWrapper = JsonSerializer.Deserialize<ProjectionWithMetadata<TState>>(existingJson, _jsonOptions);
                    if (existingWrapper?.Data != null)
                    {
                        _projectionTags[key] = _tagProvider!.GetTags(existingWrapper.Data).ToList();
                    }
                }
                catch
                {
                    // Recovery failed; old tag entries may linger until next rebuild
                }
            }

            // Get existing metadata or create new
            var existingMetadata = await _metadataIndex.GetAsync(_projectionPath, key).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;

            ProjectionMetadata metadata;
            if (existingMetadata != null)
            {
                // Update existing projection
                metadata = new ProjectionMetadata
                {
                    CreatedAt = existingMetadata.CreatedAt,
                    LastUpdatedAt = now,
                    Version = existingMetadata.Version + 1,
                    SizeInBytes = 0 // Will be updated after serialization
                };
            }
            else
            {
                // New projection
                metadata = new ProjectionMetadata
                {
                    CreatedAt = now,
                    LastUpdatedAt = now,
                    Version = 1,
                    SizeInBytes = 0 // Will be updated after serialization
                };
            }

            // Wrap state with metadata
            var wrapper = new ProjectionWithMetadata<TState>
            {
                Data = state,
                Metadata = metadata
            };

            // Serialize once and save
            var json = JsonSerializer.Serialize(wrapper, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);

            // Save metadata to index with actual file size (single serialization pass)
            await _metadataIndex.SaveAsync(_projectionPath, key, metadata with { SizeInBytes = json.Length }).ConfigureAwait(false);

            // Update tag indices if tags are configured
            if (newTags != null)
            {
                // Get old tags if projection existed before
                _projectionTags.TryGetValue(key, out var oldTags);

                if (oldTags != null)
                {
                    // Update indices: remove old tags, add new tags
                    await _tagIndex.UpdateProjectionTagsAsync(
                        _projectionPath,
                        key,
                        oldTags,
                        newTags).ConfigureAwait(false);
                }
                else
                {
                    // New projection: just add tags
                    foreach (var tag in newTags)
                    {
                        await _tagIndex.AddProjectionAsync(_projectionPath, tag, key).ConfigureAwait(false);
                    }
                }

                // Update in-memory tracking
                _projectionTags[key] = newTags;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        // If directory doesn't exist, nothing to delete
        if (!Directory.Exists(_projectionPath))
        {
            return;
        }

        var filePath = GetFilePath(key);

        if (!File.Exists(filePath))
        {
            return;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Delete projection file
            File.Delete(filePath);

            // Remove from metadata index
            await _metadataIndex.DeleteAsync(_projectionPath, key).ConfigureAwait(false);

            // Remove from tag indices if tags were tracked
            if (_projectionTags.TryGetValue(key, out var tags))
            {
                foreach (var tag in tags)
                {
                    await _tagIndex.RemoveProjectionAsync(_projectionPath, tag, key).ConfigureAwait(false);
                }
                _projectionTags.Remove(key);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Internal method to delete all indices for this projection.
    /// Used during rebuild to ensure clean state.
    /// </summary>
    internal async Task DeleteAllIndicesAsync()
    {
        _tagIndex.DeleteAllIndices(_projectionPath);
        await _metadataIndex.ClearAsync(_projectionPath).ConfigureAwait(false);
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            _projectionTags.Clear();
        }
        finally
        {
            _lock.Release();
        }
    }

    private string GetFilePath(string key)
    {
        // Sanitize key for file system
        var safeKey = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_projectionPath, $"{safeKey}.json");
    }
}
