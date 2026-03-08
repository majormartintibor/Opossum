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
    private readonly Dictionary<string, List<Tag>> _projectionTags = [];
    private readonly bool _writeProtect;

    // Rebuild mode: state changes are written directly to the temp directory (write-through).
    // Guarded by the per-projection lock held in ProjectionRebuilder.RebuildCoreAsync — not a concurrent field.
    private bool _isInRebuild;

    // During rebuild, accumulates tag-to-key mappings in memory. Written to disk at commit.
    // Initialised in BeginRebuild; nulled in CommitRebuildAsync.

    // Path to the temporary directory used during rebuild.
    // New projection files are written here and atomically moved to _projectionPath on commit,
    // so old data remains accessible to readers for the entire duration of the rebuild.

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

        if (options.StoreName is null)
        {
            throw new InvalidOperationException("No store configured");
        }

        var contextPath = Path.Combine(options.RootPath, options.StoreName);
        _projectionPath = Path.Combine(contextPath, "Projections", projectionName);
        _tagProvider = tagProvider;
        _tagIndex = new ProjectionTagIndex();
        _metadataIndex = new ProjectionMetadataIndex();
        _writeProtect = options.WriteProtectProjectionFiles;

        Directory.CreateDirectory(_projectionPath);
    }

    public async Task<TState?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        // During rebuild, read from the temp directory (write-through).
        // The temp directory is the authoritative state; if the key has not been replayed yet, null is correct.
        if (_isInRebuild)
        {
            var tempFilePath = GetFilePath(key); // resolves to _rebuildTempPath
            if (!File.Exists(tempFilePath)) return null;
            var rebuildJson = await File.ReadAllTextAsync(tempFilePath, cancellationToken).ConfigureAwait(false);
            var rebuildWrapper = JsonSerializer.Deserialize<ProjectionWithMetadata<TState>>(rebuildJson, _jsonOptions);
            return rebuildWrapper?.Data;
        }

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
            return [];
        }

        var files = Directory.GetFiles(_projectionPath, "*.json");

        if (files.Length == 0)
        {
            return [];
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
        return [..all.Where(predicate)];
    }

    public async Task<IReadOnlyList<TState>> QueryByTagAsync(Tag tag, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tag);

        if (_tagProvider == null)
        {
            // No tag provider configured - fall back to GetAllAsync with filter
            return [];
        }

        var keys = await _tagIndex.GetProjectionKeysByTagAsync(_projectionPath, tag).ConfigureAwait(false);

        if (keys.Length == 0)
        {
            return [];
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
            return [];
        }

        if (_tagProvider == null)
        {
            // No tag provider configured - return empty
            return [];
        }

        if (tagArray.Length == 1)
        {
            return await QueryByTagAsync(tagArray[0], cancellationToken).ConfigureAwait(false);
        }

        // Multi-tag query (AND logic)
        var keys = await _tagIndex.GetProjectionKeysByTagsAsync(_projectionPath, tagArray).ConfigureAwait(false);

        if (keys.Length == 0)
        {
            return [];
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

        // During rebuild, write state directly to the temp directory (write-through).
        // Memory stays bounded: each event writes immediately instead of accumulating all states.
        if (_isInRebuild)
        {
            var rebuildFilePath = GetFilePath(key);
            var now = DateTimeOffset.UtcNow;

            var metadata = new ProjectionMetadata
            {
                CreatedAt = now,
                LastUpdatedAt = now,
                Version = 1,
                SizeInBytes = 0
            };

            var wrapper = new ProjectionWithMetadata<TState>
            {
                Data = state,
                Metadata = metadata
            };

            var json = JsonSerializer.Serialize(wrapper, _jsonOptions);

            if (_writeProtect && File.Exists(rebuildFilePath))
            {
                var existing = File.GetAttributes(rebuildFilePath);
                if ((existing & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(rebuildFilePath, existing & ~FileAttributes.ReadOnly);
            }

            await File.WriteAllTextAsync(rebuildFilePath, json, cancellationToken).ConfigureAwait(false);

            if (_writeProtect)
                File.SetAttributes(rebuildFilePath, FileAttributes.ReadOnly);

            // Accumulate tags for bulk write at commit
            if (_tagProvider is not null && TagAccumulator is not null)
            {
                foreach (var tag in _tagProvider.GetTags(state))
                {
                    var tagKey = GetTagAccumulatorKey(tag);
                    if (!TagAccumulator.TryGetValue(tagKey, out var keys))
                    {
                        keys = [];
                        TagAccumulator[tagKey] = keys;
                    }
                    keys.Add(key);
                }
            }

            return;
        }

        // Ensure directory exists (create if first save after rebuild/initialization)
        Directory.CreateDirectory(_projectionPath);

        var filePath = GetFilePath(key);

        // Extract tags if provider is configured
        List<Tag>? newTags = null;
        if (_tagProvider != null)
        {
            try
            {
                newTags = [.. _tagProvider.GetTags(state)];
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
                        _projectionTags[key] = [.. _tagProvider!.GetTags(existingWrapper.Data)];
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

            // If write-protected, remove the read-only attribute before overwriting
            if (_writeProtect && File.Exists(filePath))
            {
                var existing = File.GetAttributes(filePath);
                if ((existing & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(filePath, existing & ~FileAttributes.ReadOnly);
            }

            await File.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);

            // Mark the file read-only so humans cannot accidentally modify or delete it
            if (_writeProtect)
                File.SetAttributes(filePath, FileAttributes.ReadOnly);

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

        // During rebuild, delete from the temp directory and remove from tag accumulator.
        if (_isInRebuild)
        {
            var tempFilePath = GetFilePath(key); // resolves to _rebuildTempPath
            if (File.Exists(tempFilePath))
            {
                if (_writeProtect)
                {
                    var attrs = File.GetAttributes(tempFilePath);
                    if ((attrs & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(tempFilePath, attrs & ~FileAttributes.ReadOnly);
                }
                File.Delete(tempFilePath);
            }

            // Remove key from every tag set in the accumulator
            if (TagAccumulator is not null)
            {
                foreach (var keySet in TagAccumulator.Values)
                    keySet.Remove(key);
            }

            return;
        }

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
            // Remove read-only attribute before deleting if write protection is enabled
            if (_writeProtect)
            {
                var attrs = File.GetAttributes(filePath);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(filePath, attrs & ~FileAttributes.ReadOnly);
            }

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
    /// Switches the store into rebuild mode with write-through semantics.
    /// All subsequent <see cref="SaveAsync"/> calls write directly to a temporary directory,
    /// and <see cref="GetAsync"/> reads from it. <see cref="DeleteAsync"/> removes files from
    /// the temp directory.
    /// Creates a temporary directory to hold the new projection files; the existing files in
    /// <c>_projectionPath</c> remain accessible to readers until <see cref="CommitRebuildAsync"/>
    /// performs the atomic swap.
    /// Must be followed by <see cref="CommitRebuildAsync"/> to finalise the rebuild.
    /// </summary>
    internal void BeginRebuild()
    {
        _projectionTags.Clear();
        _isInRebuild = true;
        TagAccumulator = [];
        RebuildTempPath = _projectionPath + $".tmp.{Guid.NewGuid():N}";
        Directory.CreateDirectory(RebuildTempPath);
    }

    /// <summary>
    /// Switches the store into rebuild mode using an explicit temporary directory path
    /// with write-through semantics (see <see cref="BeginRebuild()"/> for details).
    /// This overload is used during crash recovery to resume a rebuild into the same temp
    /// directory that was in use before the interruption, ensuring that previously written
    /// projection files are preserved.
    /// The directory at <paramref name="tempPath"/> is created if it does not already exist.
    /// Must be followed by <see cref="CommitRebuildAsync"/> to finalise the rebuild.
    /// </summary>
    /// <param name="tempPath">Absolute path to the temporary directory to use for this rebuild.</param>
    internal void BeginRebuild(string tempPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tempPath);

        _projectionTags.Clear();
        _isInRebuild = true;
        TagAccumulator = [];
        RebuildTempPath = tempPath;
        Directory.CreateDirectory(RebuildTempPath);
    }

    /// <summary>
    /// Returns the current rebuild temp directory path, or <c>null</c> if not in rebuild mode.
    /// Used by <see cref="ProjectionRebuilder"/> to persist the journal with the correct temp path.
    /// </summary>
    internal string? RebuildTempPath { get; private set; }

    /// <summary>
    /// Returns the current in-memory tag accumulator, or <c>null</c> if not in rebuild mode.
    /// Used by <see cref="ProjectionRebuilder"/> to periodically flush the tag accumulator
    /// alongside the rebuild journal for crash recovery.
    /// </summary>
    internal Dictionary<string, HashSet<string>>? TagAccumulator { get; private set; }

    /// <summary>
    /// Replaces the current (empty) tag accumulator with a previously persisted snapshot.
    /// Must be called after <see cref="BeginRebuild(string)"/> and before the event replay
    /// loop starts, so that tags for events processed before the crash are preserved.
    /// </summary>
    /// <param name="tagAccumulator">The tag accumulator loaded from the companion file.</param>
    internal void RestoreTagAccumulator(Dictionary<string, HashSet<string>> tagAccumulator)
    {
        ArgumentNullException.ThrowIfNull(tagAccumulator);

        if (!_isInRebuild)
            throw new InvalidOperationException("Cannot restore tag accumulator outside of rebuild mode");

        TagAccumulator = tagAccumulator;
    }

    /// <summary>
    /// Writes tag index files from the in-memory <see cref="TagAccumulator"/> to the temp directory,
    /// then atomically swaps the temp directory into the production path.
    /// Projection state files have already been written during event replay (write-through),
    /// so no state buffer flush is required.
    /// </summary>
    internal async Task CommitRebuildAsync(CancellationToken cancellationToken = default)
    {
        var tempPath = RebuildTempPath;
        try
        {
            // Write tag index files from the accumulator in parallel
            if (TagAccumulator is { Count: > 0 } && tempPath is not null)
            {
                var indicesPath = Path.Combine(tempPath, "Indices");
                Directory.CreateDirectory(indicesPath);

                await Parallel.ForEachAsync(
                    TagAccumulator,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Environment.ProcessorCount,
                        CancellationToken = cancellationToken
                    },
                    async (entry, ct) =>
                    {
                        var indexFilePath = Path.Combine(indicesPath, entry.Key);
                        var json = JsonSerializer.Serialize(entry.Value, _jsonOptions);

                        // Atomic write: temp file + rename
                        var tempFile = $"{indexFilePath}.tmp.{Guid.NewGuid():N}";
                        try
                        {
                            await File.WriteAllTextAsync(tempFile, json, ct).ConfigureAwait(false);
                            File.Move(tempFile, indexFilePath, overwrite: true);
                        }
                        catch
                        {
                            if (File.Exists(tempFile))
                            { try { File.Delete(tempFile); } catch { /* ignore cleanup errors */ } }
                            throw;
                        }
                    }).ConfigureAwait(false);
            }

            // Atomic swap: replace the production directory with the temp directory.
            // Old projection files remain on disk until this point, keeping them accessible
            // to readers for the entire duration of the rebuild.
            DeleteDirectory(_projectionPath);
            if (tempPath is not null && Directory.Exists(tempPath))
                Directory.Move(tempPath, _projectionPath);
            else
                Directory.CreateDirectory(_projectionPath);
        }
        catch
        {
            // Clean up temp directory on failure so no orphaned directories are left behind.
            if (tempPath is not null)
                DeleteDirectory(tempPath);
            throw;
        }
        finally
        {
            _isInRebuild = false;
            RebuildTempPath = null;
            TagAccumulator = null;

            // Discard stale metadata cache — the aggregated Metadata/index.json was in the
            // old directory and does not exist in the rebuilt directory.  Without this,
            // subsequent SaveAsync calls would read stale version/timestamp data from cache.
            _metadataIndex.ClearCache();
        }
    }

    private string GetFilePath(string key)
    {
        // Sanitize key for file system
        var safeKey = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));
        var dir = RebuildTempPath ?? _projectionPath;
        return Path.Combine(dir, $"{safeKey}.json");
    }

    /// <summary>
    /// Produces the tag index file name (without directory) using the same sanitisation
    /// logic as <see cref="ProjectionTagIndex"/> so the files written at commit are
    /// compatible with the read path.
    /// </summary>
    private static string GetTagAccumulatorKey(Tag tag)
    {
        var safeKey = string.Join("_", tag.Key.ToLowerInvariant().Split(Path.GetInvalidFileNameChars()));
        var safeValue = string.Join("_", tag.Value.ToLowerInvariant().Split(Path.GetInvalidFileNameChars()));
        return $"{safeKey}_{safeValue}.json";
    }

    /// <summary>
    /// Deletes a directory and all its contents, removing read-only attributes first when
    /// write protection is enabled. Safe to call when the directory does not exist.
    /// </summary>
    private void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;

        if (_writeProtect)
        {
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                var attrs = File.GetAttributes(file);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
            }
        }

        Directory.Delete(path, recursive: true);
    }
}
