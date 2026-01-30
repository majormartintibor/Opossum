# 🎯 FileSystemEventStore Implementation Plan

**Component**: FileSystemEventStore (Core Event Persistence)  
**Priority**: 🔴 CRITICAL - Blocks all other functionality  
**Estimated Time**: 8-12 hours  
**Architecture**: Dynamic Consistency Boundaries (DCB)  
**Created**: December 2024

---

## 📋 Executive Summary

FileSystemEventStore is the **critical blocker** for the entire Opossum library. It implements the core event persistence and retrieval functionality using a file-based storage approach.

**Key Responsibilities**:
- ✅ Persist events to file system with JSON serialization
- ✅ Support tag-based + event-type filtering (DCB pattern)
- ✅ Maintain ledger for sequence positions
- ✅ Ensure concurrency control with optimistic locking
- ✅ Build and maintain indices for fast querying

**Test-Driven Approach**: Each phase includes comprehensive unit tests before moving to the next.

---

## 🏗️ Architecture Overview

### File System Structure (Already Initialized)
```
/OpossumStore
  /ContextName
    .ledger                    # Sequence position tracking
    /Events                    # Event files (one per event)
      /0000000001.json
      /0000000002.json
      /...
    /Indices
      /EventType               # EventType → Position mappings
        /CourseCreated.idx
        /StudentEnrolled.idx
      /Tags                    # Tag → Position mappings
        /courseId_{value}.idx
        /studentId_{value}.idx
```

### Core Data Structures

**Ledger Entry** (`.ledger` file):
```json
{
  "lastSequencePosition": 123,
  "eventCount": 123
}
```

**Event File** (`/Events/0000000001.json`):
```json
{
  "position": 1,
  "event": {
    "eventType": "StudentEnrolledToCourseEvent",
    "event": { "courseId": "...", "studentId": "..." },
    "tags": [
      { "key": "courseId", "value": "..." },
      { "key": "studentId", "value": "..." }
    ]
  },
  "metadata": {
    "timestamp": "2024-12-01T12:00:00Z",
    "correlationId": "..."
  }
}
```

**Index File** (`/Indices/Tags/courseId_{value}.idx`):
```json
{
  "positions": [1, 5, 12, 34]
}
```

---

## 📅 Implementation Phases

### Phase 1: Foundation & Ledger Management (1-2 hours)

**Goal**: Set up basic infrastructure and ledger operations

**Tasks**:
1. ✅ Add constructor with dependency injection
2. ✅ Implement ledger read/write operations
3. ✅ Implement sequence position allocation
4. ✅ Add file locking mechanism for concurrency

**Files to Create/Modify**:
- `src/Opossum/Storage/FileSystem/FileSystemEventStore.cs` (enhance)
- `src/Opossum/Storage/FileSystem/LedgerManager.cs` (new)
- `tests/Opossum.UnitTests/Storage/FileSystem/LedgerManagerTests.cs` (new)

**Key Classes**:

```csharp
internal class LedgerManager
{
    public Task<long> GetNextSequencePositionAsync(string contextPath);
    public Task<long> GetLastSequencePositionAsync(string contextPath);
    public Task UpdateSequencePositionAsync(string contextPath, long position);
}
```

**Tests** (~15 tests):
- ✅ Read ledger when file doesn't exist (returns 0)
- ✅ Read ledger with existing data
- ✅ Write new sequence position
- ✅ Concurrent access handling
- ✅ File locking behavior
- ✅ Ledger corruption recovery

**Success Criteria**:
- All ledger tests passing
- Thread-safe ledger operations
- Proper file locking

---

### Phase 2: Event Serialization & Storage (2-3 hours)

**Goal**: Implement event persistence to individual JSON files

**Tasks**:
1. ✅ Implement JSON serialization for SequencedEvent
2. ✅ Create event file naming strategy (zero-padded positions)
3. ✅ Implement event writing with atomic operations
4. ✅ Implement event reading from files
5. ✅ Handle polymorphic event deserialization

