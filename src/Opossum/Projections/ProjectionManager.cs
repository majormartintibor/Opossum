using Opossum.Configuration;
using Opossum.Core;
using Opossum.Telemetry;

namespace Opossum.Projections;

/// <summary>
/// Manages projection lifecycle and updates
/// </summary>
internal sealed partial class ProjectionManager : IProjectionManager
{
    private readonly OpossumOptions _options;
    private readonly IEventStore _eventStore;
    private readonly IServiceProvider _serviceProvider;
    private readonly ProjectionOptions _projectionOptions;
    private readonly ILogger<ProjectionManager> _logger;
    private readonly string _checkpointPath;

    // Thread-safe projection registry - allows concurrent reads and writes
    private readonly ConcurrentDictionary<string, ProjectionRegistration> _projections = new();

    // Per-projection locks to prevent concurrent rebuilds/updates of the same projection
    // NOTE: This dictionary grows with the number of unique projection names registered.
    // In practice, this is typically <100 entries (one per projection type).
    // Each SemaphoreSlim is ~48 bytes, so even 100 projections = ~5KB overhead.
    // If this becomes a concern in very large deployments, we can implement:
    // - Lock pooling strategy
    // - Weak reference cleanup for unused projections
    // - Configurable lock retention policy
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _projectionLocks = new();

    // In-memory cache for the last known checkpoint position per projection.
    // Eliminates the per-poll-tick file read in the hot path of UpdateAsync and
    // ProcessNewEventsAsync. Populated lazily on first GetCheckpointAsync; updated
    // atomically in SaveCheckpointAsync after every successful disk write.
    private readonly ConcurrentDictionary<string, long> _checkpointCache = new();

    // Lock for rebuild status tracking
    private readonly object _rebuildLock = new();
    private ProjectionRebuildStatus _currentRebuildStatus = new()
    {
        IsRebuilding = false,
        InProgressProjections = [],
        QueuedProjections = []
    };

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ProjectionManager(
        OpossumOptions options,
        IEventStore eventStore,
        IServiceProvider serviceProvider,
        ProjectionOptions projectionOptions,
        ILogger<ProjectionManager>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(projectionOptions);

        _options = options;
        _eventStore = eventStore;
        _serviceProvider = serviceProvider;
        _projectionOptions = projectionOptions;
        _logger = logger ?? NullLogger<ProjectionManager>.Instance;

        if (options.StoreName is null)
        {
            throw new InvalidOperationException("No store configured");
        }

        // Opossum is single-context by design (see ADR-004)
        var contextPath = Path.Combine(options.RootPath, options.StoreName);
        _checkpointPath = Path.Combine(contextPath, "Projections", "_checkpoints");

        Directory.CreateDirectory(_checkpointPath);
    }

    public void RegisterProjection<TState>(IProjectionDefinition<TState> definition) where TState : class
    {
        ArgumentNullException.ThrowIfNull(definition);

        // Try to resolve store from DI first (for auto-discovered projections with tag providers)
        var storeType = typeof(IProjectionStore<>).MakeGenericType(typeof(TState));
        var store = _serviceProvider.GetService(storeType) as IProjectionStore<TState>;

        // If not in DI, create manually (for manual registration in tests or without tag providers)
        if (store == null)
        {
            var fileSystemStoreType = typeof(FileSystemProjectionStore<>).MakeGenericType(typeof(TState));
            store = Activator.CreateInstance(fileSystemStoreType, _options, definition.ProjectionName, null) as IProjectionStore<TState>;
        }

        if (store == null)
        {
            throw new InvalidOperationException($"Could not create or resolve projection store for {typeof(TState).Name}");
        }

        var registration = new ProjectionRegistration<TState>(definition, store, _eventStore);

        // ConcurrentDictionary.TryAdd is thread-safe and atomic
        if (!_projections.TryAdd(definition.ProjectionName, registration))
        {
            throw new InvalidOperationException($"Projection '{definition.ProjectionName}' is already registered");
        }
    }

