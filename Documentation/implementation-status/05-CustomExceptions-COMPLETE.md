# Custom Exception Classes - COMPLETE ✅

**Component**: Error Handling  
**Status**: ✅ Complete  
**Date**: Implementation Session  
**Time Spent**: ~20 minutes  
**Estimated Time**: 30 minutes  
**Variance**: 10 minutes ahead of schedule  

---

## Summary

Successfully implemented custom exception classes for the Opossum Event Store, providing a comprehensive error handling framework with specialized exceptions for different failure scenarios. All exceptions inherit from a base `EventStoreException` class, making it easy to catch all Opossum-specific exceptions while still allowing granular error handling.

---

## What Was Implemented

### Base Exception Class

**`EventStoreException`** - Base exception for all Opossum exceptions
- Inherits from `System.Exception`
- Standard constructor overloads (default, message, message+innerException)
- Allows catching all Opossum exceptions with single catch block

---

### Specialized Exception Classes (5)

#### 1. **AppendConditionFailedException**
**Purpose**: DCB specification compliance - optimistic concurrency control

**When Thrown**:
- AppendCondition validation fails
- FailIfEventsMatch query returns conflicting events
- Optimistic concurrency check detects conflicts

**Constructor Overloads**:
- Default constructor
- Message constructor
- Message + InnerException constructor

**Usage Example**:
```csharp
throw new AppendConditionFailedException(
    "Cannot append events: AppendCondition failed. Found 3 conflicting events.");
```

---

#### 2. **ContextNotFoundException**
**Purpose**: Bounded context management and validation

**When Thrown**:
- Accessing context not configured in OpossumOptions
- AppendAsync/ReadAsync called with unknown context name
- Storage directory for context doesn't exist

**Special Properties**:
- `ContextName` - Name of the context that wasn't found (nullable)

**Constructor Overloads**:
- Default constructor
- Message constructor
- Message + ContextName constructor ⭐
- Message + InnerException constructor
- Message + ContextName + InnerException constructor ⭐

**Usage Example**:
```csharp
throw new ContextNotFoundException(
    "Context 'Billing' not found. Add it via options.AddContext(\"Billing\")", 
    "Billing");
```

---

#### 3. **InvalidQueryException**
**Purpose**: Query validation and DCB compliance

**When Thrown**:
- Query.QueryItems is empty
- QueryItem has both empty EventTypes and Tags
- Invalid tag values or event type names
- Query violates DCB specification rules

**Constructor Overloads**:
- Default constructor
- Message constructor
- Message + InnerException constructor

**Usage Example**:
```csharp
throw new InvalidQueryException(
    "Query must contain at least one QueryItem");
```

---

#### 4. **ConcurrencyException**
**Purpose**: File system concurrency conflict handling

**When Thrown**:
- Ledger sequence conflicts
- File system race conditions
- Simultaneous writes to same aggregate
- Lower-level concurrency issues (vs DCB-level AppendConditionFailedException)

**Special Properties**:
- `ExpectedSequence` - Expected sequence position (nullable)
- `ActualSequence` - Actual sequence found (nullable)

**Constructor Overloads**:
- Default constructor
- Message constructor
- Message + ExpectedSequence + ActualSequence constructor ⭐
- Message + InnerException constructor
- Message + ExpectedSequence + ActualSequence + InnerException ⭐

**Usage Example**:
```csharp
throw new ConcurrencyException(
    "Ledger sequence conflict: expected 42, found 43", 
    42, 
    43);
```

---

#### 5. **EventNotFoundException**
**Purpose**: Event retrieval and aggregate loading

**When Thrown**:
- ReadAsync query matches no events (optional usage)
- Loading aggregate that has no events
- Event file missing or corrupted
- Index references non-existent event

**Special Properties**:
- `QueryDescription` - Description of query used (nullable)

**Constructor Overloads**:
- Default constructor
- Message constructor
- Message + QueryDescription constructor ⭐
- Message + InnerException constructor
- Message + QueryDescription + InnerException constructor ⭐

**Usage Example**:
```csharp
var courseId = Guid.NewGuid();
throw new EventNotFoundException(
    $"No events found for aggregate with CourseId: {courseId}",
    $"CourseId: {courseId}");
```

---

## Test Coverage

Created `EventStoreExceptionsTests.cs` with **38 comprehensive tests**:

### Base Class Tests (3)
1. ✅ Default constructor creates exception
2. ✅ Message constructor sets message
3. ✅ Message + InnerException sets both properties

### AppendConditionFailedException Tests (4)
4. ✅ Default constructor creates exception
5. ✅ Message constructor sets message
6. ✅ Message + InnerException sets properties
7. ✅ Can be caught as EventStoreException

### ContextNotFoundException Tests (6)
8. ✅ Default constructor creates exception (ContextName is null)
9. ✅ Message constructor sets message
10. ✅ Message + ContextName sets both properties
11. ✅ Message + InnerException sets properties
12. ✅ Full constructor (Message + ContextName + InnerException) sets all
13. ✅ Can be caught as EventStoreException