**Files to Create/Modify**:
- `src/Opossum/Storage/FileSystem/EventFileManager.cs` (new)
- `src/Opossum/Storage/FileSystem/JsonEventSerializer.cs` (new)
- `tests/Opossum.UnitTests/Storage/FileSystem/EventFileManagerTests.cs` (new)
- `tests/Opossum.UnitTests/Storage/FileSystem/JsonEventSerializerTests.cs` (new)

**Key Classes**:

```csharp
internal class EventFileManager
{
    Task WriteEventAsync(string eventsPath, SequencedEvent sequencedEvent);
    Task<SequencedEvent> ReadEventAsync(string eventsPath, long position);
    Task<SequencedEvent[]> ReadEventsAsync(string eventsPath, long[] positions);
    string GetEventFilePath(string eventsPath, long position);
}

internal class JsonEventSerializer
{
    string Serialize(SequencedEvent sequencedEvent);
    SequencedEvent Deserialize(string json);
}
```

**Tests** (~20 tests):
- ✅ Serialize SequencedEvent to JSON
- ✅ Deserialize JSON to SequencedEvent
- ✅ Handle polymorphic IEvent types
- ✅ Write event to file
- ✅ Read event from file
- ✅ File naming (0000000001.json format)
- ✅ Missing file handling
- ✅ Corrupt file handling
- ✅ Atomic write operations

**Success Criteria**:
- Events serialize/deserialize correctly
- Polymorphic events work (CourseCreated, StudentEnrolled, etc.)
- Files created with correct naming
- Atomic writes (no partial files)

---

### Phase 3: Index Management (2-3 hours)

**Goal**: Build and maintain indices for fast querying

**Tasks**:
1. ✅ Implement EventType index read/write
2. ✅ Implement Tag index read/write
3. ✅ Update indices during event append
4. ✅ Handle index corruption/rebuild

**Files to Create/Modify**:
- `src/Opossum/Storage/FileSystem/IndexManager.cs` (new)
- `src/Opossum/Storage/FileSystem/EventTypeIndex.cs` (new)
- `src/Opossum/Storage/FileSystem/TagIndex.cs` (new)
- `tests/Opossum.UnitTests/Storage/FileSystem/IndexManagerTests.cs` (new)

**Key Classes**:

```csharp
internal class IndexManager
{
    Task AddEventToIndicesAsync(string contextPath, SequencedEvent sequencedEvent);
    Task<long[]> GetPositionsByEventTypeAsync(string contextPath, string eventType);
    Task<long[]> GetPositionsByTagAsync(string contextPath, Tag tag);
    Task<long[]> GetPositionsByEventTypesAsync(string contextPath, string[] eventTypes);
    Task<long[]> GetPositionsByTagsAsync(string contextPath, Tag[] tags);
}

internal class EventTypeIndex
{
    Task AddPositionAsync(string indexPath, string eventType, long position);
    Task<long[]> GetPositionsAsync(string indexPath, string eventType);
}

internal class TagIndex
{
    Task AddPositionAsync(string indexPath, Tag tag, long position);
    Task<long[]> GetPositionsAsync(string indexPath, Tag tag);
}
```

**Index File Format**:
```json
{
  "positions": [1, 5, 12, 34, 56]
}
```

**Tests** (~25 tests):
- ✅ Add position to EventType index
- ✅ Read positions from EventType index
- ✅ Add position to Tag index
- ✅ Read positions from Tag index
- ✅ Multiple event types
- ✅ Multiple tags
- ✅ Index file creation
- ✅ Index file updates (append)
- ✅ Concurrent index updates
- ✅ Index corruption handling

**Success Criteria**:
- Indices updated on event append
- Fast position lookups by EventType
- Fast position lookups by Tag
- Thread-safe index operations

---

### Phase 4: AppendAsync Implementation (1-2 hours)

**Goal**: Implement full event appending with concurrency control

