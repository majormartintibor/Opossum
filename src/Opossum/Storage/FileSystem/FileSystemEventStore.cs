using Opossum.Core;
using Opossum.Configuration;
using Opossum.Exceptions;

namespace Opossum.Storage.FileSystem;

internal class FileSystemEventStore : IEventStore
{
    private readonly OpossumOptions _options;
    private readonly LedgerManager _ledgerManager;
    private readonly EventFileManager _eventFileManager;
    private readonly IndexManager _indexManager;
    private readonly JsonEventSerializer _serializer;
    private readonly SemaphoreSlim _appendLock = new(1, 1);

    public FileSystemEventStore(OpossumOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _ledgerManager = new LedgerManager(options.FlushEventsImmediately);
        _eventFileManager = new EventFileManager(options.FlushEventsImmediately);
        _indexManager = new IndexManager();
        _serializer = new JsonEventSerializer();
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
        _ledgerManager = ledgerManager;
        _eventFileManager = eventFileManager;
        _indexManager = indexManager;
        _serializer = new JsonEventSerializer();
    }

    public async Task AppendAsync(SequencedEvent[] events, AppendCondition? condition)
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

        // 2. Get context path (use first context, or throw if none configured)
        if (_options.Contexts.Count == 0)
        {
            throw new InvalidOperationException("No contexts configured. Add at least one context using OpossumOptions.AddContext()");
        }

        var contextPath = GetContextPath(_options.Contexts[0]);

        // 3. Use semaphore for atomic operation (one append at a time)
        await _appendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // 4. Check AppendCondition
            if (condition != null)
            {
                await ValidateAppendConditionAsync(contextPath, condition).ConfigureAwait(false);
            }

            // 5. Allocate sequence positions
            var startPosition = await _ledgerManager.GetNextSequencePositionAsync(contextPath).ConfigureAwait(false);

            for (int i = 0; i < events.Length; i++)
            {
                events[i].Position = startPosition + i;

                // Set metadata timestamp if not already set
                if (events[i].Metadata.Timestamp == default)
                {
                    events[i].Metadata.Timestamp = DateTimeOffset.UtcNow;
                }
            }

            // 6. Write events to files
            var eventsPath = GetEventsPath(contextPath);
            foreach (var evt in events)
            {
                await _eventFileManager.WriteEventAsync(eventsPath, evt).ConfigureAwait(false);
            }

            // 7. Update indices
            foreach (var evt in events)
            {
                await _indexManager.AddEventToIndicesAsync(contextPath, evt).ConfigureAwait(false);
            }

            // 8. Update ledger
            var lastPosition = startPosition + events.Length - 1;
            await _ledgerManager.UpdateSequencePositionAsync(contextPath, lastPosition).ConfigureAwait(false);
        }
        finally
        {
            _appendLock.Release();
        }
    }

    public async Task<SequencedEvent[]> ReadAsync(Query query, ReadOption[]? readOptions)
    {
        // 1. Validation
        ArgumentNullException.ThrowIfNull(query);

        // 2. Get context path (use first context, or throw if none configured)
        if (_options.Contexts.Count == 0)
        {
            throw new InvalidOperationException("No contexts configured. Add at least one context using OpossumOptions.AddContext()");
        }

        var contextPath = GetContextPath(_options.Contexts[0]);

        // 3. Get positions matching query
        var positions = await GetPositionsForQueryAsync(contextPath, query).ConfigureAwait(false);

        if (positions.Length == 0)
        {
            return [];
        }

        // 4. Read events from files
        var eventsPath = GetEventsPath(contextPath);
        var events = await _eventFileManager.ReadEventsAsync(eventsPath, positions).ConfigureAwait(false);

        // 5. Apply ReadOptions
        if (readOptions != null && readOptions.Contains(ReadOption.Descending))
        {
            Array.Reverse(events);
        }

        return events;
    }

    /// <summary>
    /// Gets all positions for a query.
    /// Implements full query logic with OR between QueryItems and proper AND/OR within items.
    /// </summary>
    private async Task<long[]> GetPositionsForQueryAsync(string contextPath, Query query)
    {
        // Handle Query.All() - return all positions from ledger
        if (query.QueryItems.Count == 0)
        {
            return await GetAllPositionsAsync(contextPath).ConfigureAwait(false);
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
                queryItem.EventTypes.ToArray()).ConfigureAwait(false);

            eventTypePositions = new HashSet<long>(typePositionsArray);
        }

        // Get positions by Tags (AND logic within Tags)
        if (queryItem.Tags != null && queryItem.Tags.Count > 0)
        {
            // For AND logic, we need to intersect all tag positions
            List<long[]> tagPositionSets = new();

            foreach (var tag in queryItem.Tags)
            {
                var positions = await _indexManager.GetPositionsByTagAsync(contextPath, tag).ConfigureAwait(false);
                tagPositionSets.Add(positions);
            }

            if (tagPositionSets.Count > 0)
            {
                // Start with first tag's positions
                tagPositions = new HashSet<long>(tagPositionSets[0]);

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
            return eventTypePositions.ToArray();
        }
        else if (eventTypePositions != null)
        {
            return eventTypePositions.ToArray();
        }
        else if (tagPositions != null)
        {
            return tagPositions.ToArray();
        }

        // No constraints specified in this QueryItem
        return [];
    }

    /// <summary>
    /// Gets all event positions in the context (for Query.All()).
    /// </summary>
    private async Task<long[]> GetAllPositionsAsync(string contextPath)
    {
        var lastPosition = await _ledgerManager.GetLastSequencePositionAsync(contextPath).ConfigureAwait(false);

        if (lastPosition == 0)
        {
            return [];
        }

        // Generate array of all positions from 1 to lastPosition
        var positions = new long[lastPosition];
        for (long i = 0; i < lastPosition; i++)
        {
            positions[i] = i + 1;
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
}
