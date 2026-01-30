# ✅ FileSystemEventStore Implementation Checklist

Quick reference for implementing FileSystemEventStore phase by phase.

**Current Status**: Core Event Store FULLY FUNCTIONAL ✅ (374 tests passing)
- Read Operations: ✅ Complete (Phase 5)
- Write Operations: ✅ Complete (Phase 4)
- Remaining Phases: Optional enhancements (6-9)

---

## Phase 1: Foundation & Ledger (1-2h) ✅ COMPLETE

### Files to Create
- [x] `src/Opossum/Storage/FileSystem/LedgerManager.cs`
- [x] `tests/Opossum.UnitTests/Storage/FileSystem/LedgerManagerTests.cs`

### Implementation Tasks
- [x] LedgerManager.GetNextSequencePositionAsync()
- [x] LedgerManager.GetLastSequencePositionAsync()
- [x] LedgerManager.UpdateSequencePositionAsync()
- [x] File locking mechanism
- [x] Concurrent access handling

### Tests (22 ✅)
- [x] Read ledger when file doesn't exist
- [x] Read ledger with existing data
- [x] Write new sequence position
- [x] Concurrent access
- [x] File locking
- [x] Ledger corruption recovery
- [x] All 22 tests passing!

---

## Phase 2: Event Serialization (2-3h) ✅ COMPLETE

### Files to Create
- [x] `src/Opossum/Storage/FileSystem/EventFileManager.cs`
- [x] `src/Opossum/Storage/FileSystem/JsonEventSerializer.cs`
- [x] `tests/Opossum.UnitTests/Storage/FileSystem/EventFileManagerTests.cs`
- [x] `tests/Opossum.UnitTests/Storage/FileSystem/JsonEventSerializerTests.cs`

### Implementation Tasks
- [x] JsonEventSerializer.Serialize()
- [x] JsonEventSerializer.Deserialize()
- [x] EventFileManager.WriteEventAsync()
- [x] EventFileManager.ReadEventAsync()
- [x] EventFileManager.ReadEventsAsync()
- [x] EventFileManager.GetEventFilePath()
- [x] Zero-padded file naming (0000000001.json)
- [x] Polymorphic IEvent handling

### Tests (49 ✅)
- [x] Serialize SequencedEvent
- [x] Deserialize SequencedEvent
- [x] Polymorphic events (TestDomainEvent, AnotherTestEvent, etc.)
- [x] Write event to file
- [x] Read event from file
- [x] Read multiple events (batch)
- [x] File naming validation
- [x] Missing file handling
- [x] File existence checks
- [x] Atomic write operations
- [x] Round-trip preservation
- [x] All 49 tests passing!

---

## Phase 3: Index Management (2-3h) ✅ COMPLETE

### Files to Create
- [x] `src/Opossum/Storage/FileSystem/IndexManager.cs`
- [x] `src/Opossum/Storage/FileSystem/EventTypeIndex.cs`
- [x] `src/Opossum/Storage/FileSystem/TagIndex.cs`
- [x] `tests/Opossum.UnitTests/Storage/FileSystem/IndexManagerTests.cs`
- [x] `tests/Opossum.UnitTests/Storage/FileSystem/EventTypeIndexTests.cs`
- [x] `tests/Opossum.UnitTests/Storage/FileSystem/TagIndexTests.cs`

### Implementation Tasks
- [x] IndexManager.AddEventToIndicesAsync()
- [x] IndexManager.GetPositionsByEventTypeAsync()
- [x] IndexManager.GetPositionsByEventTypesAsync()
- [x] IndexManager.GetPositionsByTagAsync()
- [x] IndexManager.GetPositionsByTagsAsync()
- [x] EventTypeIndex.AddPositionAsync()
- [x] EventTypeIndex.GetPositionsAsync()
- [x] EventTypeIndex.IndexExists()
- [x] TagIndex.AddPositionAsync()
- [x] TagIndex.GetPositionsAsync()
- [x] TagIndex.IndexExists()
- [x] Safe file naming for special characters
- [x] Corruption recovery
- [x] Union queries (multiple types/tags)

