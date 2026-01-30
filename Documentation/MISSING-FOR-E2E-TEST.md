# 🎯 Missing Components for End-to-End Integration Test

**Purpose**: This document lists all components that must be implemented in the Opossum core library to make the `ExampleTest` integration test fully executable.

**Test Location**: `tests/Opossum.IntegrationTests/ExampleTest.cs`

**Architecture Pattern**: Dynamic Consistency Boundaries (DCB)

**Last Updated**: December 2024

---

## 🔍 What is DCB (Dynamic Consistency Boundaries)?

**DCB = Dynamic Consistency Boundaries** - An event sourcing pattern that differs from traditional approaches:

### Traditional Event Sourcing
- Events append to ONE stream (e.g., `Course-{id}` stream)
- Load ALL events for ONE big aggregate (entire Course aggregate)
- Every operation loads the complete aggregate state

### Dynamic Consistency Boundaries (DCB)
- Load **small, purpose-built aggregates** tailored to each specific decision
- Aggregate contains **ONLY** the information needed for that particular state change
- Better decoupling in vertical slices
- More focused, performant queries

### Example: CourseEnlistmentAggregate

**Traditional approach would load:**
- ❌ ALL Course events (course name, description, instructor changes, schedule updates, etc.)
- ❌ ALL Student events (profile updates, grade changes, contact info, etc.)
- ❌ Hundreds of irrelevant events

**DCB approach loads:**
- ✅ **Only** enrollment/unenrollment events for THIS course
- ✅ **Only** enrollment/unenrollment events for THIS student
- ✅ Creates a **dynamic consistency boundary** specific to the enrollment decision
- ✅ Minimal, focused aggregate with just the data needed

This is enabled by the tag-based query system in Opossum, which allows filtering events by multiple dimensions (tags + event types).

---

## 📋 Executive Summary

To make the integration test work, we need to implement **5 critical components**:

| Component | Estimated Time | Priority | Status |
|-----------|---------------|----------|--------|
| 1. FileSystemEventStore | 8-12 hours | 🔴 CRITICAL | ❌ Not Started |
| 2. Mediator Implementation | 2-3 hours | 🔴 CRITICAL | ❌ Not Started |
| 3. Command Handlers | 1-2 hours | 🔴 CRITICAL | ❌ Not Started |
| 4. EventStore Helper Extensions | 30-45 min | 🟡 IMPORTANT | ❌ Not Started |
| 5. OpossumFixture Updates | 15-30 min | 🟡 IMPORTANT | ❌ Not Started |

**Total Estimated Time**: 12-18 hours

**Key Innovation**: The test demonstrates Dynamic Consistency Boundaries - loading small, purpose-built aggregates instead of monolithic aggregate streams.

---

## 🔴 CRITICAL PRIORITY

### 1. FileSystemEventStore Implementation ⚠️ BLOCKING

**File**: `src/Opossum/Storage/FileSystem/FileSystemEventStore.cs`

**Current State**: Only interface stub exists

**What's Needed**:
```csharp
internal class FileSystemEventStore : IEventStore
{
    // ❌ NOT IMPLEMENTED
    public Task AppendAsync(SequencedEvent[] events, AppendCondition? condition)
    {
        throw new NotImplementedException();
    }    

    // ❌ NOT IMPLEMENTED
    public Task<SequencedEvent[]> ReadAsync(Query query, ReadOption[]? readOptions)
    {
        throw new NotImplementedException();
    }
}
```

**Required Functionality**:

#### AppendAsync Implementation
- ✅ Accept array of `SequencedEvent` objects
- ❌ Validate events (non-null, valid structure)
- ❌ Check `AppendCondition` if provided (optimistic concurrency)
- ❌ Assign sequence positions (monotonically increasing)
- ❌ Write events to file system (JSON format)
- ❌ Update ledger (event catalog/index)
- ❌ Handle concurrency (atomic operations)
- ❌ Throw `AppendConditionFailedException` on condition failure
- ❌ Throw `ConcurrencyException` on conflicts
- ❌ Ensure ACID properties (atomicity, consistency)

#### ReadAsync Implementation
- ✅ Accept `Query` object with filtering criteria
- ❌ Parse `QueryItem` filters (EventTypes + Tags, OR logic between items)
- ❌ Filter events by EventType (OR logic within QueryItem)
- ❌ Filter events by Tags (AND logic within QueryItem)
- ❌ Apply `ReadOption.Descending` if specified
- ❌ Read from file system (deserialize JSON)
- ❌ Return `SequencedEvent[]` ordered by position
- ❌ Handle empty results gracefully
- ❌ Throw `InvalidQueryException` for malformed queries

**Dependencies**:
- StorageInitializer ✅ (COMPLETE)
- OpossumOptions ✅ (COMPLETE)
- Custom Exceptions ✅ (COMPLETE)
- Query/QueryItem classes ✅ (COMPLETE)
- ReadOption enum ✅ (COMPLETE)

