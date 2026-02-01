using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opossum.Core;

namespace Opossum.Projections;

/// <summary>
/// Background service that continuously updates projections from the event store
/// </summary>
internal sealed class ProjectionDaemon : BackgroundService
{
    private readonly IProjectionManager _projectionManager;
    private readonly IEventStore _eventStore;
    private readonly ProjectionOptions _options;
    private readonly ILogger<ProjectionDaemon> _logger;

    public ProjectionDaemon(
        IProjectionManager projectionManager,
        IEventStore eventStore,
        ProjectionOptions options,
        ILogger<ProjectionDaemon> logger)
    {
        ArgumentNullException.ThrowIfNull(projectionManager);
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _projectionManager = projectionManager;
        _eventStore = eventStore;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Projection daemon starting...");

        // Wait a bit for application startup to complete
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        // Auto-rebuild projections if enabled and checkpoints are missing
        if (_options.EnableAutoRebuild)
        {
            await RebuildMissingProjectionsAsync(stoppingToken);
        }

        _logger.LogInformation("Projection daemon polling started with interval: {Interval}", _options.PollingInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNewEventsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing events in projection daemon");
            }

            await Task.Delay(_options.PollingInterval, stoppingToken);
        }

        _logger.LogInformation("Projection daemon stopped");
    }

    private async Task RebuildMissingProjectionsAsync(CancellationToken cancellationToken)
    {
        var projections = _projectionManager.GetRegisteredProjections();

        foreach (var projectionName in projections)
        {
            try
            {
                var checkpoint = await _projectionManager.GetCheckpointAsync(projectionName, cancellationToken);

                if (checkpoint == 0)
                {
                    _logger.LogInformation("Rebuilding projection '{ProjectionName}' (no checkpoint found)", projectionName);
                    await _projectionManager.RebuildAsync(projectionName, cancellationToken);
                    _logger.LogInformation("Projection '{ProjectionName}' rebuilt successfully", projectionName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rebuild projection '{ProjectionName}'", projectionName);
            }
        }
    }

    private async Task ProcessNewEventsAsync(CancellationToken cancellationToken)
    {
        var projections = _projectionManager.GetRegisteredProjections();

        if (projections.Count == 0)
        {
            return;
        }

        // Find the minimum checkpoint across all projections
        long minCheckpoint = long.MaxValue;

        foreach (var projectionName in projections)
        {
            var checkpoint = await _projectionManager.GetCheckpointAsync(projectionName, cancellationToken);
            minCheckpoint = Math.Min(minCheckpoint, checkpoint);
        }

        if (minCheckpoint == long.MaxValue)
        {
            minCheckpoint = 0;
        }

        // Read all events (we'll filter by position in memory for now)
        // TODO: Add position-based filtering to Query/QueryItem when available
        var query = Query.All();
        var allEvents = await _eventStore.ReadAsync(query, null);

        // Filter to events after the minimum checkpoint
        var newEvents = allEvents
            .Where(e => e.Position > minCheckpoint)
            .OrderBy(e => e.Position)
            .ToArray();

        if (newEvents.Length == 0)
        {
            return;
        }

        _logger.LogDebug("Processing {EventCount} new events from position {MinPosition}", 
            newEvents.Length, minCheckpoint + 1);

        // Process in batches
        var batches = newEvents.Chunk(_options.BatchSize);

        foreach (var batch in batches)
        {
            await _projectionManager.UpdateAsync(batch.ToArray(), cancellationToken);
        }

        _logger.LogDebug("Processed {EventCount} events successfully", newEvents.Length);
    }
}
