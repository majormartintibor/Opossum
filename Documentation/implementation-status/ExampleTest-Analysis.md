# ExampleTest Integration Test Analysis

## Current Status: 4 Tests Failing ❌

**Error**: `No handler registered for message type CreateCourseCommand` and `EnrollStudentToCourseCommand`

---

## What We Have ✅

### 1. Infrastructure (Complete)
- ✅ **FileSystemEventStore** - Fully functional (374 tests passing)
  - AppendAsync with AppendCondition support
  - ReadAsync with complex query filtering
  - Query.All(), EventType filtering, Tag filtering
  - ReadOption.Descending support
  
- ✅ **Mediator** - Working message bus
  - Convention-based handler discovery
  - Dependency injection support
  - Timeout and cancellation support
  
- ✅ **OpossumFixture** - Test setup
  - Service provider configured
  - Mediator and EventStore injected
  - Unique temp storage per test run
  - Cleanup on dispose

### 2. Test Structure (Complete)
- ✅ **4 Integration Tests** defined in `ExampleTest.cs`:
  1. `EnrollStudentToCourse_ShouldCreateEventAndBuildAggregate` - Happy path
  2. `EnrollStudentToCourse_WhenCourseIsFull_ShouldFail` - Course capacity constraint
  3. `EnrollStudentToCourse_WhenStudentReachedLimit_ShouldFail` - Student limit constraint
  4. `EnrollStudentToCourse_MultipleStudents_ShouldTrackCorrectly` - Multiple students tracking

### 3. Domain Model (Complete)
- ✅ **Commands** defined:
  - `CreateCourseCommand(Guid CourseId, int MaxCapacity)`
  - `EnrollStudentToCourseCommand(Guid CourseId, Guid StudentId)`
  
- ✅ **Events** defined:
  - `CourseCreated(Guid CourseId, int MaxCapacity)`
  - `CourseCapacityUpdatedEvent(Guid CourseId, int NewCapacity)`
  - `StudentEnrolledToCourseEvent(Guid CourseId, Guid StudentId)`
  - `StudentUnenrolledFromCourseEvent(Guid CourseId, Guid StudentId)`
  
- ✅ **CommandResult** defined:
  - `CommandResult(bool Success, string? ErrorMessage = null)`

### 4. Aggregate (Complete)
- ✅ **CourseEnlistmentAggregate** - Fully implemented DCB aggregate
  - Tracks both course capacity AND student enrollment count
  - `Apply()` methods for all 4 event types
  - `CanEnrollStudent()` - Business rule validation
  - `GetEnrollmentFailureReason()` - Error messaging
  - Proper immutable record-based implementation
  
### 5. Test Helper (Complete)
- ✅ **BuildAggregate()** method - Event folding logic implemented

---

## What We're Missing ❌

### Command Handlers (Required)

We need to create **2 command handlers** to make the tests pass:

#### 1. CreateCourseCommandHandler ❌

**Purpose**: Create a new course with specified capacity

**What it needs to do**:
1. Create `CourseCreated` event with courseId and maxCapacity
2. Tag event with `courseId` tag
3. Append event to event store
4. Return `CommandResult(Success: true)`

**Method signature**:
```csharp
public async Task<CommandResult> HandleAsync(
    CreateCourseCommand command,
    IEventStore eventStore)
{
    // Implementation needed
}
```

**Event to append**:
```csharp
var @event = new CourseCreated(command.CourseId, command.MaxCapacity);
var tags = new[] { new Tag { Key = "courseId", Value = command.CourseId.ToString() } };

var domainEvent = new DomainEvent(
    Event: @event,
    EventType: nameof(CourseCreated),
    Tags: tags,
    Metadata: new Metadata { /* ... */ }
);

await eventStore.AppendAsync([domainEvent], context: "CourseManagement");
```

---

#### 2. EnrollStudentToCourseCommandHandler ❌

**Purpose**: Enroll a student in a course with DCB validation

**What it needs to do**:
1. **Build DCB aggregate** from relevant events
   - Query for events tagged with `courseId` OR `studentId`
   - Query for `CourseCreated`, `StudentEnrolled`, `StudentUnenrolled` events
   - Fold events into `CourseEnlistmentAggregate`

2. **Validate business rules** using aggregate
   - Check `aggregate.CanEnrollStudent()`
   - If false, return `CommandResult(Success: false, ErrorMessage: aggregate.GetEnrollmentFailureReason())`

3. **Create and append event** if validation passes
   - Create `StudentEnrolledToCourseEvent`
   - Tag with BOTH `courseId` AND `studentId` tags
   - Use **AppendCondition** to ensure no concurrent enrollments (optimistic concurrency)
   - Return `CommandResult(Success: true)`

**Method signature**:
```csharp
public async Task<CommandResult> HandleAsync(
    EnrollStudentToCourseCommand command,
    IEventStore eventStore)
{
    // Implementation needed
}
```