**Estimated Time**: 8-12 hours

**Test Coverage Needed**:
- Unit tests for AppendAsync (~20 tests)
- Unit tests for ReadAsync (~25 tests)
- Integration tests with file system (~15 tests)
- Concurrency tests (~10 tests)
- Total: ~70 tests

---

### 2. Mediator Implementation ⚠️ BLOCKING

**File**: `src/Opossum/Mediator/Mediator.cs` (new file)

**Current State**: Only `IMediator` interface exists

**What's Needed**:
```csharp
public class Mediator : IMediator
{
    // ❌ NOT IMPLEMENTED
    public Task<T> InvokeAsync<T>(
        object message, 
        CancellationToken cancellation = default, 
        TimeSpan? timeout = default)
    {
        throw new NotImplementedException();
    }
}
```

**Required Functionality**:
- ❌ Handler registration system (map message types to handlers)
- ❌ Handler discovery (via DI or manual registration)
- ❌ Handler invocation (dynamic dispatch based on message type)
- ❌ Generic response handling (`Task<T>` return type)
- ❌ Cancellation token support
- ❌ Timeout support
- ❌ Error handling (handler not found, handler exceptions)
- ❌ Logging/diagnostics integration

**Handler Interface Needed**:
```csharp
// ❌ NOT IMPLEMENTED
public interface ICommandHandler<in TCommand, TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
}
```

**Dependencies**:
- Microsoft.Extensions.DependencyInjection ✅ (Available)
- Custom Exceptions ✅ (May need MediatorException)

**Estimated Time**: 2-3 hours

**Test Coverage Needed**:
- Unit tests for handler registration (~10 tests)
- Unit tests for handler invocation (~15 tests)
- Unit tests for error scenarios (~10 tests)
- Total: ~35 tests

---

### 3. Command Handlers ⚠️ BLOCKING

**Files**: 
- `tests/Opossum.IntegrationTests/Handlers/CreateCourseCommandHandler.cs` (new)
- `tests/Opossum.IntegrationTests/Handlers/EnrollStudentToCourseCommandHandler.cs` (new)

**What's Needed**:

#### CreateCourseCommandHandler
```csharp
// ❌ NOT IMPLEMENTED
public class CreateCourseCommandHandler 
    : ICommandHandler<CreateCourseCommand, CommandResult>
{
    private readonly IEventStore _eventStore;

    public async Task<CommandResult> HandleAsync(
        CreateCourseCommand command, 
        CancellationToken cancellationToken)
    {
        // Create CourseCreated event
        // Append to event store with proper tags
        // Return success/failure
    }
}
```

#### EnrollStudentToCourseCommandHandler
```csharp
// ❌ NOT IMPLEMENTED
public class EnrollStudentToCourseCommandHandler 
    : ICommandHandler<EnrollStudentToCourseCommand, CommandResult>
{
    private readonly IEventStore _eventStore;

    public async Task<CommandResult> HandleAsync(
        EnrollStudentToCourseCommand command, 
        CancellationToken cancellationToken)
    {
        // 1. Build query for course events
        // 2. Load events from event store
        // 3. Build CourseEnlistmentAggregate from events
        // 4. Validate invariant: CanEnrollStudent()
        // 5. If valid: Create StudentEnrolledToCourseEvent
        // 6. Append event with proper tags (courseId, studentId)
        // 7. Return success/failure with error message
    }
}
```

**Required Functionality**:
- ❌ Query construction for aggregate loading
- ❌ Event store interaction (read + append)
- ❌ Aggregate building from events (fold/reduce pattern)
- ❌ Business rule validation
- ❌ Event creation with proper metadata
- ❌ Tag assignment (courseId, studentId)
- ❌ Error handling and result mapping

**Dependencies**:
- IEventStore ✅ (Interface exists)
- FileSystemEventStore ❌ (NOT IMPLEMENTED)
- IMediator ❌ (NOT IMPLEMENTED)
- EventStore Helper Extensions ❌ (NOT IMPLEMENTED - but helpful)

**Estimated Time**: 1-2 hours

**Test Coverage Needed**:
- Unit tests for each handler (~10 tests each)
- Mock-based testing (using Moq)
- Total: ~20 tests

---

## 🟡 IMPORTANT PRIORITY

### 4. EventStore Helper Extensions (Aggregate Loading)

**File**: `src/Opossum/Extensions/EventStoreExtensions.cs` (enhance existing)

**Current State**: Basic convenience methods exist (AppendAsync/ReadAsync overloads)

**What's Needed**:

