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
        WriteIndented = true,
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

        var filePath = GetFilePath(key);

        if (!File.Exists(filePath))
        {
            return null;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);

            // Deserialize wrapper (all new projections have metadata)
            var wrapper = JsonSerializer.Deserialize<ProjectionWithMetadata<TState>>(json, _jsonOptions);

            if (wrapper == null)
            {
                return null;
            }

            return wrapper.Data;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<TState>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var files = Directory.GetFiles(_projectionPath, "*.json");
        var results = new List<TState>(files.Length);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var json = await File.ReadAllTextAsync(file, cancellationToken);
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

    public async Task<IReadOnlyList<TState>> QueryAsync(Func<TState, bool> predicate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var all = await GetAllAsync(cancellationToken);
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

        var keys = await _tagIndex.GetProjectionKeysByTagAsync(_projectionPath, tag);
        var results = new List<TState>(keys.Length);

        foreach (var key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = await GetAsync(key, cancellationToken);
            if (state != null)
            {
                results.Add(state);
            }
        }

        return results;
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
            return await QueryByTagAsync(tagArray[0], cancellationToken);
        }

        // Multi-tag query (AND logic)
        var keys = await _tagIndex.GetProjectionKeysByTagsAsync(_projectionPath, tagArray);
        var results = new List<TState>(keys.Length);

        foreach (var key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = await GetAsync(key, cancellationToken);
            if (state != null)
            {
                results.Add(state);
            }
        }

        return results;
    }

    public async Task SaveAsync(string key, TState state, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(state);

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

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Get existing metadata or create new
            var existingMetadata = await _metadataIndex.GetAsync(_projectionPath, key);
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

            // Serialize and save
            var json = JsonSerializer.Serialize(wrapper, _jsonOptions);

            // Update metadata with actual size
            metadata = metadata with { SizeInBytes = json.Length };
            wrapper = wrapper with { Metadata = metadata };

            // Re-serialize with updated size
            json = JsonSerializer.Serialize(wrapper, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json, cancellationToken);

            // Save metadata to index
            await _metadataIndex.SaveAsync(_projectionPath, key, metadata);

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
                        newTags);
                }
                else
                {
                    // New projection: just add tags
                    foreach (var tag in newTags)
                    {
                        await _tagIndex.AddProjectionAsync(_projectionPath, tag, key);
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

        var filePath = GetFilePath(key);

        if (!File.Exists(filePath))
        {
            return;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Delete projection file
            File.Delete(filePath);

            // Remove from metadata index
            await _metadataIndex.DeleteAsync(_projectionPath, key);

            // Remove from tag indices if tags were tracked
            if (_projectionTags.TryGetValue(key, out var tags))
            {
                foreach (var tag in tags)
                {
                    await _tagIndex.RemoveProjectionAsync(_projectionPath, tag, key);
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
    internal void DeleteAllIndices()
    {
        _tagIndex.DeleteAllIndices(_projectionPath);
        _metadataIndex.ClearAsync(_projectionPath).GetAwaiter().GetResult();
        _projectionTags.Clear();
    }

    private string GetFilePath(string key)
    {
        // Sanitize key for file system
        var safeKey = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_projectionPath, $"{safeKey}.json");
    }
}
