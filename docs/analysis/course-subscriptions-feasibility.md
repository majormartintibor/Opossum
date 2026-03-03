# Analysis: "Course Subscriptions" DCB Example in Opossum

**Source:** https://dcb.events/examples/course-subscriptions/  
**Status:** ✅ Implemented — `Opossum.Samples.CourseManagement`  
**Scope:** Maps the DCB "Course Subscriptions" example to its Opossum implementation,
documents the domain enrichment, and notes the second implementation via the Aggregate pattern.

---

## 1. What the DCB Example Demonstrates

The "Course Subscriptions" example is the canonical showcase for DCB's primary use case:
**enforcing hard constraints that span multiple domain entities simultaneously**.

### Business Rules

1. A course has a fixed maximum capacity (maximum number of subscribers).
2. A student can subscribe to a course only if the course has capacity.
3. A student must not be subscribed to the same course twice.

### Why This Is Hard Without DCB

The constraint involves **two independent entities** — the course (capacity side) and the
student (already-subscribed side). Traditional approaches require either:

- Storing both course and student events in the same stream (wrong coupling), or
- Using a distributed lock / saga (complex, slow), or
- Accepting eventual consistency (unsafe — over-capacity enrolments can slip through)

DCB solves this by reading all relevant events in a single query, building a decision model
that spans both entities, and guarding the append with a condition that covers the union of
both queries. No lock, no saga, no coupling.

---

## 2. Domain Adaptation in `Opossum.Samples.CourseManagement`

The sample implements the same invariants with a richer domain:

| DCB Example | Opossum Sample |
|---|---|
| Course capacity check | `CourseCapacity` projection — `MaxCapacity` / `CurrentEnrollmentCount` |
| Student not already subscribed | `AlreadyEnrolled` projection — bool |
| _(not in spec)_ | `StudentEnrollmentLimit` projection — subscription tier limits (Basic: 2, Standard: 5, Professional: 10, Master: 25 courses) |

The extra tier-limit invariant demonstrates that DCB scales naturally to **three independent
entities** (course capacity, student tier, course-student pair) in a single decision model.

### Events Used

| Event | Tags | Purpose |
|---|---|---|
| `CourseCreatedEvent` | `courseId:{id}` | Establishes course and initial capacity |
| `CourseStudentLimitModifiedEvent` | `courseId:{id}` | Updates course max capacity |
| `StudentRegisteredEvent` | `studentId:{id}` | Registers student at Basic tier |
| `StudentSubscriptionUpdatedEvent` | `studentId:{id}` | Changes student tier |
| `StudentEnrolledToCourseEvent` | `courseId:{id}` + `studentId:{id}` | Records enrollment |

---

## 3. Mapping DCB Concepts to Opossum Primitives

### 3.1 Multi-Entity Decision Model — ✅ `BuildDecisionModelAsync` (3-projection overload)

The DCB spec's `buildDecisionModel({ courseCapacity, alreadyEnrolled })` maps to:

```csharp
var (courseCapacity, studentLimit, alreadyEnrolled, appendCondition) =
    await eventStore.BuildDecisionModelAsync(
        CourseEnrollmentProjections.CourseCapacity(command.CourseId),
        CourseEnrollmentProjections.StudentEnrollmentLimit(command.StudentId),
        CourseEnrollmentProjections.AlreadyEnrolled(command.CourseId, command.StudentId));
```

One `ReadAsync` call issues the union of all three queries. Each projection folds only its
own relevant events via `Query.Matches`. The returned `AppendCondition` spans all three —
a concurrent enrolment matching **any** of the three sub-queries invalidates the decision.

### 3.2 Course Capacity Projection — `IDecisionProjection<CourseCapacityState?>`

```csharp
public static IDecisionProjection<CourseCapacityState?> CourseCapacity(Guid courseId) =>
    new DecisionProjection<CourseCapacityState?>(
        initialState: null,     // null = course does not exist
        query: Query.FromItems(new QueryItem
        {
            EventTypes = [
                nameof(CourseCreatedEvent),
                nameof(CourseStudentLimitModifiedEvent),
                nameof(StudentEnrolledToCourseEvent)
            ],
            Tags = [new Tag("courseId", courseId.ToString())]
        }),
        apply: (state, evt) => evt.Event.Event switch
        {
            CourseCreatedEvent created =>
                new CourseCapacityState(created.MaxStudentCount, 0),
            CourseStudentLimitModifiedEvent modified when state is not null =>
                state with { MaxCapacity = modified.NewMaxStudentCount },
            StudentEnrolledToCourseEvent when state is not null =>
                state with { CurrentEnrollmentCount = state.CurrentEnrollmentCount + 1 },
            _ => state
        });
```