### Tests (89 ✅)
- [x] Add position to EventType index
- [x] Read positions from EventType index
- [x] Add position to Tag index
- [x] Read positions from Tag index
- [x] Multiple event types (union queries)
- [x] Multiple tags (union queries)
- [x] Index file creation
- [x] Index updates (append)
- [x] Sequential index updates
- [x] Index corruption handling
- [x] Special character handling
- [x] Null value handling
- [x] Sorted results
- [x] Deduplication
- [x] All 89 tests passing!

---

## Phase 4: AppendAsync (1-2h) ✅ COMPLETE

### Files to Modify
- [x] `src/Opossum/Storage/FileSystem/FileSystemEventStore.cs`

### Files to Create
- [x] `tests/Opossum.UnitTests/Storage/FileSystem/FileSystemEventStoreTests.cs`

### Implementation Tasks
- [x] Validate events (null check, empty check, event property validation)
- [x] Get context path (from OpossumOptions)
- [x] Acquire append lock (SemaphoreSlim for concurrency control)
- [x] Check AppendCondition (AfterSequencePosition + FailIfEventsMatch)
- [x] Allocate sequence positions (via LedgerManager)
- [x] Assign positions to events + set timestamps
- [x] Write events to files (via EventFileManager)
- [x] Update indices (via IndexManager)
- [x] Update ledger (via LedgerManager)
- [x] Error handling (ConcurrencyException, InvalidOperationException)
- [x] Dependency injection support for testing

### Tests (23 ✅)
- [x] Append single event
- [x] Append multiple events
- [x] Sequence positions assigned correctly
- [x] Events written to files
- [x] Indices updated (EventType + Tags)
- [x] Ledger updated
- [x] Timestamp auto-generation
- [x] Timestamp preservation
- [x] Null events throws
- [x] Empty array throws
- [x] Null Event property throws
- [x] Empty EventType throws
- [x] No contexts configured throws
- [x] Sequential appends maintain sequence
- [x] Large batch position assignment
- [x] AppendCondition AfterSequencePosition success
- [x] AppendCondition AfterSequencePosition failure
- [x] AppendCondition FailIfEventsMatch success (no conflict)
- [x] AppendCondition FailIfEventsMatch failure (conflict)
- [x] AppendCondition with tags (no conflict)
- [x] AppendCondition with tags (conflict)
- [x] Both conditions success
- [x] Integration test (complete workflow)
- [x] All 23 tests passing!

---

## Phase 5: ReadAsync & Query Filtering (1.5h) ✅ COMPLETE

### Files Modified
- [x] `src/Opossum/Storage/FileSystem/FileSystemEventStore.cs`
- [x] `tests/Opossum.UnitTests/Storage/FileSystem/FileSystemEventStoreReadTests.cs` (created)

### Implementation Tasks
- [x] ReadAsync() - main method with validation and ReadOption support
- [x] GetPositionsForQueryAsync() - main query entry point
- [x] GetPositionsForQueryItemAsync() - per QueryItem logic
- [x] GetAllPositionsAsync() - Query.All() support
- [x] EventType filtering (OR logic)
- [x] Tag filtering (AND logic)
- [x] Combine EventTypes + Tags (AND)
- [x] Combine QueryItems (OR)
- [x] Handle Query.All()
- [x] Apply ReadOption.Descending

