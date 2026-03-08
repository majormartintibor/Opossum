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
    private readonly string _projectionsPath;

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
        _projectionsPath = Path.Combine(contextPath, "Projections");
        _checkpointPath = Path.Combine(_projectionsPath, "_checkpoints");

        Directory.CreateDirectory(_checkpointPath);
    }

    /// <inheritdoc />
    public async Task ResumeInterruptedRebuildsAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_checkpointPath))
            return;

        var journalFiles = Directory.GetFiles(_checkpointPath, "*.rebuild.json");
        if (journalFiles.Length == 0)
        {
            // No interrupted rebuilds — clean any orphaned temp dirs and return.
            await CleanOrphanedTempDirectoriesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        foreach (var journalFile in journalFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Derive projection name from file name: "{name}.rebuild.json"
            var fileName = Path.GetFileName(journalFile);
            var name = fileName[..^".rebuild.json".Length];

            var journal = await ReadJournalAsync(name, cancellationToken).ConfigureAwait(false);
            if (journal is null)
            {
                // File vanished between enumeration and read — skip.
                continue;
            }

            if (!Directory.Exists(journal.TempPath))
            {
                // Temp directory is gone (e.g. manual cleanup). Discard the journal;
                // the normal RebuildAllAsync call will pick it up as missing-checkpoint.
                LogResumeJournalMissingTempDir(name, journal.TempPath);
                await DeleteJournalAsync(name, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var registration = _projectionManager.GetRegistration(name);
            if (registration is null)
            {
                // Projection is no longer registered (code changed between runs).
                // Clean up journal + tags + temp dir.
                LogResumeProjectionNotRegistered(name);
                await DeleteJournalAsync(name, cancellationToken).ConfigureAwait(false);
                DeleteDirectorySafe(journal.TempPath);
                continue;
            }

            LogResumingInterruptedRebuild(name, journal.ResumeFromPosition,
                journal.StoreHeadAtStart, journal.StartedAt);

            try
            {
                // Reuse the existing temp directory so previously written projection files
                // are preserved.
                await registration.BeginRebuildAsync(journal.TempPath).ConfigureAwait(false);

                // Restore the tag accumulator that was persisted alongside the journal.
                // Without this, the commit would only contain tags for the resumed portion.
                var restoredTags = await LoadTagAccumulatorAsync(name, cancellationToken).ConfigureAwait(false);
                if (restoredTags is not null)
                {
                    registration.RestoreTagAccumulator(restoredTags);
                }

                // Resume the event loop from where the last flush left off.
                await RebuildCoreAsync(name, journal, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Log but don't rethrow — other projections may still be resumable.
                // The journal is left on disk so a future restart can try again.
                LogProjectionRebuildFailed(ex, name);
            }
        }

        await CleanOrphanedTempDirectoriesAsync(cancellationToken).ConfigureAwait(false);
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
    /// Core rebuild implementation for a fresh rebuild (from position 0).
    /// Acquires the projection lock, switches the store to rebuild mode, creates a journal,
    /// replays all events, commits, and saves the checkpoint.
    /// </summary>
    private async Task<int> RebuildCoreAsync(string projectionName, CancellationToken cancellationToken)
    {
        using var activity = OpossumsActivity.Source.StartActivity(OpossumsActivity.ProjectionRebuild);
        activity?.SetTag("opossum.projection", projectionName);

        using (await _projectionManager.AcquireProjectionLockAsync(projectionName, cancellationToken, failFast: false).ConfigureAwait(false))
        {
            var registration = _projectionManager.GetRegistration(projectionName)
                ?? throw new InvalidOperationException($"Projection '{projectionName}' is not registered");

            // Switch store to rebuild mode with write-through.
            await registration.BeginRebuildAsync().ConfigureAwait(false);

            // Capture the store head before reading any events.
            var storeHeadBeforeRebuild = await _eventStore.ReadLastAsync(Query.All(), cancellationToken).ConfigureAwait(false);
            var rebuildTargetPosition = storeHeadBeforeRebuild?.Position ?? 0;

            // Create the rebuild journal so the rebuild can be resumed on crash.
            var tempPath = registration.GetRebuildTempPath()
                ?? throw new InvalidOperationException($"Projection '{projectionName}' store did not initialise a temp path");
            await CreateJournalAsync(projectionName, tempPath, rebuildTargetPosition, cancellationToken).ConfigureAwait(false);

            return await ReplayEventsAsync(projectionName, registration, fromPosition: 0,
                rebuildTargetPosition, activity, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Core rebuild implementation for resuming an interrupted rebuild.
    /// The store must already be in rebuild mode with the tag accumulator restored.
    /// Uses the journal's <see cref="ProjectionRebuildJournal.ResumeFromPosition"/> and
    /// <see cref="ProjectionRebuildJournal.StoreHeadAtStart"/> to skip already-processed events.
    /// </summary>
    private async Task<int> RebuildCoreAsync(
        string projectionName,
        ProjectionRebuildJournal journal,
        CancellationToken cancellationToken)
    {
        using var activity = OpossumsActivity.Source.StartActivity(OpossumsActivity.ProjectionRebuild);
        activity?.SetTag("opossum.projection", projectionName);
        activity?.SetTag("opossum.rebuild.resumed", true);
        activity?.SetTag("opossum.rebuild.resume_from", journal.ResumeFromPosition);

        using (await _projectionManager.AcquireProjectionLockAsync(projectionName, cancellationToken, failFast: false).ConfigureAwait(false))
        {
            var registration = _projectionManager.GetRegistration(projectionName)
                ?? throw new InvalidOperationException($"Projection '{projectionName}' is not registered");

            // No BeginRebuildAsync — caller already did it with the journal's temp path.
            // No CreateJournalAsync — journal already exists on disk.
            return await ReplayEventsAsync(projectionName, registration,
                fromPosition: journal.ResumeFromPosition,
                journal.StoreHeadAtStart, activity, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Shared event replay loop used by both fresh and resumed rebuilds.
    /// Reads events in batches from <paramref name="fromPosition"/>, applies them to the
    /// projection, periodically flushes the journal and tag accumulator, then commits and
    /// saves the checkpoint.
    /// </summary>
    private async Task<int> ReplayEventsAsync(
        string projectionName,
        ProjectionManager.ProjectionRegistration registration,
        long fromPosition,
        long rebuildTargetPosition,
        System.Diagnostics.Activity? activity,
        CancellationToken cancellationToken)
    {
        var query = Query.FromEventTypes([.. registration.EventTypes]);
        var batchSize = _projectionOptions.RebuildBatchSize;
        var flushInterval = _projectionOptions.RebuildFlushInterval;
        int totalEventsProcessed = 0;
        int eventsSinceLastFlush = 0;
        long lastCheckpointPosition = fromPosition;

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

            // Periodically flush the rebuild journal and tag accumulator so that on crash
            // at most RebuildFlushInterval events need to be re-processed.
            // Both files are flushed together so that on resume the journal position and
            // tag accumulator are consistent.
            eventsSinceLastFlush += batch.Length;
            if (eventsSinceLastFlush >= flushInterval)
            {
                await FlushJournalAsync(projectionName, lastCheckpointPosition, cancellationToken).ConfigureAwait(false);

                var tagAccumulator = registration.GetTagAccumulator();
                if (tagAccumulator is not null)
                {
                    await FlushTagAccumulatorAsync(projectionName, tagAccumulator, cancellationToken).ConfigureAwait(false);
                }

                eventsSinceLastFlush = 0;
            }

            // Log progress after each batch so developers can see the rebuild is still running.
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

        // Rebuild completed successfully — remove the journal and tags companion file.
        // On error the files are intentionally left on disk so that
        // ResumeInterruptedRebuildsAsync can pick them up on the next startup.
        await DeleteJournalAsync(projectionName, cancellationToken).ConfigureAwait(false);

        return totalEventsProcessed;
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

    // --- Journal and tag accumulator file I/O (Phase 3) ---

    private static readonly JsonSerializerOptions _journalJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Returns the path to the rebuild journal file for the given projection.
    /// </summary>
    private string GetJournalFilePath(string projectionName)
    {
        return Path.Combine(_checkpointPath, $"{projectionName}.rebuild.json");
    }

    /// <summary>
    /// Returns the path to the tag accumulator companion file for the given projection.
    /// </summary>
    private string GetTagAccumulatorFilePath(string projectionName)
    {
        return Path.Combine(_checkpointPath, $"{projectionName}.rebuild.tags.json");
    }

    /// <summary>
    /// Creates a new rebuild journal for the given projection with
    /// <see cref="ProjectionRebuildJournal.ResumeFromPosition"/> set to 0
    /// and <see cref="ProjectionRebuildJournal.StartedAt"/> set to now.
    /// The file is written atomically via a temp file + rename.
    /// </summary>
    private async Task CreateJournalAsync(
        string name,
        string tempPath,
        long storeHead,
        CancellationToken cancellationToken)
    {
        var journal = new ProjectionRebuildJournal
        {
            ProjectionName = name,
            TempPath = tempPath,
            StoreHeadAtStart = storeHead,
            ResumeFromPosition = 0,
            StartedAt = DateTimeOffset.UtcNow,
            LastFlushedAt = DateTimeOffset.UtcNow
        };

        var journalPath = GetJournalFilePath(name);
        await WriteAtomicAsync(journalPath, journal, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates an existing rebuild journal, setting
    /// <see cref="ProjectionRebuildJournal.ResumeFromPosition"/> to <paramref name="position"/>
    /// and <see cref="ProjectionRebuildJournal.LastFlushedAt"/> to now.
    /// The file is written atomically via a temp file + rename.
    /// </summary>
    private async Task FlushJournalAsync(
        string name,
        long position,
        CancellationToken cancellationToken)
    {
        var journalPath = GetJournalFilePath(name);
        var json = await File.ReadAllTextAsync(journalPath, cancellationToken).ConfigureAwait(false);
        var journal = JsonSerializer.Deserialize<ProjectionRebuildJournal>(json, _journalJsonOptions)
            ?? throw new InvalidOperationException($"Rebuild journal for '{name}' is corrupted or missing");

        journal.ResumeFromPosition = position;
        journal.LastFlushedAt = DateTimeOffset.UtcNow;

        await WriteAtomicAsync(journalPath, journal, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Serialises the tag accumulator to the companion file
    /// <c>{name}.rebuild.tags.json</c>. Written atomically via a temp file + rename
    /// alongside the journal flush.
    /// </summary>
    private async Task FlushTagAccumulatorAsync(
        string name,
        Dictionary<string, HashSet<string>> tagAccumulator,
        CancellationToken cancellationToken)
    {
        var tagPath = GetTagAccumulatorFilePath(name);
        await WriteAtomicAsync(tagPath, tagAccumulator, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the deserialised tag accumulator from the companion file, or <c>null</c>
    /// if the file does not exist. Used during crash recovery to restore pre-crash tag state.
    /// </summary>
    private async Task<Dictionary<string, HashSet<string>>?> LoadTagAccumulatorAsync(
        string name,
        CancellationToken cancellationToken)
    {
        var tagPath = GetTagAccumulatorFilePath(name);
        if (!File.Exists(tagPath))
            return null;

        var json = await File.ReadAllTextAsync(tagPath, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<Dictionary<string, HashSet<string>>>(json, _journalJsonOptions);
    }

    /// <summary>
    /// Returns the deserialised rebuild journal, or <c>null</c> if the file does not exist.
    /// </summary>
    private async Task<ProjectionRebuildJournal?> ReadJournalAsync(
        string name,
        CancellationToken cancellationToken)
    {
        var journalPath = GetJournalFilePath(name);
        if (!File.Exists(journalPath))
            return null;

        var json = await File.ReadAllTextAsync(journalPath, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<ProjectionRebuildJournal>(json, _journalJsonOptions);
    }

    /// <summary>
    /// Deletes the rebuild journal and the tag accumulator companion file for the given
    /// projection, if they exist.
    /// </summary>
    private Task DeleteJournalAsync(string name, CancellationToken cancellationToken)
    {
        _ = cancellationToken; // reserved for future async deletion

        var journalPath = GetJournalFilePath(name);
        if (File.Exists(journalPath))
            File.Delete(journalPath);

        var tagPath = GetTagAccumulatorFilePath(name);
        if (File.Exists(tagPath))
            File.Delete(tagPath);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Atomically writes <paramref name="value"/> as JSON to <paramref name="targetPath"/>
    /// using a temp file + rename to ensure readers never see a partial file.
    /// </summary>
    private static async Task WriteAtomicAsync<T>(
        string targetPath,
        T value,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, _journalJsonOptions);
        var tempFile = $"{targetPath}.tmp.{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllTextAsync(tempFile, json, cancellationToken).ConfigureAwait(false);
            File.Move(tempFile, targetPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempFile))
            { try { File.Delete(tempFile); } catch { /* ignore cleanup errors */ } }
            throw;
        }
    }

    /// <summary>
    /// Scans the Projections directory for temp directories (<c>*.tmp.*</c>) that have no
    /// matching rebuild journal in <c>_checkpointPath</c>. Such directories are orphans left
    /// behind by a crash that occurred before the journal was written, or after the journal
    /// was manually deleted. Each orphan is deleted and an informational message is logged.
    /// </summary>
    private Task CleanOrphanedTempDirectoriesAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_projectionsPath))
            return Task.CompletedTask;

        foreach (var dir in Directory.GetDirectories(_projectionsPath, "*.tmp.*"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Directory name is "{ProjectionName}.tmp.{guid}" — extract the projection name.
            var dirName = Path.GetFileName(dir);
            var tmpIndex = dirName.IndexOf(".tmp.", StringComparison.Ordinal);
            if (tmpIndex <= 0)
                continue;

            var projectionName = dirName[..tmpIndex];

            // If a journal still exists for this projection the temp dir is not orphaned —
            // it belongs to an interrupted rebuild that was already resumed (or will be on
            // the next call to ResumeInterruptedRebuildsAsync).
            var journalPath = GetJournalFilePath(projectionName);
            if (File.Exists(journalPath))
                continue;

            LogOrphanedTempDirectoryDeleted(projectionName, dir);
            DeleteDirectorySafe(dir);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Deletes a directory and all its contents. Safe to call when the directory does not exist.
    /// </summary>
    private static void DeleteDirectorySafe(string path)
    {
        if (!Directory.Exists(path))
            return;

        // Remove read-only attributes (write-protected projection files).
        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
        {
            var attrs = File.GetAttributes(file);
            if ((attrs & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
        }

        Directory.Delete(path, recursive: true);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rebuild journal for '{ProjectionName}' references missing temp dir at '{TempPath}'. Journal discarded; projection will be rebuilt from scratch.")]
    private partial void LogResumeJournalMissingTempDir(string projectionName, string tempPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rebuild journal for '{ProjectionName}' references a projection that is no longer registered. Journal and temp directory discarded.")]
    private partial void LogResumeProjectionNotRegistered(string projectionName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Resuming interrupted rebuild of '{ProjectionName}' from event position {ResumeFromPosition} (store head was {StoreHeadAtStart}, started {StartedAt})")]
    private partial void LogResumingInterruptedRebuild(string projectionName, long resumeFromPosition, long storeHeadAtStart, DateTimeOffset startedAt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted orphaned temp directory for projection '{ProjectionName}' at '{TempPath}' (no matching rebuild journal found)")]
    private partial void LogOrphanedTempDirectoryDeleted(string projectionName, string tempPath);
}