Key design: returning `null` for a non-existent course lets the command handler distinguish
"course full" from "course not found" without a separate existence check.

### 3.3 Already-Enrolled Projection — `IDecisionProjection<bool>`

The two-tag AND-scoped query is the heart of the "no duplicate subscription" invariant:

```csharp
public static IDecisionProjection<bool> AlreadyEnrolled(Guid courseId, Guid studentId) =>
    new DecisionProjection<bool>(
        initialState: false,
        query: Query.FromItems(new QueryItem
        {
            EventTypes = [nameof(StudentEnrolledToCourseEvent)],
            Tags =
            [
                new Tag("courseId", courseId.ToString()),
                new Tag("studentId", studentId.ToString())
            ]
        }),
        apply: (_, _) => true);
```

Because both tags are required (`AND` within `QueryItem.Tags`), this projection is triggered
**only** by events carrying _both_ the specific course ID and the specific student ID. A
concurrent enrolment of a different student in the same course does not match this query and
does not block this decision. That is DCB's targeted consistency boundary in action.

### 3.4 AppendCondition — ✅ Union of All Three Queries

`BuildDecisionModelAsync` sets:
- `FailIfEventsMatch` = OR-union of all three projection queries
- `AfterSequencePosition` = MAX position across all loaded events (or `null` if none)

This means: any concurrent write that matches **any** of the three projections (new course
capacity change, new student tier change, new enrolment for this exact pair) will reject the
append and trigger a retry.

### 3.5 Retry on Conflict — ✅ `ExecuteDecisionAsync`

The full read → decide → append cycle is wrapped in `ExecuteDecisionAsync`, which retries on
`AppendConditionFailedException` with exponential back-off.

---

## 4. Second Implementation: Event-Sourced Aggregate Pattern

The same invariants are also enforced via the Aggregate pattern in:
- `CourseAggregate.cs` + `CourseAggregateRepository.cs`
- `StudentAggregate.cs` + `StudentAggregateRepository.cs`
- `CourseEnrollmentService.cs` (domain service co-ordinating both aggregates)

The aggregate approach loads two independent aggregates and uses a compound
`AppendCondition` whose `AfterSequencePosition` is `MAX(course.Version, student.Version)`.
This is safe because Opossum's positions are globally monotonically increasing.

See `docs/analysis/aggregate-vs-dcb-comparison.md` for a full side-by-side analysis of
both approaches applied to this exact use case.

---

## 5. API Endpoints

`POST /courses/{courseId}/enrollments` — enroll a student (DCB Decision Model path)  
`POST /courses/aggregate/{courseId}/enroll` — enroll a student (Aggregate path)

---

## 6. Test Coverage

| Layer | File | What it covers |
|---|---|---|
| Integration | `CourseEnrollmentIntegrationTests.cs` | Happy path, duplicate, no course, no student, at capacity, tier limit |
| Integration | `CourseAggregateIntegrationTests.cs` | Aggregate path: tier-limit rejection, duplicate rejection |
| Unit | `CourseEnrollmentAggregateTests.cs` | `CourseAggregate.SubscribeStudent` invariants |
| Unit | `StudentAggregateTests.cs` | Reconstitution, tier folding, limit computation |

---

## 7. Summary

| DCB Concept | Opossum Primitive | Notes |
|---|---|---|
| Multi-entity decision model | `BuildDecisionModelAsync` (3 projections) | Single read, compound guard |
| Course capacity projection | `DecisionProjection<CourseCapacityState?>` | Null = course not found |
| Already-subscribed projection | `DecisionProjection<bool>` — AND-scoped 2-tag query | `(_, _) => true` pattern |
| Student limit projection | `DecisionProjection<StudentEnrollmentLimitState?>` | Opossum enrichment beyond spec |
| Concurrent-write guard | `AppendCondition.FailIfEventsMatch` | Union of all three queries |
| Retry | `ExecuteDecisionAsync` | Exponential back-off |
| Alternative implementation | Event-Sourced Aggregate + `CourseEnrollmentService` | Same invariants, different pattern |
