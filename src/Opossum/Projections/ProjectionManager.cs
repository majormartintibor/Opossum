using System.Text.Json;
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
    private readonly string _checkpointPath;
    private readonly Dictionary<string, ProjectionRegistration> _projections = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ProjectionManager(OpossumOptions options, IEventStore eventStore, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        _options = options;
        _eventStore = eventStore;
        _serviceProvider = serviceProvider;

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

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!_projections.TryGetValue(projectionName, out var registration))
            {
                throw new InvalidOperationException($"Projection '{projectionName}' is not registered");
            }

            // Read all events for this projection's event types
            var query = Query.FromEventTypes(registration.EventTypes);
            var events = await _eventStore.ReadAsync(query, null);

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
            var current = await _store.GetAsync(key, cancellationToken);

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
                    relatedEvents = await _eventStore.ReadAsync(relatedQuery, null);
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
                await _store.DeleteAsync(key, cancellationToken);
            }
            else
            {
                await _store.SaveAsync(key, updated, cancellationToken);
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
