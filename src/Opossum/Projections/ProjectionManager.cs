using Opossum.Configuration;
using Opossum.Core;

namespace Opossum.Projections;

/// <summary>
/// Manages projection lifecycle and updates
/// </summary>
internal sealed class ProjectionManager : IProjectionManager
{
    private readonly OpossumOptions _options;
    private readonly IEventStore _eventStore;
    private readonly IServiceProvider _serviceProvider;
    private readonly ProjectionOptions _projectionOptions;
    private readonly ILogger<ProjectionManager> _logger;
    private readonly string _checkpointPath;
    private readonly Dictionary<string, ProjectionRegistration> _projections = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

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

        if (options.Contexts.Count == 0)
        {
            throw new InvalidOperationException("No contexts configured");
        }

        var contextPath = Path.Combine(options.RootPath, options.Contexts[0]);
        _checkpointPath = Path.Combine(contextPath, "Projections", "_checkpoints");

        Directory.CreateDirectory(_checkpointPath);
    }

    public void RegisterProjection<TState>(IProjectionDefinition<TState> definition) where TState : class
    {
        ArgumentNullException.ThrowIfNull(definition);

        _lock.Wait();
        try
        {
            if (_projections.ContainsKey(definition.ProjectionName))
            {
                throw new InvalidOperationException($"Projection '{definition.ProjectionName}' is already registered");
            }

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

            _projections[definition.ProjectionName] = registration;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RebuildAsync(string projectionName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionName);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_projections.TryGetValue(projectionName, out var registration))
            {
                throw new InvalidOperationException($"Projection '{projectionName}' is not registered");
            }

            // Read all events for this projection's event types
            var query = Query.FromEventTypes(registration.EventTypes);
            var events = await _eventStore.ReadAsync(query, null).ConfigureAwait(false);

            // Clear existing projection data
            await registration.ClearAsync(cancellationToken).ConfigureAwait(false);

            // Rebuild from events
            foreach (var evt in events.OrderBy(e => e.Position))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await registration.ApplyAsync(evt, cancellationToken).ConfigureAwait(false);
            }

            // Save checkpoint
            if (events.Length > 0)
            {
                var lastPosition = events.Max(e => e.Position);
                await SaveCheckpointAsync(projectionName, lastPosition, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateAsync(SequencedEvent[] events, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (events.Length == 0)
        {
            return;
        }

        foreach (var (projectionName, registration) in _projections)
        {
            var relevantEvents = events
                .Where(e => registration.EventTypes.Contains(e.Event.EventType))
                .OrderBy(e => e.Position)
                .ToArray();

            if (relevantEvents.Length == 0)
            {
                continue;
            }

            foreach (var evt in relevantEvents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await registration.ApplyAsync(evt, cancellationToken).ConfigureAwait(false);
            }

            var lastPosition = relevantEvents.Max(e => e.Position);
            await SaveCheckpointAsync(projectionName, lastPosition, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<long> GetCheckpointAsync(string projectionName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionName);

        var filePath = GetCheckpointFilePath(projectionName);

        if (!File.Exists(filePath))
        {
            return 0;
        }

        var json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        var checkpoint = JsonSerializer.Deserialize<ProjectionCheckpoint>(json, _jsonOptions);

        return checkpoint?.LastProcessedPosition ?? 0;
    }

    public async Task SaveCheckpointAsync(string projectionName, long position, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionName);

        var currentCheckpoint = await GetCheckpointAsync(projectionName, cancellationToken).ConfigureAwait(false);

        var checkpoint = new ProjectionCheckpoint
        {
            ProjectionName = projectionName,
            LastProcessedPosition = position,
            LastUpdated = DateTimeOffset.UtcNow,
            TotalEventsProcessed = currentCheckpoint == 0 ? position : currentCheckpoint + 1
        };

        var filePath = GetCheckpointFilePath(projectionName);
        var json = JsonSerializer.Serialize(checkpoint, _jsonOptions);

        await File.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<string> GetRegisteredProjections()
    {
        return _projections.Keys.ToList();
    }

    public async Task<ProjectionRebuildResult> RebuildAllAsync(
        bool forceRebuild = false, 
        CancellationToken cancellationToken = default)
    {
        var projections = GetRegisteredProjections();
        var projectionsToRebuild = new List<string>();

        // Determine which projections need rebuilding
        foreach (var projectionName in projections)
        {
            var checkpoint = await GetCheckpointAsync(projectionName, cancellationToken).ConfigureAwait(false);

            if (forceRebuild || checkpoint == 0)
            {
                projectionsToRebuild.Add(projectionName);
            }
        }

        if (projectionsToRebuild.Count == 0)
        {
            _logger.LogInformation("All projections are up to date (no rebuilds needed)");

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

            _logger.LogInformation(
                "Starting parallel rebuild of {Count} projections with max {MaxConcurrency} concurrent rebuilds",
                projectionNames.Length,
                maxConcurrency);

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
                        _logger.LogInformation("Rebuilding projection '{ProjectionName}'...", projectionName);

                        // Call existing RebuildAsync(string) method
                        await RebuildAsync(projectionName, ct).ConfigureAwait(false);

                        stopwatch.Stop();

                        var eventsProcessed = await GetCheckpointAsync(projectionName, ct).ConfigureAwait(false);

                        details.Add(new ProjectionRebuildDetail
                        {
                            ProjectionName = projectionName,
                            Success = true,
                            Duration = stopwatch.Elapsed,
                            EventsProcessed = (int)eventsProcessed,
                            ErrorMessage = null
                        });

                        _logger.LogInformation(
                            "Projection '{ProjectionName}' rebuilt successfully in {Duration}ms ({EventCount} events)",
                            projectionName,
                            stopwatch.ElapsedMilliseconds,
                            eventsProcessed);
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

                        _logger.LogError(ex, 
                            "Failed to rebuild projection '{ProjectionName}'", 
                            projectionName);
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
                _logger.LogInformation(
                    "All {Count} projections rebuilt successfully in {Duration}",
                    result.TotalRebuilt,
                    overallStopwatch.Elapsed);
            }
            else
            {
                _logger.LogWarning(
                    "Projection rebuild completed with errors. Success: {Success}/{Total}. Failed: {Failed}",
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

    /// <summary>
    /// Internal wrapper for projection registration
    /// </summary>
    private abstract class ProjectionRegistration
    {
        public abstract string[] EventTypes { get; }
        public abstract Task ApplyAsync(SequencedEvent evt, CancellationToken cancellationToken);
        public abstract Task ClearAsync(CancellationToken cancellationToken);
    }

    private sealed class ProjectionRegistration<TState> : ProjectionRegistration where TState : class
    {
        private readonly IProjectionDefinition<TState> _definition;
        private readonly IProjectionStore<TState> _store;
        private readonly IEventStore? _eventStore;

        public ProjectionRegistration(
            IProjectionDefinition<TState> definition, 
            IProjectionStore<TState> store,
            IEventStore? eventStore = null)
        {
            _definition = definition;
            _store = store;
            _eventStore = eventStore;
        }

        public override string[] EventTypes => _definition.EventTypes;

        public override async Task ApplyAsync(SequencedEvent evt, CancellationToken cancellationToken)
        {
            var key = _definition.KeySelector(evt);
            var current = await _store.GetAsync(key, cancellationToken).ConfigureAwait(false);

            TState? updated;

            // Check if this is a multi-stream projection
            if (_definition is IMultiStreamProjectionDefinition<TState> multiStreamProjection)
            {
                // Get related events query
                var relatedQuery = multiStreamProjection.GetRelatedEventsQuery(evt.Event.Event);
                SequencedEvent[] relatedEvents = [];

                // Load related events if query is provided
                if (relatedQuery != null && _eventStore != null)
                {
                    relatedEvents = await _eventStore.ReadAsync(relatedQuery, null).ConfigureAwait(false);
                }

                // Apply with related events
                updated = multiStreamProjection.Apply(current, evt.Event.Event, relatedEvents);
            }
            else
            {
                // Regular projection - apply without related events
                updated = _definition.Apply(current, evt.Event.Event);
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
            // Cast to FileSystemProjectionStore to access DeleteAllIndicesAsync method
            if (_store is FileSystemProjectionStore<TState> fsStore)
            {
                // Delete indices first
                await fsStore.DeleteAllIndicesAsync().ConfigureAwait(false);

                // Then delete projection files
                var projectionPath = fsStore.GetType().GetField("_projectionPath", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.GetValue(fsStore) as string ?? "";

                if (Directory.Exists(projectionPath))
                {
                    foreach (var file in Directory.GetFiles(projectionPath, "*.json"))
                    {
                        File.Delete(file);
                    }
                }
            }
        }
    }
}