**Tasks**:
1. ✅ Implement AppendAsync method
2. ✅ Validate events
3. ✅ Check AppendCondition (if provided)
4. ✅ Allocate sequence positions
5. ✅ Write events to files
6. ✅ Update indices
7. ✅ Update ledger
8. ✅ Handle errors and rollback

**Implementation Strategy**:

```csharp
public async Task AppendAsync(SequencedEvent[] events, AppendCondition? condition)
{
    // 1. Validation
    ArgumentNullException.ThrowIfNull(events);
    if (events.Length == 0)
        throw new ArgumentException("Events array cannot be empty", nameof(events));
    
    // 2. Get context path (assume single context for now)
    var contextPath = GetContextPath();
    
    // 3. Lock ledger for atomic operation
    using (var ledgerLock = await AcquireLedgerLockAsync(contextPath))
    {
        // 4. Check AppendCondition
        if (condition != null)
        {
            await ValidateAppendConditionAsync(contextPath, condition);
        }
        
        // 5. Allocate sequence positions
        var startPosition = await _ledgerManager.GetNextSequencePositionAsync(contextPath);
        for (int i = 0; i < events.Length; i++)
        {
            events[i].Position = startPosition + i;
        }
        
        // 6. Write events to files
        foreach (var evt in events)
        {
            await _eventFileManager.WriteEventAsync(GetEventsPath(contextPath), evt);
        }
        
        // 7. Update indices
        foreach (var evt in events)
        {
            await _indexManager.AddEventToIndicesAsync(contextPath, evt);
        }
        
        // 8. Update ledger
        await _ledgerManager.UpdateSequencePositionAsync(contextPath, 
            startPosition + events.Length - 1);
    }
}
```

**Tests** (~20 tests):
- ✅ Append single event
- ✅ Append multiple events
- ✅ Sequence positions assigned correctly
- ✅ Events written to files
- ✅ Indices updated
- ✅ Ledger updated
- ✅ Null events throws
- ✅ Empty array throws
- ✅ AppendCondition success
- ✅ AppendCondition failure throws AppendConditionFailedException
- ✅ Concurrent appends handled correctly
- ✅ Rollback on failure

**Success Criteria**:
- Events persisted atomically
- Sequence positions monotonically increasing
- Indices and ledger consistent
- AppendCondition enforced

---

### Phase 5: Query Filtering Logic (1-2 hours)

**Goal**: Implement complex query filtering for DCB pattern

**Tasks**:
1. ✅ Implement QueryItem matching logic
2. ✅ Combine multiple QueryItems (OR logic)
3. ✅ Handle EventType filtering (OR within QueryItem)
4. ✅ Handle Tag filtering (AND within QueryItem)
5. ✅ Optimize position retrieval

**Key Algorithm**:

```csharp
private async Task<long[]> GetPositionsForQueryAsync(string contextPath, Query query)
{
    // Handle Query.All() - return all positions
    if (query.QueryItems.Count == 0)
    {
        return await GetAllPositionsAsync(contextPath);
    }
    
    var allPositions = new HashSet<long>();
    
    // OR logic between QueryItems
    foreach (var queryItem in query.QueryItems)
    {
        var positions = await GetPositionsForQueryItemAsync(contextPath, queryItem);
        allPositions.UnionWith(positions);
    }
    
    return allPositions.OrderBy(p => p).ToArray();
}

private async Task<long[]> GetPositionsForQueryItemAsync(string contextPath, QueryItem queryItem)
{
    HashSet<long>? eventTypePositions = null;
    HashSet<long>? tagPositions = null;
    
    // Get positions by EventType (OR logic)
    if (queryItem.EventTypes.Count > 0)
    {
        eventTypePositions = new HashSet<long>();
        foreach (var eventType in queryItem.EventTypes)
        {
            var positions = await _indexManager.GetPositionsByEventTypeAsync(contextPath, eventType);
            eventTypePositions.UnionWith(positions);
        }
    }
    
    // Get positions by Tags (AND logic)
    if (queryItem.Tags.Count > 0)
    {
        List<long[]> tagPositionSets = new();
        foreach (var tag in queryItem.Tags)
        {
            var positions = await _indexManager.GetPositionsByTagAsync(contextPath, tag);
            tagPositionSets.Add(positions);
        }
        
        // Intersect all tag positions (AND logic)
        tagPositions = new HashSet<long>(tagPositionSets[0]);
        for (int i = 1; i < tagPositionSets.Count; i++)
        {
            tagPositions.IntersectWith(tagPositionSets[i]);
        }
    }
    
    // Combine EventType and Tag results (AND logic)
    if (eventTypePositions != null && tagPositions != null)
    {
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
    
    return Array.Empty<long>();
}
```