    public async Task RebuildAsync(string projectionName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionName);
        await RebuildProjectionCoreAsync(projectionName, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Core rebuild implementation that returns the number of projection-relevant events
    /// actually processed. Callers use this count for accurate <see cref="ProjectionRebuildDetail.EventsProcessed"/>
    /// reporting instead of the checkpoint position (which equals the last store position even
    /// when zero relevant events exist).
    /// </summary>
    private async Task<int> RebuildProjectionCoreAsync(string projectionName, CancellationToken cancellationToken)
    {
        using var activity = OpossumsActivity.Source.StartActivity(OpossumsActivity.ProjectionRebuild);
        activity?.SetTag("opossum.projection", projectionName);

        // Wait for any in-progress update to finish before rebuilding.
        // (UpdateAsync uses fail-fast; RebuildAsync must wait so the admin endpoint does not
        // immediately lose the race against the daemon's polling loop.)
        using (await AcquireProjectionLockAsync(projectionName, cancellationToken, failFast: false).ConfigureAwait(false))
        {
            // Thread-safe dictionary lookup (no global lock needed)
            if (!_projections.TryGetValue(projectionName, out var registration))
            {
                throw new InvalidOperationException($"Projection '{projectionName}' is not registered");
            }

            // Read all events for this projection's event types
            var query = Query.FromEventTypes([.. registration.EventTypes]);
            var events = await _eventStore.ReadAsync(query, null).ConfigureAwait(false);

            // Clear existing projection data
            await registration.ClearAsync(cancellationToken).ConfigureAwait(false);

            // Switch store to rebuild mode: state changes are buffered in memory and
            // flushed to disk once at the end, reducing disk I/O from O(events) to O(unique keys).
            await registration.BeginRebuildAsync().ConfigureAwait(false);

            // Rebuild from events (ReadAsync already returns events in ascending position order)
            foreach (var evt in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await registration.ApplyAsync(evt, cancellationToken).ConfigureAwait(false);
            }

            // Flush all buffered state to disk in a single pass
            await registration.CommitRebuildAsync(cancellationToken).ConfigureAwait(false);

            // Save checkpoint — always write the checkpoint file so the projection is not
            // treated as "never rebuilt" by RebuildAllAsync(forceRebuild: false) and the
            // daemon's startup auto-rebuild. When the store is completely empty the position
            // is 0; RebuildAllAsync uses File.Exists to distinguish "rebuilt but empty store"
            // (file present, position 0) from "truly never rebuilt" (file absent).
            activity?.SetTag("opossum.events_processed", events.Length);
            long checkpointPosition;
            if (events.Length > 0)
            {
                checkpointPosition = events.Max(e => e.Position);
            }
            else
            {
                var lastEvent = await _eventStore.ReadLastAsync(Query.All()).ConfigureAwait(false);
                checkpointPosition = lastEvent?.Position ?? 0;
            }
            await SaveCheckpointAsync(projectionName, checkpointPosition, cancellationToken).ConfigureAwait(false);

            return events.Length;
        }
        // Lock automatically released here
    }

    public async Task UpdateAsync(SequencedEvent[] events, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (events.Length == 0)
        {
            return;
        }

        // Compute once — the same frontier applies to every projection in this batch.
        var batchMax = events.Max(e => e.Position);

        foreach (var (projectionName, registration) in _projections)
        {
            // Acquire the per-projection lock for ALL paths (apply and checkpoint-advance alike).
            // This ensures the checkpoint is always read after the lock is held, so a concurrent
            // rebuild that completed between the outer check and lock acquisition cannot leave us
            // with a stale checkpoint value that causes already-processed events to be re-applied.
            try
            {
                using (await AcquireProjectionLockAsync(projectionName, cancellationToken).ConfigureAwait(false))
                {
                    // Read checkpoint inside the lock — always reflects the latest committed value.
                    var checkpoint = await GetCheckpointAsync(projectionName, cancellationToken).ConfigureAwait(false);

                    // Entire batch is already behind this projection's frontier — nothing to do.
                    if (batchMax <= checkpoint)
                        continue;

                    // Filter by both event type AND position.
                    // The position guard is critical: ProcessNewEventsAsync reads from the global
                    // minCheckpoint, so this batch may contain events that a faster projection has
                    // already processed. Without the guard those events would be re-applied, causing
                    // duplicate state mutations and checkpoint regressions.
                    var relevantEvents = events
                        .Where(e => registration.EventTypes.Contains(e.Event.EventType) && e.Position > checkpoint)
                        .ToArray();

                    if (relevantEvents.Length == 0)
                    {
                        // No new relevant events in this window; advance the frontier so this
                        // projection does not drag minCheckpoint down on future ticks.
                        await SaveCheckpointAsync(projectionName, batchMax, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    // ReadAsync already returns events in ascending position order.
                    foreach (var evt in relevantEvents)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await registration.ApplyAsync(evt, cancellationToken).ConfigureAwait(false);
                    }

                    // Always advance to batchMax, not just relevantEvents.Max().
                    // Sparse projections (few relevant event types) would otherwise sit below the
                    // batch head and force the daemon to re-read already-seen events every tick.
                    await SaveCheckpointAsync(projectionName, batchMax, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already being rebuilt or updated"))
            {
                // Projection is being rebuilt — skip this update batch.
                // The rebuild will process all events up to the store head anyway.
                LogSkippingProjectionUpdate(projectionName);
            }
            catch (Exception ex)
            {
                // Log and continue — one failing projection must not abort updates for all others.
                LogProjectionUpdateFailed(projectionName, ex);
            }
        }
    }

    public async Task<long> GetCheckpointAsync(string projectionName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionName);

        if (_checkpointCache.TryGetValue(projectionName, out var cached))
            return cached;

        // Cache miss — read from disk once and populate the cache.
        var filePath = GetCheckpointFilePath(projectionName);

        if (!File.Exists(filePath))
        {
            _checkpointCache[projectionName] = 0;
            return 0;
        }

        var json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        var checkpoint = JsonSerializer.Deserialize<ProjectionCheckpoint>(json, _jsonOptions);
        var position = checkpoint?.LastProcessedPosition ?? 0;
        _checkpointCache[projectionName] = position;
        return position;
    }

    public async Task SaveCheckpointAsync(string projectionName, long position, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionName);

        var checkpoint = new ProjectionCheckpoint
        {
            ProjectionName = projectionName,
            LastProcessedPosition = position,
            LastUpdated = DateTimeOffset.UtcNow,
            TotalEventsProcessed = position
        };

        var filePath = GetCheckpointFilePath(projectionName);
        var json = JsonSerializer.Serialize(checkpoint, _jsonOptions);

        // Write atomically: temp file + rename prevents corrupt checkpoints on crash
        var tempPath = filePath + $".tmp.{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            { try { File.Delete(tempPath); } catch { /* ignore cleanup errors */ } }
            throw;
        }

        // Update cache after successful disk write so subsequent GetCheckpointAsync
        // calls return the new position without a disk round-trip.
        _checkpointCache[projectionName] = position;
    }

    public IReadOnlyList<string> GetRegisteredProjections()
    {
        return [.._projections.Keys];
    }

    public async Task<ProjectionRebuildResult> RebuildAllAsync(
        bool forceRebuild = false,
        CancellationToken cancellationToken = default)
    {
        var projections = GetRegisteredProjections();
        var projectionsToRebuild = new List<string>();

        // Determine which projections need rebuilding.
        // Use File.Exists instead of checkpoint == 0: a checkpoint file written with
        // position 0 means the projection was rebuilt against an empty store and should
        // NOT be rebuilt again. A missing file means it has truly never been rebuilt.
        foreach (var projectionName in projections)
        {
            var checkpointFile = GetCheckpointFilePath(projectionName);
            if (forceRebuild || !File.Exists(checkpointFile))
            {
                projectionsToRebuild.Add(projectionName);
            }
        }

        if (projectionsToRebuild.Count == 0)
        {
            LogAllProjectionsUpToDate();

            return new ProjectionRebuildResult
            {
                TotalRebuilt = 0,
                Duration = TimeSpan.Zero,
                Details = []
            };
        }

        // Delegate to RebuildAsync(string[])
        return await RebuildAsync([.. projectionsToRebuild], cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProjectionRebuildResult> RebuildAsync(
        string[] projectionNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectionNames);

        if (projectionNames.Length == 0)
        {
            return new ProjectionRebuildResult
            {
                TotalRebuilt = 0,
                Duration = TimeSpan.Zero,
                Details = []
            };
        }

        // Update rebuild status
        UpdateRebuildStatus(isRebuilding: true,
            inProgress: [],
            queued: [.. projectionNames]);

        var overallStopwatch = Stopwatch.StartNew();
        var details = new ConcurrentBag<ProjectionRebuildDetail>();

        try
        {
            var maxConcurrency = _projectionOptions.MaxConcurrentRebuilds;

            LogStartingParallelRebuild(projectionNames.Length, maxConcurrency);

            // Rebuild projections in parallel
            await Parallel.ForEachAsync(
                projectionNames,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxConcurrency,
                    CancellationToken = cancellationToken
                },
                async (projectionName, ct) =>
                {
                    // Update status: move from queued to in-progress
                    MoveToInProgress(projectionName);

                    var stopwatch = Stopwatch.StartNew();

                    try
                    {
                        LogRebuildingProjection(projectionName);

                        // Use core method to get actual events-processed count for accurate reporting.
                        // (GetCheckpointAsync returns the last store position, not the number of
                        // projection-relevant events, which would mislead users for sparse projections.)
                        var eventsProcessed = await RebuildProjectionCoreAsync(projectionName, ct).ConfigureAwait(false);

                        stopwatch.Stop();

                        details.Add(new ProjectionRebuildDetail
                        {
                            ProjectionName = projectionName,
                            Success = true,
                            Duration = stopwatch.Elapsed,
                            EventsProcessed = eventsProcessed,
                            ErrorMessage = null
                        });

                        LogProjectionRebuiltSuccessfully(projectionName, stopwatch.ElapsedMilliseconds, eventsProcessed);
                    }
                    catch (Exception ex)
                    {
                        stopwatch.Stop();

                        details.Add(new ProjectionRebuildDetail
                        {
                            ProjectionName = projectionName,
                            Success = false,
                            Duration = stopwatch.Elapsed,
                            EventsProcessed = 0,
                            ErrorMessage = ex.Message
                        });

                        LogProjectionRebuildFailed(ex, projectionName);
                    }
                    finally
                    {
                        // Update status: remove from in-progress
                        RemoveFromInProgress(projectionName);
                    }
                }).ConfigureAwait(false);

            overallStopwatch.Stop();

            var result = new ProjectionRebuildResult
            {
                TotalRebuilt = details.Count(d => d.Success),
                Duration = overallStopwatch.Elapsed,
                Details = [.. details.OrderBy(d => d.ProjectionName)]
            };

            if (result.Success)
            {
                LogAllProjectionsRebuiltSuccessfully(result.TotalRebuilt, overallStopwatch.Elapsed);
            }
            else
            {
                LogRebuildWithErrors(
                    result.TotalRebuilt,
                    projectionNames.Length,
                    string.Join(", ", result.FailedProjections));
            }

            return result;
        }
        finally
        {
            // Clear rebuild status
            UpdateRebuildStatus(isRebuilding: false, inProgress: [], queued: []);
        }
    }

    public Task<ProjectionRebuildStatus> GetRebuildStatusAsync()
    {
        lock (_rebuildLock)
        {
            return Task.FromResult(_currentRebuildStatus);
        }
    }

    private void UpdateRebuildStatus(bool isRebuilding, List<string> inProgress, List<string> queued)
    {
        lock (_rebuildLock)
        {
            _currentRebuildStatus = new ProjectionRebuildStatus
            {
                IsRebuilding = isRebuilding,
                InProgressProjections = inProgress,
                QueuedProjections = queued,
                StartedAt = isRebuilding ? DateTimeOffset.UtcNow : null,
                EstimatedCompletionAt = null
            };
        }
    }

    private void MoveToInProgress(string projectionName)
    {
        lock (_rebuildLock)
        {
            var queued = _currentRebuildStatus.QueuedProjections.ToList();
            queued.Remove(projectionName);

            var inProgress = _currentRebuildStatus.InProgressProjections.ToList();
            inProgress.Add(projectionName);

            _currentRebuildStatus = _currentRebuildStatus with
            {
                QueuedProjections = queued,
                InProgressProjections = inProgress
            };
        }
    }

    private void RemoveFromInProgress(string projectionName)
    {
        lock (_rebuildLock)
        {
            var inProgress = _currentRebuildStatus.InProgressProjections.ToList();
            inProgress.Remove(projectionName);

            _currentRebuildStatus = _currentRebuildStatus with
            {
                InProgressProjections = inProgress
            };
        }
    }

    private string GetCheckpointFilePath(string projectionName)
    {
        return Path.Combine(_checkpointPath, $"{projectionName}.checkpoint");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "All projections are up to date (no rebuilds needed)")]
    private partial void LogAllProjectionsUpToDate();

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting parallel rebuild of {Count} projections with max {MaxConcurrency} concurrent rebuilds")]
    private partial void LogStartingParallelRebuild(int count, int maxConcurrency);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Rebuilding projection '{ProjectionName}'...")]
    private partial void LogRebuildingProjection(string projectionName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Projection '{ProjectionName}' rebuilt successfully in {ElapsedMs}ms ({EventCount} events)")]
    private partial void LogProjectionRebuiltSuccessfully(string projectionName, long elapsedMs, long eventCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to rebuild projection '{ProjectionName}'")]
    private partial void LogProjectionRebuildFailed(Exception ex, string projectionName);

    [LoggerMessage(Level = LogLevel.Information, Message = "All {Count} projections rebuilt successfully in {Duration}")]
    private partial void LogAllProjectionsRebuiltSuccessfully(int count, TimeSpan duration);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Projection rebuild completed with errors. Success: {SuccessCount}/{TotalCount}. Failed: {FailedProjections}")]
    private partial void LogRebuildWithErrors(int successCount, int totalCount, string failedProjections);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping update for projection '{ProjectionName}' - currently being rebuilt")]
    private partial void LogSkippingProjectionUpdate(string projectionName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to update projection '{ProjectionName}', skipping this batch")]
    private partial void LogProjectionUpdateFailed(string projectionName, Exception ex);

    /// <summary>
    /// Acquires a per-projection lock to prevent concurrent operations on the same projection.
    /// </summary>
    /// <param name="projectionName">Name of the projection to lock</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="failFast">
    /// When <see langword="true"/> (the default, used by <see cref="UpdateAsync"/>), the method
    /// returns immediately with an exception if the lock is already held — daemon updates yield
    /// to in-progress rebuilds.
    /// When <see langword="false"/> (used by <see cref="RebuildAsync(string,CancellationToken)"/>),
    /// the method waits until the lock is available so the admin-triggered rebuild is not
    /// immediately lost against the daemon's polling loop.
    /// </param>
    /// <returns>Disposable that releases the lock when disposed</returns>
    /// <exception cref="InvalidOperationException">If <paramref name="failFast"/> is <see langword="true"/> and the projection is already locked</exception>
    private async Task<IDisposable> AcquireProjectionLockAsync(
        string projectionName,
        CancellationToken cancellationToken,
        bool failFast = true)
    {
        // Get or create a lock for this specific projection
        var projectionLock = _projectionLocks.GetOrAdd(
            projectionName,
            _ => new SemaphoreSlim(1, 1));

        if (failFast)
        {
            // Fail-fast: Don't wait if already locked (daemon updates yield to in-progress rebuilds)
            if (!await projectionLock.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    $"Projection '{projectionName}' is already being rebuilt or updated. " +
                    $"Please wait for the current operation to complete.");
            }
        }
        else
        {
            // Wait for the lock: admin rebuild waits for any in-progress daemon update to finish
            await projectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return new ProjectionLockReleaser(projectionLock);
    }

    /// <summary>
    /// RAII-style lock releaser for projection locks.
    /// Automatically releases the semaphore when disposed.
    /// </summary>
    private sealed class ProjectionLockReleaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public ProjectionLockReleaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _semaphore.Release();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Internal wrapper for projection registration
    /// </summary>
    private abstract class ProjectionRegistration
    {
        public abstract IReadOnlySet<string> EventTypes { get; }
        public abstract Task ApplyAsync(SequencedEvent evt, CancellationToken cancellationToken);
        public abstract Task ClearAsync(CancellationToken cancellationToken);
        public abstract Task BeginRebuildAsync();
        public abstract Task CommitRebuildAsync(CancellationToken cancellationToken);
    }

    private sealed class ProjectionRegistration<TState> : ProjectionRegistration where TState : class
    {
        private readonly IProjectionDefinition<TState> _definition;
        private readonly IProjectionStore<TState> _store;
        private readonly IEventStore? _eventStore;
        private readonly IReadOnlySet<string> _eventTypes;

        public ProjectionRegistration(
            IProjectionDefinition<TState> definition,
            IProjectionStore<TState> store,
            IEventStore? eventStore = null)
        {
            _definition = definition;
            _store = store;
            _eventStore = eventStore;
            _eventTypes = new HashSet<string>(definition.EventTypes, StringComparer.Ordinal);
        }

        public override IReadOnlySet<string> EventTypes => _eventTypes;

        public override async Task ApplyAsync(SequencedEvent evt, CancellationToken cancellationToken)
        {
            var key = _definition.KeySelector(evt);
            var current = await _store.GetAsync(key, cancellationToken).ConfigureAwait(false);

            TState? updated;

            // Check if this projection needs related events
            if (_definition is IProjectionWithRelatedEvents<TState> multiStreamProjection)
            {
                // Get related events query — full SequencedEvent available for key/tag/metadata access
                var relatedQuery = multiStreamProjection.GetRelatedEventsQuery(evt);
                SequencedEvent[] relatedEvents = [];

                // Load related events if query is provided
                if (relatedQuery != null && _eventStore != null)
                {
                    relatedEvents = await _eventStore.ReadAsync(relatedQuery, null).ConfigureAwait(false);
                }

                // Apply with related events — full SequencedEvent passed directly
                updated = multiStreamProjection.Apply(current, evt, relatedEvents);
            }
            else
            {
                // Regular projection — full SequencedEvent passed directly
                updated = _definition.Apply(current, evt);
            }

            if (updated == null)
            {
                // Null means delete
                await _store.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _store.SaveAsync(key, updated, cancellationToken).ConfigureAwait(false);
            }
        }

        public override async Task ClearAsync(CancellationToken cancellationToken)
        {
            // Delete all projection files and indices
            // Cast to FileSystemProjectionStore to access internal clearing methods
            if (_store is FileSystemProjectionStore<TState> fsStore)
            {
                // Delete indices first
                await fsStore.DeleteAllIndicesAsync().ConfigureAwait(false);

                // Delete projection files, handling read-only protection transparently
                fsStore.ClearProjectionFiles();
            }
        }

        public override Task BeginRebuildAsync()
        {
            if (_store is FileSystemProjectionStore<TState> fsStore)
            {
                fsStore.BeginRebuild();
            }
            return Task.CompletedTask;
        }

        public override async Task CommitRebuildAsync(CancellationToken cancellationToken)
        {
            if (_store is FileSystemProjectionStore<TState> fsStore)
            {
                await fsStore.CommitRebuildAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