#### LoadAggregateAsync Extension
```csharp
// ❌ NOT IMPLEMENTED
public static async Task<TAggregate?> LoadAggregateAsync<TAggregate>(
    this IEventStore eventStore,
    Query query,
    Func<SequencedEvent[], TAggregate> builder)
{
    var events = await eventStore.ReadAsync(query);
    if (events.Length == 0)
        return default;
    
    return builder(events);
}
```

**Alternative Approach** (More Type-Safe):
```csharp
// ❌ NOT IMPLEMENTED
public static async Task<TAggregate> LoadAggregateAsync<TAggregate>(
    this IEventStore eventStore,
    Query query)
    where TAggregate : IAggregate, new()
{
    var events = await eventStore.ReadAsync(query);
    var aggregate = new TAggregate();
    
    foreach (var sequencedEvent in events)
    {
        aggregate.Apply(sequencedEvent.Event);
    }
    
    return aggregate;
}

// Requires:
public interface IAggregate
{
    void Apply(DomainEvent @event);
}
```

**Benefits**:
- Simplifies command handlers (one-liner aggregate loading)
- Encapsulates event folding/reducing pattern
- Type-safe aggregate construction
- Reusable across all command handlers

**Dependencies**:
- IEventStore ✅ (Interface exists)
- Query ✅ (COMPLETE)

**Estimated Time**: 30-45 minutes

**Test Coverage Needed**:
- Unit tests for aggregate loading (~8 tests)
- Mock-based testing
- Total: ~8 tests

---

### 5. OpossumFixture Updates (Mediator Registration)

**File**: `tests/Opossum.IntegrationTests/OpossumFixture.cs`

**Current State**: Provides `IEventStore` and `IMediator` but mediator is not functional

**What's Needed**:
```csharp
public class OpossumFixture : IAsyncLifetime
{
    public IMediator Mediator { get; private set; } = null!;
    public IEventStore EventStore { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // ... existing code ...

        // ❌ NEEDS UPDATE: Register command handlers
        services.AddScoped<ICommandHandler<CreateCourseCommand, CommandResult>, 
            CreateCourseCommandHandler>();
        services.AddScoped<ICommandHandler<EnrollStudentToCourseCommand, CommandResult>, 
            EnrollStudentToCourseCommandHandler>();

        // ❌ NEEDS UPDATE: Register mediator with handler discovery
        services.AddSingleton<IMediator, Mediator>();

        _serviceProvider = services.BuildServiceProvider();
        
        // ❌ NEEDS UPDATE: Get mediator from DI
        Mediator = _serviceProvider.GetRequiredService<IMediator>();
        EventStore = _serviceProvider.GetRequiredService<IEventStore>();
    }
}
```

**Required Functionality**:
- ❌ Register command handlers in DI
- ❌ Register Mediator implementation
- ❌ Ensure handlers are discoverable by mediator
- ❌ Update tests to verify mediator functionality

**Dependencies**:
- Mediator Implementation ❌ (NOT IMPLEMENTED)
- Command Handlers ❌ (NOT IMPLEMENTED)

**Estimated Time**: 15-30 minutes

---

## 📊 Implementation Roadmap

### Recommended Implementation Order

#### Phase A: Core Infrastructure (10-15 hours)
1. **FileSystemEventStore** (8-12 hours) - BLOCKING EVERYTHING
   - Start with AppendAsync (simpler - no query logic)
   - Then implement ReadAsync (complex query filtering)
   - Comprehensive tests at each step

2. **Mediator** (2-3 hours) - NEEDED FOR COMMAND HANDLING
   - Start with simple handler registration
   - Add handler invocation
   - Add error handling and diagnostics

#### Phase B: Application Layer (2-3 hours)
3. **Command Handlers** (1-2 hours) - BUSINESS LOGIC
   - CreateCourseCommandHandler (simpler)
   - EnrollStudentToCourseCommandHandler (more complex)

4. **EventStore Extensions** (30-45 min) - CONVENIENCE
   - LoadAggregateAsync helper
   - Simplifies command handlers

5. **OpossumFixture Updates** (15-30 min) - INTEGRATION
   - Wire everything together
   - Update test infrastructure

---

## 🧪 Test Scenarios Enabled by Implementation

Once all components are implemented, the `ExampleTest` will verify:

### Happy Path Scenarios ✅
1. **Create Course**
   - Command → Handler → Event Store
   - Event persisted with proper tags
   - Aggregate built from events

2. **Enroll Student**
   - Load aggregate from events
   - Validate business rule (capacity available)
   - Create and persist enrollment event
   - Verify event tags (courseId, studentId)
   - Rebuild aggregate with new event

3. **Query Filtering**
   - Query by courseId tag
   - Query by studentId tag
   - Query by event type
   - Combined queries (tags AND types)

### Error Scenarios ⚠️
4. **Course Capacity Exceeded**
   - Aggregate validation fails
   - Command returns failure
   - No event persisted
   - Error message provided

