# Retry Logic Implementation - Dynamic Consistency Boundary (DCB)

## Overview

The enrollment handler implements **automatic retry logic** to handle concurrent enrollment attempts gracefully. This ensures that legitimate concurrent operations (e.g., two different students enrolling in the same course) can succeed without manual intervention.

---

## Implementation

### Handler Structure

```csharp
public sealed class EnrollStudentToCourseCommandHandler()
{
    private const int MaxRetryAttempts = 3;
    private const int InitialRetryDelayMs = 50;

    public async Task<CommandResult> HandleAsync(...)
    {
        // Retry loop
        while (attempt < MaxRetryAttempts)
        {
            try
            {
                return await TryEnrollStudentAsync(...);
            }
            catch (AppendConditionFailedException)
            {
                // Exponential backoff and retry
            }
        }
    }

    private async Task<CommandResult> TryEnrollStudentAsync(...)
    {
        // Actual enrollment logic
    }
}
```

---

## How It Works

### Retry Flow

```
Attempt 1 (0ms delay)
├─ Read events
├─ Build aggregate
├─ Validate invariants
├─ Append with AppendCondition
└─ ✅ Success → Return
   OR
   ❌ AppendConditionFailedException → Retry

Attempt 2 (50ms delay)
├─ Re-read events (includes new changes)
├─ Re-build aggregate (with updated state)
├─ Re-validate invariants (may now fail if capacity reached)
├─ Append with new AppendCondition
└─ ✅ Success → Return
   OR
   ❌ AppendConditionFailedException → Retry

Attempt 3 (100ms delay)
├─ Re-read events
├─ Re-build aggregate
├─ Re-validate invariants
├─ Append with new AppendCondition
└─ ✅ Success → Return
   OR
   ❌ AppendConditionFailedException → Give up

Final: Return "Failed after 3 attempts"
```

---

## Exponential Backoff

### Delay Strategy

```csharp
delayMs = InitialRetryDelayMs * (int)Math.Pow(2, attempt - 1)
```

**Delays:**
- Attempt 1: 0ms (immediate)
- Attempt 2: 50ms delay
- Attempt 3: 100ms delay
- Attempt 4: 200ms delay (if MaxRetryAttempts increased)

### Why Exponential Backoff?

1. **Reduces Contention**: Spreads out retry attempts
2. **Gives Time for Locks to Release**: File system operations complete
3. **Prevents Thundering Herd**: Concurrent requests don't all retry at once
4. **Progressive Back-off**: Assumes conflict will resolve quickly

---

## Concurrent Scenarios

### Scenario 1: Different Students, Same Course ✅

**Timeline:**

```
T1: Thread A (Alice) & Thread B (Bob) both read events (position 100)
    Course: 28/30 students enrolled

T2: Thread A validates → ✅ Capacity OK (28 < 30)
    Thread B validates → ✅ Capacity OK (28 < 30)

T3: Thread A appends → ✅ Success (position 101)
    Alice enrolled

T4: Thread B appends → ❌ AppendConditionFailedException
    (AfterSequencePosition = 100, but current = 101)

T5: Thread B RETRY (50ms delay)
    Re-read events (now includes Alice at position 101)
    Course: 29/30 students enrolled

T6: Thread B re-validates → ✅ Capacity OK (29 < 30)
    FailIfEventsMatch → No match (looking for Bob, found Alice)

T7: Thread B appends → ✅ Success (position 102)
    Bob enrolled

RESULT: ✅ Both Alice and Bob successfully enrolled
```

### Scenario 2: Same Student, Same Course ❌

**Timeline:**

```
T1: Thread A & Thread B both read events (position 100)
    Alice not yet enrolled

T2: Both validate → ✅ Not enrolled yet

T3: Thread A appends → ✅ Success (position 101)
    Alice enrolled

T4: Thread B appends → ❌ AppendConditionFailedException
    (Position conflict)

T5: Thread B RETRY (50ms delay)
    Re-read events (includes Alice enrollment at position 101)

T6: Thread B re-validates:
    IsStudentAlreadyEnrolledInThisCourse = true
    
    → ❌ Return CommandResult.Fail("Student is already enrolled")

RESULT: ❌ Thread B fails with business rule violation (no more retries)
```

### Scenario 3: Course at Capacity ❌

**Timeline:**

```
T1: Course: 29/30 students
    Thread A (Alice) & Thread B (Bob) both read events

T2: Both validate → ✅ Capacity OK (29 < 30)

T3: Thread A appends Alice → ✅ Success
    Course: 30/30 students (FULL)

T4: Thread B appends → ❌ AppendConditionFailedException

T5: Thread B RETRY (50ms delay)
    Re-read events (includes Alice)
    Course: 30/30 students

T6: Thread B re-validates:
    CourseCurrentEnrollmentCount (30) >= CourseMaxCapacity (30)
    
    → ❌ Return CommandResult.Fail("Course is at maximum capacity")

RESULT: ❌ Thread B fails with capacity violation (no more retries)
```

---

## Exception Handling

### What Gets Retried

**Only `AppendConditionFailedException`:**
- Thrown when `AfterSequencePosition` doesn't match
- Thrown when `FailIfEventsMatch` finds a duplicate
- Indicates a **recoverable** concurrency conflict

