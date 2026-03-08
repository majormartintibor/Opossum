using Opossum.Configuration;
using Opossum.Core;
using Opossum.Telemetry;

namespace Opossum.Projections;

/// <summary>
/// Manages all projection rebuild operations, cleanly separated from the live event
/// processing handled by <see cref="ProjectionManager"/>.
/// <para>
/// This class owns rebuild orchestration, status tracking, and (in later phases) crash
/// recovery via <see cref="ProjectionRebuildJournal"/>. It delegates per-projection
/// store operations (<c>BeginRebuildAsync</c>, <c>ApplyAsync</c>, <c>CommitRebuildAsync</c>)
/// to <see cref="ProjectionManager.ProjectionRegistration"/> objects obtained through
/// <see cref="ProjectionManager.GetRegistration"/>.
/// </para>
/// </summary>
internal sealed partial class ProjectionRebuilder : IProjectionRebuilder
{
    private readonly IEventStore _eventStore;
    private readonly ProjectionManager _projectionManager;
    private readonly ProjectionOptions _projectionOptions;
    private readonly ILogger<ProjectionRebuilder> _logger;
    private readonly string _checkpointPath;

    // Lock for rebuild status tracking
    private readonly object _rebuildLock = new();
    private ProjectionRebuildStatus _currentRebuildStatus = new()
    {
        IsRebuilding = false,
        InProgressProjections = [],
        QueuedProjections = []
    };

    public ProjectionRebuilder(
        OpossumOptions options,
        IEventStore eventStore,
        ProjectionManager projectionManager,
        ProjectionOptions projectionOptions,
        ILogger<ProjectionRebuilder>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(projectionManager);
        ArgumentNullException.ThrowIfNull(projectionOptions);

        _eventStore = eventStore;
        _projectionManager = projectionManager;
        _projectionOptions = projectionOptions;
        _logger = logger ?? NullLogger<ProjectionRebuilder>.Instance;

        if (options.StoreName is null)
        {
            throw new InvalidOperationException("No store configured");
        }

        var contextPath = Path.Combine(options.RootPath, options.StoreName);
        _checkpointPath = Path.Combine(contextPath, "Projections", "_checkpoints");

        Directory.CreateDirectory(_checkpointPath);
    }