### InvalidQueryException Tests (4)
14. ✅ Default constructor creates exception
15. ✅ Message constructor sets message
16. ✅ Message + InnerException sets properties
17. ✅ Can be caught as EventStoreException

### ConcurrencyException Tests (7)
18. ✅ Default constructor creates exception (sequences are null)
19. ✅ Message constructor sets message
20. ✅ Message + Sequences sets all properties
21. ✅ Message + InnerException sets properties
22. ✅ Full constructor sets all properties
23. ✅ Can be caught as EventStoreException
24. ✅ Sequence properties are nullable

### EventNotFoundException Tests (6)
25. ✅ Default constructor creates exception (QueryDescription is null)
26. ✅ Message constructor sets message
27. ✅ Message + QueryDescription sets both properties
28. ✅ Message + InnerException sets properties
29. ✅ Full constructor sets all properties
30. ✅ Can be caught as EventStoreException

### Integration Tests (3)
31. ✅ All exceptions inherit from EventStoreException
32. ✅ EventStoreException can catch all specialized exceptions
33. ✅ Specialized exceptions can be caught specifically

### Usage Scenarios (5)
34. ✅ ContextNotFoundException with context name
35. ✅ ConcurrencyException with sequence conflict
36. ✅ EventNotFoundException with query description
37. ✅ AppendConditionFailedException for DCB violation
38. ✅ InvalidQueryException for empty query items

### Test Results
```
Test summary: total: 38; failed: 0; succeeded: 38; skipped: 0
Duration: 1.0s
```

---

## Design Decisions

### 1. **Single File Approach**
- All exceptions in `EventStoreExceptions.cs`
- Easier to maintain and navigate
- Clear overview of entire exception hierarchy
- Consistent with small-to-medium library pattern

### 2. **Rich Constructor Overloads**
- Standard overloads for all exceptions (default, message, message+inner)
- Specialized overloads for context-specific data:
  - `ContextNotFoundException` adds contextName parameter
  - `ConcurrencyException` adds sequence parameters
  - `EventNotFoundException` adds queryDescription parameter
- Enables both simple and detailed exception scenarios

### 3. **Nullable Extra Properties**
- ContextName, ExpectedSequence, ActualSequence, QueryDescription all nullable
- Allows creating exceptions without extra data
- Defaults are safe (null)
- Can check for presence: `if (ex.ContextName != null)`

### 4. **Inheritance Hierarchy**
```
System.Exception
└── EventStoreException (base for all Opossum exceptions)
    ├── AppendConditionFailedException
    ├── ContextNotFoundException
    ├── InvalidQueryException
    ├── ConcurrencyException
    └── EventNotFoundException
```

### 5. **XML Documentation**
- Comprehensive XML docs on all classes and members
- Explains when each exception should be thrown
- Documents all constructor parameters
- Provides IntelliSense support for library users

---

## Key Features

### Catch Flexibility

**Catch all Opossum exceptions**:
```csharp
try
{
    await eventStore.AppendAsync(context, events);
}
catch (EventStoreException ex)
{
    // Handles all Opossum-specific exceptions
    logger.LogError(ex, "Event store operation failed");
}
```

**Catch specific exceptions**:
```csharp
try
{
    await eventStore.AppendAsync(context, events);
}
catch (AppendConditionFailedException ex)
{
    // Handle DCB conflicts specifically
    logger.LogWarning("Optimistic concurrency conflict detected");
}
catch (ContextNotFoundException ex)
{
    logger.LogError($"Unknown context: {ex.ContextName}");
}
catch (EventStoreException ex)
{
    // Other Opossum exceptions
}
```

### Context-Rich Error Messages

Exceptions include relevant context:
```csharp
// ContextNotFoundException with context name
var ex1 = new ContextNotFoundException("Context not found", "Billing");
Console.WriteLine(ex1.ContextName); // "Billing"

// ConcurrencyException with sequences
var ex2 = new ConcurrencyException("Conflict", 42, 43);
Console.WriteLine($"Expected: {ex2.ExpectedSequence}, Actual: {ex2.ActualSequence}");

// EventNotFoundException with query
var ex3 = new EventNotFoundException("No events", "CourseId: 123");
Console.WriteLine(ex3.QueryDescription); // "CourseId: 123"
```

---

## Integration with Existing Code

### Usage in FileSystemEventStore (Future)

**AppendAsync validation**:
```csharp
public async Task AppendAsync(string context, ...)
{
    if (!_options.Contexts.Contains(context))
    {
        throw new ContextNotFoundException(
            $"Context '{context}' not found.", 
            context);
    }
    
    // DCB append condition check
    if (appendCondition?.FailIfEventsMatch != null)
    {
        var conflicts = await ReadAsync(context, appendCondition.FailIfEventsMatch);
        if (conflicts.Any())
        {
            throw new AppendConditionFailedException(
                $"Cannot append: found {conflicts.Count} conflicting events");
        }
    }
}
```

**ReadAsync validation**:
```csharp
public async Task<List<SequencedEvent>> ReadAsync(string context, Query query)
{
    if (!_options.Contexts.Contains(context))
    {
        throw new ContextNotFoundException(
            $"Context '{context}' not found.", 
            context);
    }
    
    if (query.QueryItems.Count == 0)
    {
        throw new InvalidQueryException(
            "Query must contain at least one QueryItem");
    }
}
```