### What Doesn't Get Retried

**Business rule violations** (return `CommandResult.Fail`):
- Course doesn't exist
- Student not registered
- Already enrolled
- Course at capacity
- Student at enrollment limit

These are **intentional failures** and should not be retried.

**Other exceptions** (propagate up):
- Infrastructure failures (disk errors, network issues)
- Unexpected errors

---

## Configuration

### Retry Parameters

```csharp
private const int MaxRetryAttempts = 3;
private const int InitialRetryDelayMs = 50;
```

**Why 3 attempts?**
- Most conflicts resolve within 1-2 retries
- 3 attempts provides good balance between success rate and latency
- Total max delay: 50ms + 100ms = 150ms (acceptable for user experience)

**Why 50ms initial delay?**
- File system operations typically complete within 10-30ms
- 50ms gives enough time for locks to release
- Not too long to impact user experience

### Tuning Considerations

**Increase MaxRetryAttempts if:**
- ✅ High concurrency environment (many simultaneous enrollments)
- ✅ Slower storage system
- ❌ But increases worst-case latency

**Decrease MaxRetryAttempts if:**
- ✅ Low concurrency
- ✅ Fast storage system
- ✅ Strict latency requirements

**Adjust InitialRetryDelayMs if:**
- Increase for slower file systems
- Decrease for in-memory or fast SSDs

---

## Benefits

### 1. **Automatic Conflict Resolution**
- Different students can enroll concurrently without errors
- No manual retry required from clients

### 2. **Maintains Invariants**
- Business rules re-validated on each retry
- Ensures consistency even with stale reads

### 3. **Predictable Behavior**
- Maximum 3 attempts = predictable latency
- Clear error message when exhausted

### 4. **Transparent to Clients**
- API clients don't need to implement retry logic
- Same HTTP response whether it took 1 or 3 attempts

---

## Trade-offs

### Pros ✅
- Handles most concurrency conflicts automatically
- Simple implementation
- Predictable performance

### Cons ❌
- Adds latency on retry (50ms-150ms)
- Re-reads events on each retry (I/O overhead)
- Can fail after exhausting retries (client may still need retry)

---

## Alternative Approaches

### 1. **No Retry (Current Without This)**
- **Pro**: Lowest latency on success
- **Con**: Clients must implement retry
- **Con**: Poor user experience (random failures)

### 2. **Infrastructure-Level Retry**
- **Pro**: Reusable across all handlers
- **Con**: Harder to test
- **Con**: Less control over retry logic

### 3. **Client-Side Retry**
- **Pro**: Server doesn't hold resources during retry
- **Con**: Network overhead on each retry
- **Con**: More complex client code

### 4. **Idempotent Commands with Deduplication**
- **Pro**: Can safely retry infinitely
- **Con**: Requires idempotency keys
- **Con**: More complex infrastructure

---

## Testing

### Unit Tests

```csharp
[Fact]
public async Task EnrollStudent_WithConcurrentEnrollments_RetriesAndSucceeds()
{
    // Arrange: Mock AppendAsync to fail once, then succeed
    
    // Act: Enroll student
    
    // Assert: 
    // - Retry happened (delay observed)
    // - Success on second attempt
}

[Fact]
public async Task EnrollStudent_AfterMaxRetries_ReturnsConcurrencyFailure()
{
    // Arrange: Mock AppendAsync to always fail
    
    // Act: Enroll student
    
    // Assert:
    // - Failed after 3 attempts
    // - Error message mentions retry exhaustion
}
```

### Integration Tests

```csharp
[Fact]
public async Task ConcurrentEnrollments_DifferentStudents_BothSucceed()
{
    // Arrange: Course with capacity 30
    
    // Act: 
    // - Task A: Enroll Alice
    // - Task B: Enroll Bob (concurrent)
    // - await Task.WhenAll(taskA, taskB)
    
    // Assert:
    // - Both tasks succeeded
    // - Course has 2 students
}
```

---

## Monitoring & Observability

### Metrics to Track

1. **Retry Rate**: % of enrollments that required retry
2. **Average Retry Count**: How many retries on average
3. **Retry Exhaustion Rate**: % that failed after max retries
4. **Latency Distribution**: P50, P95, P99 including retry delays

### Logging (Future Enhancement)

```csharp
catch (AppendConditionFailedException ex)
{
    attempt++;
    _logger.LogWarning(
        "Enrollment retry {Attempt}/{MaxAttempts} for student {StudentId} in course {CourseId}",
        attempt, MaxRetryAttempts, command.StudentId, command.CourseId);
    
    // ... retry logic
}
```

---

## Summary

The retry logic implements the **Dynamic Consistency Boundary (DCB)** pattern's promise:

> "Detect conflicts before they happen, but allow concurrent operations that don't truly conflict."

By automatically retrying with fresh data, we achieve:
- ✅ High availability (concurrent operations succeed)
- ✅ Strong consistency (business rules always enforced)
- ✅ Good performance (most conflicts resolve in 1-2 retries)
- ✅ Predictable behavior (max 3 attempts, clear errors)

This is the **practical implementation** of optimistic concurrency in event sourcing systems.
