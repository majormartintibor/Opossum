using Opossum.Core;

namespace Opossum.Projections;

/// <summary>
/// Background service that continuously updates projections from the event store
/// </summary>
internal sealed partial class ProjectionDaemon : BackgroundService
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
        LogDaemonStarting();

        // Wait a bit for application startup to complete
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);

        // Auto-rebuild projections if enabled and checkpoints are missing
        if (_options.EnableAutoRebuild)
        {
            await RebuildMissingProjectionsAsync(stoppingToken).ConfigureAwait(false);
        }

        LogPollingStarted(_options.PollingInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNewEventsAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                LogProcessingError(ex);
            }

            await Task.Delay(_options.PollingInterval, stoppingToken).ConfigureAwait(false);
        }

        LogDaemonStopped();
    }

    private async Task RebuildMissingProjectionsAsync(CancellationToken cancellationToken)
    {
        LogCheckingForRebuilds();

        // Use the new RebuildAllAsync method (only rebuilds missing projections)
        var result = await _projectionManager.RebuildAllAsync(
            forceRebuild: false,
            cancellationToken).ConfigureAwait(false);

        if (result.TotalRebuilt == 0)
        {
            LogAllProjectionsUpToDate();
            return;
        }

        if (result.Success)
        {
            LogRebuildSucceeded(result.TotalRebuilt, result.Duration);
        }
        else
        {
            LogRebuildWithFailures(
                result.TotalRebuilt,
                result.Details.Count - result.TotalRebuilt,
                string.Join(", ", result.FailedProjections));
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
            var checkpoint = await _projectionManager.GetCheckpointAsync(projectionName, cancellationToken).ConfigureAwait(false);
            minCheckpoint = Math.Min(minCheckpoint, checkpoint);
        }

        if (minCheckpoint == long.MaxValue)
        {
            minCheckpoint = 0;
        }

        // Read only events after the minimum checkpoint position — the store filters at
        // the index level, so we never load already-processed events into memory.
        var newEvents = await _eventStore.ReadAsync(Query.All(), null, minCheckpoint).ConfigureAwait(false);

        if (newEvents.Length == 0)
        {
            return;
        }

        LogProcessingEvents(newEvents.Length, minCheckpoint + 1);

        // Process in batches
        var batches = newEvents.Chunk(_options.BatchSize);

        foreach (var batch in batches)
        {
            // Chunk returns T[] segments — no need to call ToArray() again
            await _projectionManager.UpdateAsync(batch, cancellationToken).ConfigureAwait(false);
        }

        LogProcessedEvents(newEvents.Length);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Projection daemon starting...")]
    private partial void LogDaemonStarting();

    [LoggerMessage(Level = LogLevel.Information, Message = "Projection daemon polling started with interval: {Interval}")]
    private partial void LogPollingStarted(TimeSpan interval);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error processing events in projection daemon")]
    private partial void LogProcessingError(Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Projection daemon stopped")]
    private partial void LogDaemonStopped();

    [LoggerMessage(Level = LogLevel.Information, Message = "Checking for projections that need rebuilding...")]
    private partial void LogCheckingForRebuilds();

    [LoggerMessage(Level = LogLevel.Information, Message = "All projections are up to date (no rebuilds needed)")]
    private partial void LogAllProjectionsUpToDate();

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully rebuilt {Count} projections in {Duration}")]
    private partial void LogRebuildSucceeded(int count, TimeSpan duration);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Projection rebuild completed with {SuccessCount} successes and {FailureCount} failures. Failed projections: {FailedProjections}")]
    private partial void LogRebuildWithFailures(int successCount, int failureCount, string failedProjections);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Processing {EventCount} new events from position {MinPosition}")]
    private partial void LogProcessingEvents(int eventCount, long minPosition);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Processed {EventCount} events successfully")]
    private partial void LogProcessedEvents(int eventCount);
}