### Tests (21) - All Passing ✅
- [x] ReadAsync_WithQueryAll_ReturnsAllEvents
- [x] ReadAsync_WithQueryAll_ReturnsInAscendingOrder
- [x] ReadAsync_WithNoEvents_ReturnsEmptyArray
- [x] ReadAsync_WithNullQuery_ThrowsArgumentNullException
- [x] ReadAsync_WithSingleEventType_ReturnsMatchingEvents
- [x] ReadAsync_WithMultipleEventTypes_ReturnsUnionOfMatches
- [x] ReadAsync_WithNonExistentEventType_ReturnsEmpty
- [x] ReadAsync_WithSingleTag_ReturnsMatchingEvents
- [x] ReadAsync_WithMultipleTags_ReturnsIntersectionOfMatches
- [x] ReadAsync_WithNonExistentTag_ReturnsEmpty
- [x] ReadAsync_WithEventTypesAndTags_ReturnsIntersection
- [x] ReadAsync_WithMultipleQueryItems_ReturnsUnion
- [x] ReadAsync_WithComplexQuery_ReturnsCorrectMatches
- [x] ReadAsync_WithDescendingOption_ReturnsReversedOrder
- [x] ReadAsync_WithDescendingAndFilter_ReturnsFilteredDescending
- [x] ReadAsync_WithNullOptions_UsesDefaultAscending
- [x] ReadAsync_WithEmptyOptions_UsesDefaultAscending
- [x] ReadAsync_PreservesEventData
- [x] ReadAsync_PreservesMetadata
- [x] Integration_AppendAndRead_CompleteWorkflow
- [x] ReadAsync_WithNonExistentEventType_ReturnsEmpty

**Key Features Delivered**:
- Complete query filtering with OR/AND logic combinations
- Query.All() support for retrieving all events
- ReadOption.Descending for reverse chronological order
- Unified query logic used by both ReadAsync and AppendCondition
- Integration with all previous phases verified

**Completion Status**: ✅ All 21 tests passing! Core Event Store FULLY FUNCTIONAL (read + write operations complete)

---

## Phase 6: DCB Query Helpers (OPTIONAL - 1-2h)

---

## Phase 7: AppendCondition Validation (1h)

### Files to Modify
- [ ] `src/Opossum/Storage/FileSystem/FileSystemEventStore.cs`

### Implementation Tasks
- [ ] ValidateAppendConditionAsync()
- [ ] Check AfterSequencePosition
- [ ] Check FailIfEventsMatch query
- [ ] Throw AppendConditionFailedException

### Tests (10)
- [ ] Valid AfterSequencePosition
- [ ] Invalid AfterSequencePosition throws
- [ ] FailIfEventsMatch no matches
- [ ] FailIfEventsMatch with matches throws
- [ ] Both conditions valid
- [ ] Both conditions invalid

---

## Phase 8: Error Handling (1h)

### Files to Modify
- [ ] Multiple files - add error handling

### Implementation Tasks
- [ ] Handle missing context
- [ ] Handle corrupt files
- [ ] Handle disk full
- [ ] Add logging
- [ ] Implement retry logic

### Tests (10)
- [ ] Missing context throws ContextNotFoundException
- [ ] Corrupt event file
- [ ] Corrupt index file
- [ ] Disk full
- [ ] Concurrent stress test

---

## Phase 9: Integration Testing (1-2h)

### Files to Create/Modify
- [ ] Integration tests with real file system

### Tasks
- [ ] Run ExampleTest
- [ ] Verify DCB pattern
- [ ] Test with real file system
- [ ] Performance testing
- [ ] Cleanup test files

### Tests (10)
- [ ] Full append → read workflow
- [ ] Multiple contexts
- [ ] Concurrent operations
- [ ] Large event sets (1000+)
- [ ] Complex queries with DCB

---

## 🎯 Final Verification

- [ ] All 140 unit tests passing
- [ ] All 10 integration tests passing
- [ ] ExampleTest GREEN ✅
- [ ] Build succeeds with no warnings
- [ ] Code coverage > 80%
- [ ] Performance benchmarks met
- [ ] Documentation updated

---

## 📊 Progress Tracking

**Current Phase**: ___________  
**Tests Passing**: ___ / 150  
**Time Invested**: ___ hours  
**Estimated Remaining**: ___ hours  

**Blockers**:
- 
- 

**Notes**:
- 
- 

---

**Start Date**: ___________  
**Target Completion**: ___________  
**Actual Completion**: ___________
