using Opossum.Core;
using Opossum.Configuration;
using Opossum.Exceptions;
using Opossum.Telemetry;

namespace Opossum.Storage.FileSystem;

internal sealed partial class FileSystemEventStore : IEventStore, IDisposable
{
    private readonly OpossumOptions _options;
    private readonly LedgerManager _ledgerManager;
    private readonly EventFileManager _eventFileManager;
    private readonly IndexManager _indexManager;
    private readonly CrossProcessLockManager _crossProcessLockManager;
    private readonly SemaphoreSlim _appendLock = new(1, 1);
    private readonly ILogger<FileSystemEventStore> _logger;

    public FileSystemEventStore(OpossumOptions options, ILogger<FileSystemEventStore>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _logger = logger ?? NullLogger<FileSystemEventStore>.Instance;
        _ledgerManager = new LedgerManager(options.FlushEventsImmediately);
        _eventFileManager = new EventFileManager(options.FlushEventsImmediately, options.WriteProtectEventFiles);
        _indexManager = new IndexManager(options.FlushEventsImmediately);
        _crossProcessLockManager = new CrossProcessLockManager(options.CrossProcessLockTimeout);
    }

    // Constructor for testing with dependency injection
    internal FileSystemEventStore(
        OpossumOptions options,
        LedgerManager ledgerManager,
        EventFileManager eventFileManager,
        IndexManager indexManager)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(ledgerManager);
        ArgumentNullException.ThrowIfNull(eventFileManager);
        ArgumentNullException.ThrowIfNull(indexManager);

