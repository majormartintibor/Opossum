# Bug Fix: DCB Concurrency Control in Course Enrollment

## Issue Summary

The integration test `EnrollStudent_ConcurrentEnrollments_HandlesRaceCondition` revealed a **critical bug** in the Sample Application's DCB (Dynamic Consistency Boundaries) implementation. When two students tried to enroll concurrently in a course with capacity=1, **both enrollments succeeded** instead of exactly one succeeding and one failing.

## Root Cause

The `EnrollStudentToCourseCommandHandler` was using **two different queries**:

1. **`GetCourseEnrollmentQuery()`** - Used to read events and build the decision model
2. **`GetFailIfMatchQuery()`** - Used in `AppendCondition.FailIfEventsMatch`

This violated the **fundamental DCB pattern**: 

> The query used to read events MUST be the same query used in the append condition.

### Why This Mattered

`GetFailIfMatchQuery()` only checked for **exact duplicate enrollments** (same student + same course):
```csharp
new QueryItem
{
    Tags = [
        new Tag { Key = "courseId", Value = courseId },
        new Tag { Key = "studentId", Value = studentId }
    ],
    EventTypes = [nameof(StudentEnrolledToCourseEvent)]
}
```

This meant when **two DIFFERENT students** tried to enroll concurrently:
- Thread A appends: `StudentEnrolledToCourseEvent(courseId=X, studentId=1)`
- Thread B's `FailIfEventsMatch` check: "Are there any events with `courseId=X AND studentId=2`?" → **NO!**
- Thread B succeeds → **Bug: Both enrolled in a course with capacity=1**

## The Fix

Changed line 110 in `EnrollStudentToCourseCommand.cs`:

**Before (WRONG):**
```csharp
var appendCondition = new AppendCondition
{
    AfterSequencePosition = events.Length > 0 ? events.Max(e => e.Position) : null,
    FailIfEventsMatch = command.GetFailIfMatchQuery() // ❌ DIFFERENT query
};
```

**After (CORRECT):**
```csharp
var appendCondition = new AppendCondition
{
    AfterSequencePosition = events.Length > 0 ? events.Max(e => e.Position) : null,
    FailIfEventsMatch = enrollmentQuery // ✅ SAME query used to read events
};
```

Now `FailIfEventsMatch` uses `enrollmentQuery`, which includes:
- **All course events** (courseId tag) - detects OTHER students enrolling
- **All student events** (studentId tag) - detects THIS student enrolling elsewhere
- **All enrollment events** - detects any relevant state changes

## Additional Fixes

### 1. Exception Handling

The retry logic was only catching `AppendConditionFailedException`, but the event store throws `ConcurrencyException`. Fixed by catching both:

```csharp
catch (ConcurrencyException) when (attempt < MaxRetryAttempts - 1)
{
    // Retry with exponential backoff
}
catch (AppendConditionFailedException) when (attempt < MaxRetryAttempts - 1)
{
    // Retry with exponential backoff
}
```

### 2. Removed Unnecessary Method

Deleted `GetFailIfMatchQuery()` from `Queries.cs` since it's no longer needed.

## Verification

The fix was verified by comparing with the Opossum library's `ConcurrencyTests.cs`, specifically:
- `ConcurrentEnrollments_WhenCourseHasOneSpotLeft_ShouldAllowOnlyOne` - which always passes

The Opossum library's command handler correctly uses the **same query** for both reading and the append condition (line 158 in `CommandHandlers.cs`):

```csharp
var appendCondition = new AppendCondition
{
    AfterSequencePosition = lastPosition,
    FailIfEventsMatch = query // ✅ Same query!
};
```

## Test Results

**Before Fix:**
- Concurrency test: ❌ **FAILED** - Both enrollments succeeded (2 success, 0 failure)

**After Fix:**
- Concurrency test: ✅ **PASSED** - Exactly one succeeds, one fails (1 success, 1 failure)
- All 36 sample application tests: ✅ **PASSED**

## Key Lesson: The Golden Rule of DCB

**CRITICAL DCB PATTERN:**

> Whatever query you use to READ events and build your decision model,  
> you MUST use that EXACT SAME query in `AppendCondition.FailIfEventsMatch`.

This ensures that:
1. If ANY event matching your decision criteria is added after your read...
2. ...your append will fail with a concurrency exception
3. ...forcing you to retry with fresh data
4. ...thus preventing stale decision models from succeeding

### Why Both Conditions Are Needed

- **`AfterSequencePosition`** alone: Would need to check ALL events (too broad, false conflicts)
- **`FailIfEventsMatch`** alone: Doesn't know what position you read from  
- **Together**: "Fail if events matching MY query were added after MY read" ← **Perfect DCB**

## Files Changed

1. `Samples/Opossum.Samples.CourseManagement/CourseEnrollment/EnrollStudentToCourseCommand.cs`
   - Fixed append condition to use `enrollmentQuery`
   - Updated exception handling to catch both exception types
   - Improved comments to explain correct DCB pattern

2. `Samples/Opossum.Samples.CourseManagement/CourseEnrollment/Queries.cs`
   - Removed `GetFailIfMatchQuery()` extension method (no longer needed)

3. `tests/Samples/Opossum.Samples.CourseManagement.IntegrationTests/CourseEnrollmentIntegrationTests.cs`
   - Fixed test expectations to require exactly 1 success and 1 failure
   - Removed incorrect comment about "both might succeed"

## Credit

This bug was discovered through comprehensive integration testing and comparing the Sample Application's implementation against the proven Opossum library patterns. The test suite successfully validated the DCB specification's requirements!