    /// <inheritdoc />
    public Task ResumeInterruptedRebuildsAsync(CancellationToken cancellationToken = default)
    {
        // Stub — implemented in Phase 3 (P3-T4).
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<ProjectionRebuildResult> RebuildAsync(
        string projectionName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionName);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var eventsProcessed = await RebuildCoreAsync(projectionName, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            return new ProjectionRebuildResult
            {
                TotalRebuilt = 1,
                Duration = stopwatch.Elapsed,
                Details =
                [
                    new ProjectionRebuildDetail
                    {
                        ProjectionName = projectionName,
                        Success = true,
                        Duration = stopwatch.Elapsed,
                        EventsProcessed = eventsProcessed,
                        ErrorMessage = null
                    }
                ]
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            return new ProjectionRebuildResult
            {
                TotalRebuilt = 0,
                Duration = stopwatch.Elapsed,
                Details =
                [
                    new ProjectionRebuildDetail
                    {
                        ProjectionName = projectionName,
                        Success = false,
                        Duration = stopwatch.Elapsed,
                        EventsProcessed = 0,
                        ErrorMessage = ex.Message
                    }
                ]
            };
        }
    }

    /// <inheritdoc />
    public async Task<ProjectionRebuildResult> RebuildAllAsync(
        bool forceRebuild = false,
        CancellationToken cancellationToken = default)
    {
        var projections = _projectionManager.GetRegisteredProjections();
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

    /// <inheritdoc />
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
                        var eventsProcessed = await RebuildCoreAsync(projectionName, ct).ConfigureAwait(false);

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

    /// <inheritdoc />
    public Task<ProjectionRebuildStatus> GetRebuildStatusAsync()
    {
        lock (_rebuildLock)
        {
            return Task.FromResult(_currentRebuildStatus);
        }
    }

    /// <summary>
    /// Core rebuild implementation that returns the number of projection-relevant events
    /// actually processed. Callers use this count for accurate <see cref="ProjectionRebuildDetail.EventsProcessed"/>
    /// reporting instead of the checkpoint position (which equals the last store position even
    /// when zero relevant events exist).
    /// Events are read in batches of <see cref="ProjectionOptions.RebuildBatchSize"/> to bound
    /// peak memory usage regardless of how many total events match the projection.
    /// </summary>
    private async Task<int> RebuildCoreAsync(string projectionName, CancellationToken cancellationToken)
    {
        using var activity = OpossumsActivity.Source.StartActivity(OpossumsActivity.ProjectionRebuild);
        activity?.SetTag("opossum.projection", projectionName);

        // Wait for any in-progress update to finish before rebuilding.
        // (UpdateAsync uses fail-fast; RebuildAsync must wait so the admin endpoint does not
        // immediately lose the race against the daemon's polling loop.)
        using (await _projectionManager.AcquireProjectionLockAsync(projectionName, cancellationToken, failFast: false).ConfigureAwait(false))
        {
            var registration = _projectionManager.GetRegistration(projectionName)
                ?? throw new InvalidOperationException($"Projection '{projectionName}' is not registered");

            // Switch store to rebuild mode with write-through: state changes are written
            // directly to the temp directory during event replay so memory stays bounded.
            // Old projection files remain accessible to readers until CommitRebuildAsync performs
            // the atomic directory swap at the end of the rebuild.
            await registration.BeginRebuildAsync().ConfigureAwait(false);

            var query = Query.FromEventTypes([.. registration.EventTypes]);
            var batchSize = _projectionOptions.RebuildBatchSize;
            long fromPosition = 0;
            int totalEventsProcessed = 0;
            long lastCheckpointPosition = 0;

            // Capture the store head before reading any events.  After the rebuild we will
            // advance the checkpoint to at least this position so the daemon does not
            // needlessly re-read thousands of non-relevant events that already exist between
            // the last relevant event and the store head (e.g. a sparse projection whose last
            // event is at position 5 600 in a store with 86 000+ events would otherwise cause
            // the daemon to call SaveCheckpointAsync ~81 times in rapid succession, triggering
            // UnauthorizedAccessException on Windows when MoveFileEx is called on a file the
            // OS has not yet fully released from the previous atomic rename).
            var storeHeadBeforeRebuild = await _eventStore.ReadLastAsync(Query.All(), cancellationToken).ConfigureAwait(false);
            var rebuildTargetPosition = storeHeadBeforeRebuild?.Position ?? 0;

            var progressStopwatch = Stopwatch.StartNew();

            // Read and process events in bounded batches so that peak memory is proportional
            // to batchSize × avg-event-size rather than total-events × avg-event-size.
            // ReadAsync returns [] when no events with Position > fromPosition remain, which
            // terminates the loop.  The exclusive fromPosition filter ensures the last event
            // of each batch is never re-read on the following page request.
            cancellationToken.ThrowIfCancellationRequested();
            var batch = await _eventStore.ReadAsync(query, null, fromPosition, maxCount: batchSize).ConfigureAwait(false);

            while (batch.Length > 0)
            {
                foreach (var evt in batch)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await registration.ApplyAsync(evt, cancellationToken).ConfigureAwait(false);
                }

                totalEventsProcessed += batch.Length;
                lastCheckpointPosition = batch[^1].Position;
                fromPosition = lastCheckpointPosition;

                // Log progress after each batch so developers can see the rebuild is still running.
                // The rate is derived from wall-clock time to give a meaningful events/second figure.
                var elapsedMs = progressStopwatch.ElapsedMilliseconds;
                var rate = elapsedMs > 0 ? totalEventsProcessed * 1000L / elapsedMs : 0;
                LogRebuildProgress(projectionName, totalEventsProcessed, rate, progressStopwatch.Elapsed);

                cancellationToken.ThrowIfCancellationRequested();
                batch = await _eventStore.ReadAsync(query, null, fromPosition, maxCount: batchSize).ConfigureAwait(false);
            }

            // Write tag indices and perform the atomic directory swap
            await registration.CommitRebuildAsync(cancellationToken).ConfigureAwait(false);

            // Advance checkpoint to at least the pre-rebuild store head.
            // Using Math.Max handles events appended during the rebuild that the loop may
            // have processed (lastCheckpointPosition can exceed rebuildTargetPosition).
            // For an empty store both values are 0, which is still written as a file so
            // RebuildAllAsync(forceRebuild: false) treats the projection as "already rebuilt"
            // rather than "never built" on the next startup.
            activity?.SetTag("opossum.events_processed", totalEventsProcessed);
            lastCheckpointPosition = Math.Max(rebuildTargetPosition, lastCheckpointPosition);
            await _projectionManager.SaveCheckpointAsync(projectionName, lastCheckpointPosition, cancellationToken).ConfigureAwait(false);

            return totalEventsProcessed;
        }
        // Lock automatically released here
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Rebuilding '{ProjectionName}': {EventsProcessed} events processed ({Rate} events/s, elapsed {Elapsed})")]
    private partial void LogRebuildProgress(string projectionName, int eventsProcessed, long rate, TimeSpan elapsed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Projection '{ProjectionName}' rebuilt successfully in {ElapsedMs}ms ({EventCount} events)")]
    private partial void LogProjectionRebuiltSuccessfully(string projectionName, long elapsedMs, long eventCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to rebuild projection '{ProjectionName}'")]
    private partial void LogProjectionRebuildFailed(Exception ex, string projectionName);

    [LoggerMessage(Level = LogLevel.Information, Message = "All {Count} projections rebuilt successfully in {Duration}")]
    private partial void LogAllProjectionsRebuiltSuccessfully(int count, TimeSpan duration);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Projection rebuild completed with errors. Success: {SuccessCount}/{TotalCount}. Failed: {FailedProjections}")]
    private partial void LogRebuildWithErrors(int successCount, int totalCount, string failedProjections);
}