**Tests** (~15 tests):
- ✅ Query with single EventType
- ✅ Query with multiple EventTypes (OR)
- ✅ Query with single Tag
- ✅ Query with multiple Tags (AND)
- ✅ Query with EventTypes AND Tags
- ✅ Query with multiple QueryItems (OR)
- ✅ Query.All() returns all events
- ✅ Empty query returns empty
- ✅ No matches returns empty

**Success Criteria**:
- Correct OR logic between QueryItems
- Correct OR logic for EventTypes within QueryItem
- Correct AND logic for Tags within QueryItem
- Correct intersection of EventTypes + Tags

---

### Phase 6: ReadAsync Implementation (1-2 hours)

**Goal**: Implement full event reading with filtering and ordering

**Tasks**:
1. ✅ Implement ReadAsync method
2. ✅ Apply query filtering
3. ✅ Retrieve events from files
4. ✅ Apply ReadOption (Descending)
5. ✅ Handle errors gracefully

**Implementation Strategy**:

```csharp
public async Task<SequencedEvent[]> ReadAsync(Query query, ReadOption[]? readOptions)
{
    ArgumentNullException.ThrowIfNull(query);
    
    // 1. Get context path
    var contextPath = GetContextPath();
    
    // 2. Get positions matching query
    var positions = await GetPositionsForQueryAsync(contextPath, query);
    
    if (positions.Length == 0)
        return Array.Empty<SequencedEvent>();
    
    // 3. Read events from files
    var eventsPath = GetEventsPath(contextPath);
    var events = await _eventFileManager.ReadEventsAsync(eventsPath, positions);
    
    // 4. Apply ReadOptions
    if (readOptions != null && readOptions.Contains(ReadOption.Descending))
    {
        Array.Reverse(events);
    }
    
    return events;
}
```

**Tests** (~15 tests):
- ✅ Read with Query.All()
- ✅ Read with EventType filter
- ✅ Read with Tag filter
- ✅ Read with complex query
- ✅ Read with Descending option
- ✅ Read with no options (default ascending)
- ✅ Read with empty result
- ✅ Read with missing files (graceful handling)
- ✅ Null query throws

**Success Criteria**:
- Events retrieved correctly
- Filtering works as expected
- Ordering works (ascending/descending)
- Empty results handled gracefully

---

### Phase 7: AppendCondition Validation (1 hour)

**Goal**: Implement optimistic concurrency control

**Tasks**:
1. ✅ Implement condition validation logic
2. ✅ Check AfterSequencePosition
3. ✅ Check FailIfEventsMatch query
4. ✅ Throw AppendConditionFailedException

**Implementation**:

```csharp
private async Task ValidateAppendConditionAsync(string contextPath, AppendCondition condition)
{
    // Check AfterSequencePosition
    if (condition.AfterSequencePosition.HasValue)
    {
        var lastPosition = await _ledgerManager.GetLastSequencePositionAsync(contextPath);
        if (lastPosition != condition.AfterSequencePosition.Value)
        {
            throw new AppendConditionFailedException(
                $"Expected position {condition.AfterSequencePosition.Value}, but found {lastPosition}",
                expectedPosition: condition.AfterSequencePosition.Value,
                actualPosition: lastPosition);
        }
    }
    
    // Check FailIfEventsMatch
    var matchingEvents = await ReadAsync(condition.FailIfEventsMatch, null);
    if (matchingEvents.Length > 0)
    {
        throw new AppendConditionFailedException(
            "Events matching the fail condition were found",
            expectedPosition: null,
            actualPosition: null);
    }
}
```