5. **Concurrent Enrollments** (Future)
   - OptimisticConcurrency with AppendCondition
   - Retry logic in handlers

---

## 🎯 Success Criteria

The implementation is complete when:

- ✅ `ExampleTest.EnrollStudentToCourse_ShouldCreateEventAndBuildAggregate()` passes
- ✅ `ExampleTest.EnrollStudentToCourse_WhenCourseIsFull_ShouldFail()` passes
- ✅ All new components have comprehensive unit tests (70+ new tests)
- ✅ Build succeeds with no warnings
- ✅ Code coverage > 80% for new components
- ✅ Documentation updated for new features

---

## 📝 Current Test Code Structure

The test demonstrates the full Dynamic Consistency Boundaries workflow:

```csharp
// 1. Query Construction - Define the consistency boundary (WORKS - Query class complete)
// DCB: Load ONLY enrollment-relevant events, not the entire Course or Student aggregate
var query = Query.FromItems(
    new QueryItem
    {
        Tags = [new Tag { Key = "courseId", Value = courseId.ToString() }],
        EventTypes = [nameof(StudentEnrolledToCourseEvent), nameof(StudentUnenrolledFromCourseEvent)]
    },
    new QueryItem
    {
        Tags = [new Tag { Key = "studentId", Value = studentId.ToString() }],
        EventTypes = [nameof(StudentEnrolledToCourseEvent), nameof(StudentUnenrolledFromCourseEvent)]
    }
);

// 2. Command Execution (NEEDS: Mediator + Handlers)
var result = await _mediator.InvokeAsync<CommandResult>(enrollCommand);

// 3. Event Retrieval - Fetch only events within the consistency boundary (NEEDS: FileSystemEventStore.ReadAsync)
var events = await _eventStore.ReadAsync(query);

// 4. Aggregate Building - Build purpose-specific aggregate (WORKS - Helper method in test)
// DCB: CourseEnlistmentAggregate is NOT a general Course or Student aggregate
// It's a focused decision model for enrollment validation
var aggregate = BuildAggregate(events);

// 5. Business Rule Validation (WORKS - Aggregate method)
Assert.True(aggregate.CanEnrollStudent());
```

**DCB Benefits Demonstrated**:
- 🎯 **Focused Queries**: Only load events relevant to enrollment decision
- 🚀 **Performance**: Avoid loading hundreds of irrelevant events
- 🔒 **Isolation**: Vertical slices are more decoupled
- 📊 **Clarity**: Aggregate purpose is explicit (CourseEnlistment, not generic Course)

**Status Breakdown**:
- ✅ Query Construction: **COMPLETE** (enables DCB)
- ❌ Command Execution: **NEEDS IMPLEMENTATION**
- ❌ Event Retrieval: **NEEDS IMPLEMENTATION**
- ✅ Aggregate Building: **WORKS (manual helper)**
- ✅ Business Rules: **WORKS**

---

## 🔧 Technical Considerations

### FileSystemEventStore Challenges
1. **Query Filtering**: Implement efficient tag + eventType filtering
2. **Concurrency**: File locking, atomic operations
3. **Performance**: Index management for fast queries
4. **Serialization**: JSON serialization of polymorphic events
5. **Error Handling**: Corrupt files, missing directories

### Mediator Challenges
1. **Handler Discovery**: Reflection vs manual registration
2. **Type Safety**: Generic handler invocation
3. **Performance**: Handler caching, minimal reflection overhead
4. **Diagnostics**: Logging, tracing, debugging support

### Testing Strategy
1. **Unit Tests**: Mock dependencies, test logic in isolation
2. **Integration Tests**: Real file system, real mediator
3. **End-to-End Tests**: Full workflow from command → event → aggregate
4. **Performance Tests**: Large event sets, concurrent operations

---

## 📚 Related Documentation

- **Implementation Ready**: `Documentation/implementation-ready.md`
- **Progress Tracking**: `Documentation/PROGRESS.md`
- **FileSystemEventStore Spec**: `Documentation/what-to-build-now.md` (Phase 3)
- **Completed Features**: `Documentation/implementation-status/*.md`

---

## 🎉 Benefits of This Test-Driven Approach

1. **Clear Target**: Test defines exact behavior needed
2. **Validation**: Immediate feedback when features work
3. **Documentation**: Test serves as usage example for DCB pattern
4. **Confidence**: Comprehensive coverage of critical path
5. **Regression Protection**: Guards against future breaks
6. **DCB Proof-of-Concept**: Demonstrates Dynamic Consistency Boundaries in practice

---

**Next Step**: Implement **FileSystemEventStore** (AppendAsync + ReadAsync) as it's the critical blocker for all other functionality.

The FileSystemEventStore must support the DCB pattern through efficient tag-based and event-type filtering, enabling focused queries for dynamic consistency boundaries.
