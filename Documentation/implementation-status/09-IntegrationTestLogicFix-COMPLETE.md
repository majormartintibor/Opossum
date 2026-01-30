# ✅ Integration Test Logic Fix - Dynamic Consistency Boundaries

**Date**: December 2024  
**Issue**: Incorrect aggregate logic for enrollment decision  
**Status**: ✅ FIXED

---

## 🐛 Problems Identified

### Problem 1: StudentId Was Mutable
**Before**:
```csharp
public Guid StudentId { get; set; } // Last student who interacted
```
- StudentId changed with every event
- Lost track of which student we're validating for

**After**:
```csharp
public Guid StudentId { get; private init; }
```
- Set once during construction from command
- Defines the consistency boundary

### Problem 2: Missing Student Enrollment Count Logic
**Before**:
```csharp
public CourseEnlistmentAggregate Apply(StudentEnrolledToCourseEvent @event)
{
    return this with
    {
        StudentId = @event.StudentId, // WRONG!
        CourseCurrentEnrollmentCount = CourseCurrentEnrollmentCount + 1
    };
    // StudentCurrentCourseEnrollmentCount was never updated!
}
```

**After**:
```csharp
public CourseEnlistmentAggregate Apply(StudentEnrolledToCourseEvent @event)
{
    var courseCount = CourseCurrentEnrollmentCount;
    var studentCount = StudentCurrentCourseEnrollmentCount;

    // If event is for THIS course -> increment course enrollment count
    if (@event.CourseId == CourseId)
        courseCount++;

    // If event is for THIS student -> increment student enrollment count
    if (@event.StudentId == StudentId)
        studentCount++;

    return this with
    {
        CourseCurrentEnrollmentCount = courseCount,
        StudentCurrentCourseEnrollmentCount = studentCount
    };
}
```

### Problem 3: Static Apply vs Constructor Initialization
**Before**:
```csharp
public static CourseEnlistmentAggregate Apply(CourseCreated @event)
{
    return new CourseEnlistmentAggregate() 
    {
        CourseId = @event.CourseId,
        CourseMaxCapacity = @event.MaxCapacity,
        CourseCurrentEnrollmentCount = 0
    };
}
```
- Used static factory method
- CourseId and StudentId came from events
- Didn't define the consistency boundary clearly

**After**:
```csharp
// Constructor defines the consistency boundary from command
public CourseEnlistmentAggregate(Guid courseId, Guid studentId, int studentMaxLimit = 5)
{
    CourseId = courseId;
    StudentId = studentId;
    StudentMaxCourseEnrollmentLimit = studentMaxLimit;
}

// Apply only updates relevant fields
public CourseEnlistmentAggregate Apply(CourseCreated @event)
{
    // Only apply if it's for our course
    if (@event.CourseId != CourseId)
        return this;

    return this with
    {
        CourseMaxCapacity = @event.MaxCapacity
    };
}
```

---

## ✅ Correct Implementation

### Key Insights

**1. Aggregate Identity Comes from Command**
```csharp
// Command defines which student + course we're validating
var command = new EnrollStudentToCourseCommand(courseId, studentId);

// Aggregate is initialized with this context
var aggregate = new CourseEnlistmentAggregate(courseId, studentId);
```

**2. Events Are Conditionally Applied**
The aggregate receives events for:
- All enrollments in THIS course (different students)
- All enrollments of THIS student (different courses)

Each event is checked to see if it affects:
- The course count (if `event.CourseId == CourseId`)
- The student count (if `event.StudentId == StudentId`)
- BOTH (if event is THIS student enrolling in THIS course)

**3. Two Invariants Enforced**
```csharp
public bool CanEnrollStudent()
{
    return CourseCurrentEnrollmentCount < CourseMaxCapacity &&
           StudentCurrentCourseEnrollmentCount < StudentMaxCourseEnrollmentLimit;
}
```

---

## 📊 Updated Test Structure

### Test 1: Happy Path
```csharp
[Fact]
public async Task EnrollStudentToCourse_ShouldCreateEventAndBuildAggregate()
{
    var courseId = Guid.NewGuid();
    var studentId = Guid.NewGuid();
    
    // Create course
    await _mediator.InvokeAsync<CommandResult>(new CreateCourseCommand(courseId, 30));
    
    // Enroll student
    await _mediator.InvokeAsync<CommandResult>(
        new EnrollStudentToCourseCommand(courseId, studentId));
    
    // Build aggregate
    var aggregate = BuildAggregate(events, courseId, studentId);
    
    // Assert
    Assert.Equal(1, aggregate.CourseCurrentEnrollmentCount); // 1 student in course
    Assert.Equal(1, aggregate.StudentCurrentCourseEnrollmentCount); // student in 1 course
}
```

### Test 2: Course Capacity Limit
```csharp
[Fact]
public async Task EnrollStudentToCourse_WhenCourseIsFull_ShouldFail()
{
    var courseId = Guid.NewGuid();
    
    // Create course with capacity 2
    await _mediator.InvokeAsync<CommandResult>(new CreateCourseCommand(courseId, 2));
    
    // Enroll 2 students (course full)
    await _mediator.InvokeAsync<CommandResult>(
        new EnrollStudentToCourseCommand(courseId, student1Id));
    await _mediator.InvokeAsync<CommandResult>(
        new EnrollStudentToCourseCommand(courseId, student2Id));
    
    // Try to enroll 3rd student
    var result = await _mediator.InvokeAsync<CommandResult>(
        new EnrollStudentToCourseCommand(courseId, student3Id));
    
    Assert.False(result.Success);
    Assert.Equal("Course is at maximum capacity", result.ErrorMessage);
}
```