**Tests** (~10 tests):
- ✅ Valid AfterSequencePosition succeeds
- ✅ Invalid AfterSequencePosition throws
- ✅ FailIfEventsMatch with no matches succeeds
- ✅ FailIfEventsMatch with matches throws
- ✅ Both conditions valid succeeds
- ✅ Both conditions invalid throws

**Success Criteria**:
- Optimistic concurrency working
- AppendConditionFailedException thrown correctly
- Exception includes expected/actual positions

---

### Phase 8: Error Handling & Edge Cases (1 hour)

**Goal**: Robust error handling and edge case coverage

**Tasks**:
1. ✅ Handle missing context
2. ✅ Handle corrupt files
3. ✅ Handle disk full scenarios
4. ✅ Add comprehensive logging
5. ✅ Implement retry logic where appropriate

**Tests** (~10 tests):
- ✅ Missing context directory throws ContextNotFoundException
- ✅ Corrupt event file throws InvalidQueryException
- ✅ Corrupt index file recovers gracefully
- ✅ Disk full handling
- ✅ Concurrent access stress test

**Success Criteria**:
- All error scenarios handled
- Appropriate exceptions thrown
- Logging in place
- No data corruption on errors

---

### Phase 9: Integration Testing (1-2 hours)

**Goal**: Validate end-to-end functionality with integration tests

**Tasks**:
1. ✅ Run ExampleTest integration test
2. ✅ Verify DCB pattern works
3. ✅ Test with real file system
4. ✅ Performance testing
5. ✅ Cleanup test files

**Integration Tests** (~10 tests):
- ✅ Full append → read workflow
- ✅ Multiple contexts
- ✅ Concurrent operations
- ✅ Large event sets (1000+ events)
- ✅ Complex queries with DCB pattern

**Success Criteria**:
- ExampleTest passes ✅
- All integration tests pass
- Performance acceptable (<100ms for simple queries)
- No file leaks

---

## 📊 Test Coverage Summary

| Phase | Component | Unit Tests | Integration Tests | Total |
|-------|-----------|------------|-------------------|-------|
| 1 | LedgerManager | 15 | - | 15 |
| 2 | EventFileManager | 10 | - | 10 |
| 2 | JsonEventSerializer | 10 | - | 10 |
| 3 | IndexManager | 15 | - | 15 |
| 3 | EventTypeIndex | 5 | - | 5 |
| 3 | TagIndex | 5 | - | 5 |
| 4 | AppendAsync | 20 | - | 20 |
| 5 | Query Filtering | 15 | - | 15 |
| 6 | ReadAsync | 15 | - | 15 |
| 7 | AppendCondition | 10 | - | 10 |
| 8 | Error Handling | 10 | - | 10 |
| 9 | End-to-End | - | 10 | 10 |
| **TOTAL** | | **130** | **10** | **140** |

**Total Estimated Tests**: 140 tests

---

## 🎯 Definition of Done

**Phase Complete When**:
- ✅ All unit tests passing
- ✅ Code coverage > 80%
- ✅ No compiler warnings
- ✅ Code reviewed (if applicable)
- ✅ Documentation updated

**Entire Implementation Complete When**:
- ✅ All 140 tests passing
- ✅ ExampleTest integration test passing
- ✅ Build succeeds with no warnings
- ✅ Performance benchmarks met
- ✅ Documentation complete
- ✅ Ready for production use

---

## 🚀 Implementation Order

**Recommended sequence** (dependencies flow downward):

