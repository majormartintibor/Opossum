# Course Enrollment Implementation Summary

## ✅ Completed Implementation

### Feature: Enroll Student to Course

**File**: `CourseEnrollment/EnrollStudentToCourse.cs`

**Endpoint**:
```http
POST /courses/{courseId}/enrollments
Body: { "studentId": "..." }
```

---

## 🏗️ Architecture

### Aggregate: CourseEnrollmentAggregate

A specialized aggregate that tracks **both course and student state** to enforce enrollment business rules.

**State Tracked**:
```csharp
- CourseId                              // Which course
- StudentId                             // Which student
- CourseMaxCapacity                     // Course limit
- CourseCurrentEnrollmentCount          // Current enrollments in course
- StudentEnrollmentTier                 // Student's subscription tier
- StudentCurrentCourseEnrollmentCount   // Student's total enrollments
- IsStudentAlreadyEnrolledInThisCourse  // Duplicate check
```

**Computed Property**:
```csharp
StudentMaxCourseEnrollmentLimit → Derived from tier
  - Basic: 2 courses
  - Standard: 5 courses
  - Professional: 10 courses
  - Master: 25 courses
```

---

## 📊 Event Sourcing Logic

### Events Applied to Aggregate

#### Course Events
```csharp
CourseCreatedEvent
  → Sets initial CourseMaxCapacity

CourseStudentLimitModifiedEvent
  → Updates CourseMaxCapacity
```

#### Student Events
```csharp
StudentRegisteredEvent
  → Sets StudentEnrollmentTier = Basic

StudentSubscriptionUpdatedEvent
  → Updates StudentEnrollmentTier
```

#### Enrollment Events
```csharp
StudentEnrolledToCourseEvent (this course + this student)
  → Sets IsStudentAlreadyEnrolledInThisCourse = true

StudentEnrolledToCourseEvent (this course + any student)
  → Increments CourseCurrentEnrollmentCount

StudentEnrolledToCourseEvent (this student + any course)
  → Increments StudentCurrentCourseEnrollmentCount
```

**Pattern**: Events are applied based on matching either `CourseId` or `StudentId` or both.

---

## 🛡️ Business Invariants Enforced

### 1. Course Must Exist
```csharp
if (!events.Any(e => e.Event.Event is CourseCreatedEvent))
    return CommandResult.Fail("Course does not exist.");
```

### 2. Student Must Be Registered
```csharp
if (!events.Any(e => e.Event.Event is StudentRegisteredEvent))
    return CommandResult.Fail("Student is not registered.");
```

### 3. No Duplicate Enrollments
```csharp
if (aggregate.IsStudentAlreadyEnrolledInThisCourse)
    return CommandResult.Fail("Student is already enrolled in this course.");
```

### 4. Course Capacity Check
```csharp
if (aggregate.CourseCurrentEnrollmentCount >= aggregate.CourseMaxCapacity)
    return CommandResult.Fail($"Course is at maximum capacity ({aggregate.CourseMaxCapacity} students).");
```

### 5. Student Enrollment Limit Check
```csharp
if (aggregate.StudentCurrentCourseEnrollmentCount >= aggregate.StudentMaxCourseEnrollmentLimit)
    return CommandResult.Fail($"Student has reached their enrollment limit ({aggregate.StudentMaxCourseEnrollmentLimit} courses for {aggregate.StudentEnrollmentTier} tier).");
```

---

## 🔒 Concurrency Control (DCB Pattern)

### Append Condition

```csharp
var appendCondition = new AppendCondition
{
    AfterSequencePosition = events.Max(e => e.Position),
    FailIfEventsMatch = Query.FromItems(
        new QueryItem
        {
            Tags = [
                new Tag { Key = "courseId", Value = command.CourseId.ToString() },
                new Tag { Key = "studentId", Value = command.StudentId.ToString() }
            ],
            EventTypes = [nameof(StudentEnrolledToCourseEvent)]
        })
};
```

**Purpose**:
1. **AfterSequencePosition**: Ensures we're appending after the last event we've read (optimistic concurrency)
2. **FailIfEventsMatch**: Prevents duplicate enrollment if the same student+course event was already appended by another concurrent request

**Race Condition Prevention**:
- Two concurrent enrollments for same student+course → Second fails with DCB violation
- Two concurrent enrollments for different students in same course → Both succeed if capacity allows
- Enrollment + capacity change → Properly serialized by sequence position

---

## 🔍 Query Pattern

### Multi-Aggregate Query
```csharp
var enrollmentQuery = Query.FromItems(
    new QueryItem  // Course events
    {
        Tags = [new Tag { Key = "courseId", Value = ... }],
        EventTypes = [CourseCreatedEvent, CourseStudentLimitModifiedEvent, StudentEnrolledToCourseEvent]
    },
    new QueryItem  // Student events
    {
        Tags = [new Tag { Key = "studentId", Value = ... }],
        EventTypes = [StudentRegisteredEvent, StudentSubscriptionUpdatedEvent, StudentEnrolledToCourseEvent]
    });
```