        _options = options;
        _logger = NullLogger<FileSystemEventStore>.Instance;
        _ledgerManager = ledgerManager;
        _eventFileManager = eventFileManager;
        _indexManager = indexManager;
        _crossProcessLockManager = new CrossProcessLockManager(options.CrossProcessLockTimeout);
    }

    public void Dispose()
    {
        _appendLock.Dispose();
    }

    public async Task AppendAsync(NewEvent[] events, AppendCondition? condition, CancellationToken cancellationToken = default)
    {
        // 1. Validation
        ArgumentNullException.ThrowIfNull(events);

        if (events.Length == 0)
        {
            throw new ArgumentException("Events array cannot be empty", nameof(events));
        }

        // Validate all events have valid Event objects
        for (int i = 0; i < events.Length; i++)
        {
            if (events[i].Event == null)
            {
                throw new ArgumentException($"Event at index {i} has null Event property", nameof(events));
            }

            if (string.IsNullOrWhiteSpace(events[i].Event.EventType))
            {
                throw new ArgumentException($"Event at index {i} has empty EventType", nameof(events));
            }
        }

        // 2. Get context path — Opossum is single-context by design (see ADR-004)
        if (_options.StoreName is null)
        {
            throw new InvalidOperationException("No store configured. Call options.UseStore(\"YourStoreName\") in the configuration.");
        }

        var contextPath = GetContextPath(_options.StoreName);

        using var activity = OpossumsActivity.Source.StartActivity(OpossumsActivity.Append);
        activity?.SetTag("db.operation", "append");
        activity?.SetTag("opossum.event_count", events.Length);
        activity?.SetTag("opossum.context", _options.StoreName);

        // 3. Use semaphore for atomic operation (one append at a time within this process)
        await _appendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 4. Acquire cross-process file lock to serialise appends across all processes
            //    sharing the same store directory (e.g. on a UNC path or mapped drive).
            await using var _ = await _crossProcessLockManager
                .AcquireAsync(contextPath, cancellationToken)
                .ConfigureAwait(false);

            // 5. Check AppendCondition
            if (condition != null)
            {
                await ValidateAppendConditionAsync(contextPath, condition).ConfigureAwait(false);
            }

            // 6. Allocate sequence positions and build SequencedEvents
            var startPosition = await _ledgerManager.GetNextSequencePositionAsync(contextPath).ConfigureAwait(false);

            var sequencedEvents = new SequencedEvent[events.Length];
            for (int i = 0; i < events.Length; i++)
            {
                // Metadata is immutable — create a new instance with Timestamp defaulted to UtcNow
                // if the caller did not supply one, rather than mutating the caller's object.
                var metadata = events[i].Metadata.Timestamp == default
                    ? events[i].Metadata with { Timestamp = DateTimeOffset.UtcNow }
                    : events[i].Metadata;

                sequencedEvents[i] = new SequencedEvent
                {
                    Position = startPosition + i,
                    Event = events[i].Event,
                    Metadata = metadata
                };
            }

            // 7. Write events to files
            var eventsPath = GetEventsPath(contextPath);
            foreach (var evt in sequencedEvents)
            {
                await _eventFileManager.WriteEventAsync(eventsPath, evt).ConfigureAwait(false);
            }

            // 8. Update indices
            foreach (var evt in sequencedEvents)
            {
                await _indexManager.AddEventToIndicesAsync(contextPath, evt).ConfigureAwait(false);
            }

            // 9. Update ledger
            var lastPosition = startPosition + events.Length - 1;
            await _ledgerManager.UpdateSequencePositionAsync(contextPath, lastPosition).ConfigureAwait(false);
        }
        catch (AppendConditionFailedException)
        {
            activity?.SetTag("opossum.append.conflict", true);
            throw;
        }
        catch (Exception ex)
        {
            LogAppendError(ex, _options.StoreName ?? string.Empty);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            _appendLock.Release();
        }
    }

    public async Task<SequencedEvent[]> ReadAsync(Query query, ReadOption[]? readOptions, long? fromPosition = null)
    {
        // 1. Validation
        ArgumentNullException.ThrowIfNull(query);

        // 2. Get context path — Opossum is single-context by design (see ADR-004)
        if (_options.StoreName is null)
        {
            throw new InvalidOperationException("No store configured. Call options.UseStore(\"YourStoreName\") in the configuration.");
        }

        var contextPath = GetContextPath(_options.StoreName);

        using var activity = OpossumsActivity.Source.StartActivity(OpossumsActivity.Read);
        activity?.SetTag("db.operation", "read");
        activity?.SetTag("opossum.context", _options.StoreName);

        try
        {
            // 3. Get positions matching query, optionally filtered by fromPosition
            var positions = await GetPositionsForQueryAsync(contextPath, query, fromPosition).ConfigureAwait(false);

            if (positions.Length == 0)
            {
                return [];
            }

            // 4. Apply descending order to positions BEFORE reading
            // This is 10x faster than reading all events then reversing the array
            if (readOptions != null && readOptions.Contains(ReadOption.Descending))
            {
                Array.Reverse(positions);
            }

            // 5. Read events from files in the correct order
            var eventsPath = GetEventsPath(contextPath);
            var events = await _eventFileManager.ReadEventsAsync(eventsPath, positions).ConfigureAwait(false);

            activity?.SetTag("opossum.event_count", events.Length);
            return events;
        }
        catch (Exception ex)
        {
            LogReadError(ex, _options.StoreName);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Gets all positions for a query.
    /// Implements full query logic with OR between QueryItems and proper AND/OR within items.
    /// When <paramref name="fromPosition"/> is provided, only positions strictly greater than
    /// that value are returned.
    /// </summary>
    private async Task<long[]> GetPositionsForQueryAsync(string contextPath, Query query, long? fromPosition = null)
    {
        // Handle Query.All() - return all positions from ledger
        if (query.QueryItems.Count == 0)
        {
            return await GetAllPositionsAsync(contextPath, fromPosition).ConfigureAwait(false);
        }

        var allPositions = new HashSet<long>();

        // OR logic between QueryItems
        foreach (var queryItem in query.QueryItems)
        {
            var positions = await GetPositionsForQueryItemAsync(contextPath, queryItem).ConfigureAwait(false);
            foreach (var pos in positions)
            {
                allPositions.Add(pos);
            }
        }

        var result = allPositions.ToArray();
        Array.Sort(result);

        // Filter by fromPosition for non-All queries (index lookups return all positions)
        if (fromPosition.HasValue)
        {
            var threshold = fromPosition.Value;
            result = Array.FindAll(result, p => p > threshold);
        }

        return result;
    }

    /// <summary>
    /// Gets positions for a single QueryItem.
    /// EventTypes are OR'd together, Tags are AND'd together, then EventTypes AND Tags.
    /// </summary>
    private async Task<long[]> GetPositionsForQueryItemAsync(string contextPath, QueryItem queryItem)
    {
        HashSet<long>? eventTypePositions = null;
        HashSet<long>? tagPositions = null;

        // Get positions by EventTypes (OR logic within EventTypes)
        if (queryItem.EventTypes != null && queryItem.EventTypes.Count > 0)
        {
            var typePositionsArray = await _indexManager.GetPositionsByEventTypesAsync(
                contextPath,
                [.. queryItem.EventTypes]).ConfigureAwait(false);

            eventTypePositions = [.. typePositionsArray];
        }

        // Get positions by Tags (AND logic within Tags)
        if (queryItem.Tags != null && queryItem.Tags.Count > 0)
        {
            // For AND logic, we need to intersect all tag positions
            List<long[]> tagPositionSets = [];

            foreach (var tag in queryItem.Tags)
            {
                var positions = await _indexManager.GetPositionsByTagAsync(contextPath, tag).ConfigureAwait(false);
                tagPositionSets.Add(positions);
            }

            if (tagPositionSets.Count > 0)
            {
                // Start with first tag's positions
                tagPositions = [.. tagPositionSets[0]];

                // Intersect with all other tags (AND logic)
                for (int i = 1; i < tagPositionSets.Count; i++)
                {
                    tagPositions.IntersectWith(tagPositionSets[i]);
                }
            }
        }

        // Combine EventType and Tag results (AND logic between them)
        if (eventTypePositions != null && tagPositions != null)
        {
            // Intersection: must match both EventType AND Tags
            eventTypePositions.IntersectWith(tagPositions);
            return [.. eventTypePositions];
        }
        else if (eventTypePositions != null)
        {
            return [.. eventTypePositions];
        }
        else if (tagPositions != null)
        {
            return [.. tagPositions];
        }

        // No constraints specified in this QueryItem
        return [];
    }

    /// <summary>
    /// Gets all event positions in the context (for Query.All()).
    /// Optimized: uses simple sequential array generation (most efficient for contiguous sequences).
    /// When <paramref name="fromPosition"/> is provided, only positions strictly greater than
    /// that value are generated — avoiding allocation of the full prefix.
    /// </summary>
    private async Task<long[]> GetAllPositionsAsync(string contextPath, long? fromPosition = null)
    {
        var lastPosition = await _ledgerManager.GetLastSequencePositionAsync(contextPath).ConfigureAwait(false);

        if (lastPosition == 0)
        {
            return [];
        }

        var startPosition = fromPosition.HasValue ? fromPosition.Value + 1 : 1L;

        if (startPosition > lastPosition)
        {
            return [];
        }

        // Sequential generation is fastest for creating contiguous number sequences
        var count = (int)(lastPosition - startPosition + 1);
        var positions = new long[count];
        for (int i = 0; i < count; i++)
        {
            positions[i] = startPosition + i;
        }

        return positions;
    }

    /// <summary>
    /// Validates the append condition by checking if conflicting events exist.
    /// Per DCB spec: when AfterSequencePosition is present, FailIfEventsMatch should
    /// only check for events AFTER that position (ignore events before).
    /// </summary>
    private async Task ValidateAppendConditionAsync(string contextPath, AppendCondition condition)
    {
        // Get current ledger position for comparison
        var currentPosition = await _ledgerManager.GetLastSequencePositionAsync(contextPath).ConfigureAwait(false);

        // Check AfterSequencePosition constraint
        // This checks if ANY events were added since the specified position
        if (condition.AfterSequencePosition.HasValue)
        {
            if (currentPosition != condition.AfterSequencePosition.Value)
            {
                // Events were added - now check if they match our query
                // If FailIfEventsMatch is provided, we only care about matching events
                if (condition.FailIfEventsMatch != null && condition.FailIfEventsMatch.QueryItems.Count > 0)
                {
                    // Get all positions matching the query
                    var matchingPositions = await GetPositionsForQueryAsync(contextPath, condition.FailIfEventsMatch).ConfigureAwait(false);

                    // Filter to only positions AFTER our read (> AfterSequencePosition)
                    var newMatchingPositions = matchingPositions
                        .Where(p => p > condition.AfterSequencePosition.Value)
                        .ToArray();

                    if (newMatchingPositions.Length > 0)
                    {
                        throw new ConcurrencyException(
                            $"Append condition failed: found {newMatchingPositions.Length} matching event(s) after position {condition.AfterSequencePosition.Value}");
                    }
                }
                else
                {
                    // No query specified - ANY new events cause failure
                    throw new ConcurrencyException(
                        $"Expected sequence position {condition.AfterSequencePosition.Value}, but current position is {currentPosition}");
                }
            }
        }
        else
        {
            // AfterSequencePosition not provided - check all matching events
            if (condition.FailIfEventsMatch != null && condition.FailIfEventsMatch.QueryItems.Count > 0)
            {
                var matchingPositions = await GetPositionsForQueryAsync(contextPath, condition.FailIfEventsMatch).ConfigureAwait(false);

                if (matchingPositions.Length > 0)
                {
                    throw new ConcurrencyException(
                        $"Append condition failed: found {matchingPositions.Length} matching event(s)");
                }
            }
        }
    }

    /// <summary>
    /// Gets the full context path for a context name.
    /// </summary>
    private string GetContextPath(string contextName)
    {
        return Path.Combine(_options.RootPath, contextName);
    }

    /// <summary>
    /// Gets the events directory path for a context.
    /// </summary>
    private static string GetEventsPath(string contextPath)
    {
        return Path.Combine(contextPath, "events");
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Error appending events to context '{Context}'")]
    private partial void LogAppendError(Exception ex, string context);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error reading events from context '{Context}'")]
    private partial void LogReadError(Exception ex, string context);
}