### Test 3: Student Enrollment Limit (NEW)
```csharp
[Fact]
public async Task EnrollStudentToCourse_WhenStudentReachedLimit_ShouldFail()
{
    var studentId = Guid.NewGuid();
    
    // Create 3 courses
    await _mediator.InvokeAsync<CommandResult>(new CreateCourseCommand(course1Id, 30));
    await _mediator.InvokeAsync<CommandResult>(new CreateCourseCommand(course2Id, 30));
    await _mediator.InvokeAsync<CommandResult>(new CreateCourseCommand(course3Id, 30));
    
    // Student enrolls in 2 courses (at limit)
    await _mediator.InvokeAsync<CommandResult>(
        new EnrollStudentToCourseCommand(course1Id, studentId));
    await _mediator.InvokeAsync<CommandResult>(
        new EnrollStudentToCourseCommand(course2Id, studentId));
    
    // Try to enroll in 3rd course
    var result = await _mediator.InvokeAsync<CommandResult>(
        new EnrollStudentToCourseCommand(course3Id, studentId));
    
    Assert.False(result.Success);
    Assert.Contains("Student has reached maximum course enrollment limit", result.ErrorMessage);
}
```

### Test 4: Multiple Students Tracking (NEW)
```csharp
[Fact]
public async Task EnrollStudentToCourse_MultipleStudents_ShouldTrackCorrectly()
{
    // Enroll 3 different students in same course
    await _mediator.InvokeAsync<CommandResult>(
        new EnrollStudentToCourseCommand(courseId, student1Id));
    await _mediator.InvokeAsync<CommandResult>(
        new EnrollStudentToCourseCommand(courseId, student2Id));
    await _mediator.InvokeAsync<CommandResult>(
        new EnrollStudentToCourseCommand(courseId, student3Id));
    
    // Build aggregate from student1's perspective
    var aggregate = BuildAggregate(events, courseId, student1Id);
    
    Assert.Equal(3, aggregate.CourseCurrentEnrollmentCount); // 3 students in course
    Assert.Equal(1, aggregate.StudentCurrentCourseEnrollmentCount); // student1 in 1 course
}
```

---

## 🎯 DCB Pattern Validated

### The Query Retrieves Mixed Events
```csharp
var enrollmentQuery = Query.FromItems(
    new QueryItem
    {
        Tags = [new Tag { Key = "courseId", Value = courseId.ToString() }],
        EventTypes = ["StudentEnrolledToCourseEvent", "StudentUnenrolledFromCourseEvent"]
    },
    new QueryItem
    {
        Tags = [new Tag { Key = "studentId", Value = studentId.ToString() }],
        EventTypes = ["StudentEnrolledToCourseEvent", "StudentUnenrolledFromCourseEvent"]
    }
);
```

**Returns events like:**
- `StudentEnrolledToCourseEvent(courseX, studentA)` ✅ courseX matches
- `StudentEnrolledToCourseEvent(courseX, studentB)` ✅ courseX matches
- `StudentEnrolledToCourseEvent(courseY, studentX)` ✅ studentX matches
- `StudentEnrolledToCourseEvent(courseZ, studentX)` ✅ studentX matches

### The Aggregate Processes Conditionally
```csharp
foreach (var event in events)
{
    if (event.CourseId == CourseId) // Count for THIS course
        CourseCurrentEnrollmentCount++;
    
    if (event.StudentId == StudentId) // Count for THIS student
        StudentCurrentCourseEnrollmentCount++;
}
```

**Result**: Aggregate has complete context for BOTH invariants:
- ✅ How many students in the course?
- ✅ How many courses the student is in?

---

## 💡 Key Learnings

### 1. Aggregate Identity is Set at Construction
- CourseId and StudentId come from the **command**, not events
- They define the **consistency boundary**
- They never change during event folding

### 2. Events Are Filtered During Apply
- Not every event affects every field
- Use conditional logic: `if (event.CourseId == CourseId)`
- Some events might affect both counters (same student + same course)

### 3. Two Perspectives, One Aggregate
- Same event stream, different aggregate instances
- `BuildAggregate(events, courseX, studentA)` - one perspective
- `BuildAggregate(events, courseX, studentB)` - different perspective
- Different StudentCurrentCourseEnrollmentCount, same CourseCurrentEnrollmentCount

### 4. DCB Enables Focused Queries
- Traditional ES: Load entire Course aggregate + entire Student aggregate
- DCB: Load only enrollment events for relevant course + student
- Much smaller event set, same validation capability

---

## 📝 Summary

✅ **Fixed**: Aggregate initialization (constructor with command context)  
✅ **Fixed**: StudentId is immutable (defines consistency boundary)  
✅ **Fixed**: StudentCurrentCourseEnrollmentCount tracking  
✅ **Fixed**: Conditional event application logic  
✅ **Added**: Student enrollment limit test  
✅ **Added**: Multiple students tracking test  
✅ **Added**: GetEnrollmentFailureReason() helper  

**Build Status**: ✅ SUCCESSFUL  
**Logic Validation**: ✅ CORRECT  
**DCB Pattern**: ✅ PROPERLY DEMONSTRATED  

The aggregate now correctly implements Dynamic Consistency Boundaries for the enrollment decision! 🎉