**Query for DCB aggregate**:
```csharp
// Get events for enrollment decision (same query from tests)
var query = Query.FromItems(
    new QueryItem
    {
        Tags = [new Tag { Key = "courseId", Value = command.CourseId.ToString() }],
        EventTypes = [
            nameof(CourseCreated),
            nameof(CourseCapacityUpdatedEvent),
            nameof(StudentEnrolledToCourseEvent),
            nameof(StudentUnenrolledFromCourseEvent)
        ]
    },
    new QueryItem
    {
        Tags = [new Tag { Key = "studentId", Value = command.StudentId.ToString() }],
        EventTypes = [
            nameof(StudentEnrolledToCourseEvent),
            nameof(StudentUnenrolledFromCourseEvent)
        ]
    }
);

var events = await eventStore.ReadAsync(query);
var aggregate = BuildAggregate(events, command.CourseId, command.StudentId);
```

**Validation logic**:
```csharp
if (!aggregate.CanEnrollStudent())
{
    return new CommandResult(
        Success: false,
        ErrorMessage: aggregate.GetEnrollmentFailureReason()
    );
}
```

**Event creation with AppendCondition**:
```csharp
var @event = new StudentEnrolledToCourseEvent(command.CourseId, command.StudentId);
var tags = new[]
{
    new Tag { Key = "courseId", Value = command.CourseId.ToString() },
    new Tag { Key = "studentId", Value = command.StudentId.ToString() }
};

var domainEvent = new DomainEvent(
    Event: @event,
    EventType: nameof(StudentEnrolledToCourseEvent),
    Tags: tags,
    Metadata: new Metadata { /* ... */ }
);

// Use AppendCondition to prevent concurrent enrollments
var lastPosition = events.Length > 0 ? events[^1].SequencePosition : 0;
var appendCondition = new AppendCondition
{
    AfterSequencePosition = lastPosition,
    FailIfEventsMatch = query // Fail if new events appeared since we read
};

await eventStore.AppendAsync(
    [domainEvent],
    context: "CourseManagement",
    appendCondition: appendCondition
);

return new CommandResult(Success: true);
```

---

## Implementation Plan

### Step 1: Create Command Handlers File
**File**: `tests/Opossum.IntegrationTests/CommandHandlers.cs`

Contains:
- `CreateCourseCommandHandler` class
- `EnrollStudentToCourseCommandHandler` class
- Both handlers use convention-based discovery (no attribute needed)
- Both handlers inject `IEventStore` via constructor or method parameter

### Step 2: Helper Method (Optional)
Move the `BuildAggregate()` method to a shared location or keep in handlers.

### Step 3: Run Tests
Expected result: All 4 integration tests should pass ✅

---

## Key Design Patterns Demonstrated

### 1. **Dynamic Consistency Boundary (DCB)**
The `EnrollStudentToCourseCommandHandler` demonstrates DCB:
- Doesn't load entire "Course" or "Student" aggregates
- Loads only events needed for THIS decision (enrollment validation)
- Query combines courseId AND studentId events
- Aggregate tracks BOTH course capacity AND student enrollment count

### 2. **Optimistic Concurrency with AppendCondition**
Uses `AppendCondition` to detect concurrent modifications:
- Checks if any new events appeared since we read
- If yes, the append fails and handler can retry with fresh data
- Prevents race conditions (2 students enrolling in last slot simultaneously)

### 3. **Event Sourcing Best Practices**
- Events are immutable facts (`StudentEnrolledToCourseEvent`)
- Tags enable efficient querying (`courseId`, `studentId`)
- Aggregate is built by folding events (no state stored)
- Business rules enforced BEFORE creating events

### 4. **Mediator Pattern**
- Commands are dispatched through mediator
- Handlers discovered via convention
- Dependency injection for `IEventStore`
- Clear separation: command → handler → event store

---

## Testing Coverage

Once handlers are implemented, these scenarios will be validated:

✅ **Happy Path**: Student successfully enrolled in course
✅ **Course Full**: Enrollment fails when course at max capacity  
✅ **Student Limit**: Enrollment fails when student reached max courses
✅ **Multiple Students**: Course correctly tracks enrollment count
✅ **Event Persistence**: Events written to file system
✅ **Event Querying**: Events retrieved by courseId and studentId tags
✅ **Aggregate Building**: State reconstructed from events
✅ **Business Rules**: Capacity and limit constraints enforced
✅ **Tagging**: Events properly tagged for efficient queries
✅ **Metadata**: Event metadata preserved (correlation, causation, etc.)

---

## Next Steps

**Priority**: Implement the 2 missing command handlers

**File to create**: `tests/Opossum.IntegrationTests/CommandHandlers.cs`

**Estimated time**: 20-30 minutes

**Expected outcome**: All 4 integration tests pass, demonstrating end-to-end DCB event sourcing workflow

---

## Additional Notes

### Why These Tests Matter

These integration tests demonstrate the **core value proposition** of Opossum:

1. **DCB in Action**: Show how to load small, purpose-built aggregates instead of full entity streams
2. **Event Sourcing**: Events as source of truth, aggregates as projections
3. **Complex Queries**: AND/OR combinations of EventTypes and Tags
4. **Business Rules**: Domain logic enforced through aggregates
5. **Optimistic Concurrency**: Safe concurrent operations with AppendCondition
6. **Real-World Scenario**: Course enrollment is a relatable domain problem

### Handler Discovery

Handlers are discovered via **convention**:
- Class name: `{CommandName}Handler` or any name
- Method name: `Handle`, `HandleAsync`, or any name
- First parameter: The command type
- Additional parameters: Injected dependencies (IEventStore, ILogger, etc.)
- Return type: The expected response (CommandResult)

No attributes required for basic scenarios!