```
Phase 1: Foundation & Ledger
    ↓
Phase 2: Event Serialization
    ↓
Phase 3: Index Management
    ↓
Phase 4: AppendAsync ← (Uses 1, 2, 3)
    ↓
Phase 5: Query Filtering ← (Uses 3)
    ↓
Phase 6: ReadAsync ← (Uses 2, 5)
    ↓
Phase 7: AppendCondition ← (Uses 6)
    ↓
Phase 8: Error Handling
    ↓
Phase 9: Integration Testing
```

**Critical Path**: Phases 1-4 are blockers for AppendAsync  
**Parallel Work Possible**: Phases 2 & 3 can be done concurrently

---

## 💡 Technical Considerations

### Concurrency Strategy
- **File Locking**: Use `FileStream` with `FileShare.None` for exclusive access
- **Ledger Lock**: Separate lock object for ledger operations
- **Index Updates**: Atomic operations with retry logic

### Performance Optimizations
- **Index Caching**: Cache frequently used indices in memory
- **Batch Operations**: Support batch event appends
- **Position Sorting**: Efficient sorting for large position sets

### Error Recovery
- **Partial Writes**: Detect and clean up partial writes
- **Index Rebuild**: Ability to rebuild indices from events
- **Corruption Detection**: Checksums for event files

---

## 📝 Dependencies

### Existing (Already Complete)
- ✅ StorageInitializer (directory structure)
- ✅ OpossumOptions (configuration)
- ✅ Custom Exceptions (error handling)
- ✅ Query/QueryItem classes (filtering)
- ✅ ReadOption enum (ordering)
- ✅ Core types (SequencedEvent, DomainEvent, Tag)

### New (To Be Created)
- ❌ LedgerManager
- ❌ EventFileManager
- ❌ JsonEventSerializer
- ❌ IndexManager
- ❌ EventTypeIndex
- ❌ TagIndex

### External NuGet Packages Needed
- ✅ System.Text.Json (built-in .NET)
- ❌ Possibly: Polly (for retry logic) - can defer

---

## 📅 Estimated Timeline

| Phase | Time Estimate | Cumulative |
|-------|--------------|------------|
| Phase 1 | 1-2 hours | 2h |
| Phase 2 | 2-3 hours | 5h |
| Phase 3 | 2-3 hours | 8h |
| Phase 4 | 1-2 hours | 10h |
| Phase 5 | 1-2 hours | 12h |
| Phase 6 | 1-2 hours | 14h |
| Phase 7 | 1 hour | 15h |
| Phase 8 | 1 hour | 16h |
| Phase 9 | 1-2 hours | 18h |

**Total**: 12-18 hours (as originally estimated)

**With breaks and debugging**: 2-3 days of focused work

---

## 🎉 Success Metrics

**When FileSystemEventStore is complete**:
- ✅ 140+ tests passing
- ✅ ExampleTest integration test ✅ GREEN
- ✅ DCB pattern fully functional
- ✅ Events persist correctly to file system
- ✅ Complex queries work (tags + event types)
- ✅ Concurrency handled correctly
- ✅ Error scenarios covered
- ✅ Performance acceptable
- ✅ Ready for sample application development

---

## 📚 Next Steps After FileSystemEventStore

Once complete, proceed to:
1. **Mediator Implementation** (2-3 hours)
2. **Command Handlers** (1-2 hours)
3. **EventStore Extensions** (LoadAggregateAsync helper)
4. **OpossumFixture Updates** (wire everything together)

**Total to fully functional library**: ~18-25 hours

---

## 🔗 References

- **Missing Components Doc**: `Documentation/MISSING-FOR-E2E-TEST.md`
- **Integration Test**: `tests/Opossum.IntegrationTests/ExampleTest.cs`
- **Current Implementation**: `src/Opossum/Storage/FileSystem/FileSystemEventStore.cs`
- **Storage Structure**: `src/Opossum/Storage/FileSystem/StorageInitializer.cs`

---

**Ready to begin Phase 1: Foundation & Ledger Management** 🚀