**Concurrency control**:
```csharp
private async Task UpdateLedgerAsync(string context, long expectedSequence)
{
    var actualSequence = await ReadLedgerAsync(context);
    
    if (actualSequence != expectedSequence)
    {
        throw new ConcurrencyException(
            $"Ledger sequence conflict: expected {expectedSequence}, found {actualSequence}",
            expectedSequence,
            actualSequence);
    }
}
```

---

## Files Created

### Production Code
- ✅ `src\Opossum\Exceptions\EventStoreExceptions.cs` (~280 lines)
  - EventStoreException
  - AppendConditionFailedException
  - ContextNotFoundException (with ContextName property)
  - InvalidQueryException
  - ConcurrencyException (with ExpectedSequence, ActualSequence properties)
  - EventNotFoundException (with QueryDescription property)

### Test Code
- ✅ `tests\Opossum.UnitTests\Exceptions\EventStoreExceptionsTests.cs` (~650 lines)
  - 38 comprehensive unit tests
  - Integration tests for exception hierarchy
  - Usage scenario tests

---

## Exception Usage Guidelines

### When to Use Each Exception

| Exception | Use When | Don't Use When |
|-----------|----------|----------------|
| AppendConditionFailedException | DCB append condition fails | General validation errors |
| ContextNotFoundException | Context not in OpossumOptions.Contexts | Context directory doesn't exist (use IOException) |
| InvalidQueryException | Query violates DCB rules | Query returns no results (that's valid) |
| ConcurrencyException | File system race, ledger conflicts | DCB-level conflicts (use AppendConditionFailedException) |
| EventNotFoundException | Aggregate has no events, event file missing | Query returns empty (that's valid) |
| EventStoreException (base) | Rarely used directly | Use specialized exceptions instead |

---

## Success Metrics

| Metric | Target | Actual | Status |
|--------|--------|--------|--------|
| Time to implement | 30 min | 20 min | ✅ 10 min ahead |
| Exception classes | 6 (1 base + 5 specialized) | 6 | ✅ Complete |
| Tests created | 30+ | 38 | ✅ Exceeded |
| Tests passing | 100% | 100% | ✅ Perfect |
| Build errors | 0 | 0 | ✅ Clean |
| XML documentation | All public APIs | All | ✅ Complete |

---

## Phase 1 Progress Update

**Phase 1: Independent Components** - 25% COMPLETE (2 of 8 items)

| Component | Status | Tests | Time |
|-----------|--------|-------|------|
| OpossumOptions | ✅ Complete | 19/19 | 25 min |
| **Custom Exceptions** | ✅ **Complete** | **38/38** | **20 min** |
| ReadOption Enum | ⏳ Not started | 0 | 15 min est |
| EventStore Extensions | ⏳ Not started | 0 | 60 min est |
| Domain Events | ⚠️ Manual only | N/A | 30 min |
| Domain Aggregate | ⚠️ Manual only | N/A | 45 min |
| Commands & Queries | ⚠️ Manual only | N/A | 20 min |
| Command Handlers | ⚠️ Manual only | N/A | 30 min |

**Progress**: 2/8 items complete (25%), ~45 minutes invested

---

## Next Steps

**Immediate**:
- [x] Custom Exceptions implemented
- [x] All tests passing
- [x] Documentation complete
- [ ] Update PROGRESS.md
- [ ] Continue with ReadOption Enum (15 min) or EventStore Extensions (1 hour)

**Future**:
- Use exceptions in FileSystemEventStore implementation
- Consider adding more specialized exceptions as needs arise
- Monitor real-world usage patterns

---

## Lessons Learned

### 1. **Single File for Related Exceptions**
Keeping all exceptions in one file made it easy to maintain consistency in:
- Constructor patterns
- XML documentation style
- Inheritance hierarchy
- Namespace organization

### 2. **Extra Properties are Powerful**
Adding context-specific properties (ContextName, sequences, query description) makes debugging much easier without requiring message parsing.

### 3. **Nullable Properties Work Well**
Making extra properties nullable allows exceptions to be created without that data while still providing rich context when available.

### 4. **Test Coverage Builds Confidence**
38 tests covering all constructors and usage scenarios ensures the exception classes work as expected in real-world situations.

### 5. **Clear Hierarchy Aids Usability**
Single base class (EventStoreException) makes it trivial for library users to catch all Opossum exceptions or specific ones as needed.

---

## Conclusion

Custom exception classes are production-ready and provide a solid foundation for error handling throughout the Opossum Event Store. The exception hierarchy is:

✅ **Well-designed** - Clear separation of concerns  
✅ **Well-tested** - 38 tests covering all scenarios  
✅ **Well-documented** - XML docs on all public APIs  
✅ **Production-ready** - Can be used immediately in FileSystemEventStore  

**Phase 1 is now 25% complete!** 🎉

The exceptions provide clear, actionable error messages that will help developers diagnose and fix issues quickly when using the Opossum library.