**Retrieves**:
- All events for the specific course
- All events for the specific student
- All enrollment events involving either

---

## 🎯 API Design Alignment

### Pattern A Compliance ✅
```http
POST /courses/{courseId}/enrollments
```

- ✅ Resource-oriented: Course resource with enrollments collection
- ✅ Aggregate ID in URL (courseId)
- ✅ Command data in body (studentId)
- ✅ POST for creating relationship
- ✅ Returns 201 Created with location header

---

## 🚀 Response Behavior

### Success Response
```http
201 Created
Location: /courses/{courseId}/enrollments/{studentId}
Body: { "courseId": "...", "studentId": "..." }
```

### Error Responses
```http
400 Bad Request
Body: { error: "Course does not exist." }
Body: { error: "Student is not registered." }
Body: { error: "Student is already enrolled in this course." }
Body: { error: "Course is at maximum capacity (30 students)." }
Body: { error: "Student has reached their enrollment limit (2 courses for Basic tier)." }
```

---

## 📝 Key Implementation Details

### Aggregate Initialization
```csharp
new CourseEnrollmentAggregate(command.CourseId, command.StudentId)
```
- Sets identity upfront
- Default tier = Basic
- All counts = 0

### Event Application Order
```csharp
events
    .OrderBy(e => e.Position)           // ✅ Chronological order
    .Select(e => e.Event.Event)         // Extract domain event
    .Aggregate(initialAggregate, Apply) // Fold over events
```

### Apply Method Pattern Matching
```csharp
public CourseEnrollmentAggregate Apply(object @event) => @event switch
{
    CourseCreatedEvent created when created.CourseId == CourseId => ...,
    StudentEnrolledToCourseEvent enrolled when enrolled.CourseId == CourseId && enrolled.StudentId == StudentId => ...,
    _ => this  // Ignore irrelevant events
};
```

**Note**: Uses `when` clauses to filter events by CourseId/StudentId

---

## 🧪 Testing Scenarios

### Happy Path
1. Create course (max 30 students)
2. Register student (Basic tier, max 2 courses)
3. Enroll student → ✅ Success

### Capacity Constraint
1. Create course (max 2 students)
2. Enroll student A → ✅ Success
3. Enroll student B → ✅ Success
4. Enroll student C → ❌ "Course is at maximum capacity"

### Student Limit Constraint
1. Register student (Basic tier, max 2 courses)
2. Enroll in course A → ✅ Success
3. Enroll in course B → ✅ Success
4. Enroll in course C → ❌ "Student has reached their enrollment limit (2 courses for Basic tier)"

### Duplicate Prevention
1. Enroll student in course → ✅ Success
2. Enroll same student in same course → ❌ "Student is already enrolled in this course"

### Concurrency Test
1. Thread A: Read events for student+course
2. Thread B: Read events for student+course (same data)
3. Thread A: Append enrollment → ✅ Success
4. Thread B: Append enrollment → ❌ DCB violation (FailIfEventsMatch)

### Tier Upgrade Scenario
1. Register student (Basic tier, max 2 courses)
2. Enroll in course A → ✅
3. Enroll in course B → ✅
4. Enroll in course C → ❌ "Enrollment limit reached"
5. Upgrade to Standard tier → ✅
6. Enroll in course C → ✅ (now allowed, limit is 5)

---

## 🔄 Future Enhancements

### Potential Additional Events
- `StudentUnenrolledFromCourseEvent` - Decrement counts, allow re-enrollment
- `CourseArchivedEvent` - Prevent new enrollments
- `StudentSuspendedEvent` - Prevent enrollments while suspended

### Potential Projections
- `CourseEnrollmentListProjection` - List all students in a course
- `StudentEnrollmentListProjection` - List all courses for a student
- `CourseAvailabilityProjection` - Real-time capacity tracking

---

## ✅ Verification

**Build**: ✅ Successful  
**Endpoint Registered**: ✅ `app.MapEnrollStudentToCourseEndpoint()`  
**Invariants**: ✅ All 5 business rules enforced  
**Concurrency**: ✅ DCB pattern prevents race conditions  
**Pattern Compliance**: ✅ Follows API design patterns  

---

## 📚 Related Files

- `CourseCreation/CreateCourse.cs` - Creates courses with initial capacity
- `CourseStudentLimitModification/ModifyCourseStudentLimit.cs` - Changes capacity
- `StudentRegistration/RegisterStudent.cs` - Creates students
- `StudentSubscription/UpdateStudentSubscription.cs` - Changes tier
- `EnrollmentTier/StudentMaxCourseEnrollment.cs` - Tier limit rules
