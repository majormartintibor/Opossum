# Aggregate Pattern vs DCB Decision Model — Side-by-Side Comparison

This document explains the architectural difference between the two write-side patterns
available in `Opossum.Samples.CourseManagement`, using the **student enrollment** use case
as the concrete example.

Both patterns write to the same event log and produce identical results. The difference
is entirely in how business decisions are structured in code.

---

## The Enrollment Use Case

Enrolling a student in a course requires three independent invariants to hold simultaneously:

| # | Invariant | Relevant state |
|---|---|---|
| 1 | The course must exist and not be at capacity | Course events (`courseId` tag) |
| 2 | The student must be registered and not exceed their tier limit | Student events (`studentId` tag) |
| 3 | The student must not already be enrolled in this course | Events tagged with both `courseId` **and** `studentId` |

Enforcing all three **atomically** — so no concurrent write can sneak between the check and
the append — is the core challenge.

---

## DCB Decision Model (`CourseEnrollment/EnrollStudentToCourseCommand.cs`)

```csharp
var (courseCapacity, studentLimit, alreadyEnrolled, appendCondition) =
    await eventStore.BuildDecisionModelAsync(
        CourseEnrollmentProjections.CourseCapacity(command.CourseId),
        CourseEnrollmentProjections.StudentEnrollmentLimit(command.StudentId),
        CourseEnrollmentProjections.AlreadyEnrolled(command.CourseId, command.StudentId));

if (courseCapacity is null)       return CommandResult.Fail("Course does not exist.");
if (studentLimit is null)         return CommandResult.Fail("Student is not registered.");
if (alreadyEnrolled)              return CommandResult.Fail("Student is already enrolled.");
if (courseCapacity.IsFull)        return CommandResult.Fail("Course is at capacity.");
if (studentLimit.IsAtLimit)       return CommandResult.Fail("Student is at tier limit.");

await eventStore.AppendAsync(enrollmentEvent, appendCondition);
```

**What `BuildDecisionModelAsync` does internally:**
1. Issues a **single** `ReadAsync` call with the **union** of all three projection queries.
2. Folds each projection in memory over its own relevant events.
3. Constructs an `AppendCondition` where:
   - `FailIfEventsMatch` = the union query (OR across all three)
   - `AfterSequencePosition` = MAX position across all returned events

The compound guard is **automatic** — it spans every entity involved in the decision.

### What you need

| Asset | Count |
|---|---|
| Projection classes | 3 (one per invariant) |
| Repository methods | 0 (no aggregates to load) |
| Lines of new infrastructure | ~0 (standard `BuildDecisionModelAsync` call) |

---

## Aggregate Pattern (`CourseAggregate/`)

To achieve the same three-invariant enforcement, the aggregate approach requires:

### 1. Two aggregate classes

**`CourseAggregate`** — encapsulates course-local state: capacity, enrollment count, enrolled
student IDs. `SubscribeStudent()` enforces capacity and duplicate enrollment.

**`StudentAggregate`** — encapsulates student-local state: tier and course enrollment count.
`IsAtEnrollmentLimit` exposes the tier-limit check.

### 2. A domain service (`CourseEnrollmentService`)

Neither repository should know about the other aggregate — that would contaminate each with cross-entity concerns. Instead, a **domain service** owns the coordination:

```csharp
public sealed class CourseEnrollmentService(
    CourseAggregateRepository courseRepository,
    StudentAggregateRepository studentRepository)
```

Each repository loads only its own aggregate. The service checks both invariants, then delegates the save back to `CourseAggregateRepository` with a compound condition.

### 3. Two independent reads — why this is safe

Loading the two aggregates via two separate `ReadAsync` calls looks like it should introduce a race window, but it is **safe** due to a property of the store:

> Store positions are globally monotonically increasing across **all** event types.

If `course.Version = 100` and `student.Version = 50`, positions 51–100 are already occupied by course events. No student event can ever appear at those positions retrospectively. Any event appended to either entity **after** both reads will therefore have a position strictly greater than `MAX(100, 50) = 100`.

The compound `AppendCondition` uses exactly that MAX as its `AfterSequencePosition`:

```csharp
var compoundCondition = new AppendCondition
{
    FailIfEventsMatch = Query.FromItems(
        new QueryItem { Tags = [new Tag("courseId", courseId.ToString())] },
        new QueryItem { Tags = [new Tag("studentId", studentId.ToString())] }),
    AfterSequencePosition = Math.Max(course.Version, student.Version)
};

await courseRepository.SaveAsync(course, compoundCondition, cancellationToken);
```

Concurrent writes to either entity after the reads are always detected.

### 4. `SaveAsync` accepts an optional condition override

`CourseAggregateRepository.SaveAsync` is extended with an optional `AppendCondition?` parameter. When the service passes a compound condition, the repository uses it; when `null`, it builds the default course-scoped condition as usual. This keeps the repository clean — it never needs to know about `StudentAggregate`:

```csharp
// Default course-only save — no change for other callers:
await repository.SaveAsync(aggregate);

// Compound save via domain service:
await repository.SaveAsync(aggregate, compoundCondition, cancellationToken);
```

### What you need

| Asset | Count |
|---|---|
| Aggregate classes | 2 (`CourseAggregate` + `StudentAggregate`) |
| Repository classes | 2 (`CourseAggregateRepository` + `StudentAggregateRepository`) |
| Domain service | 1 (`CourseEnrollmentService`) |
| Compound condition construction | Manual — in `CourseEnrollmentService` |
| Extra lines of infrastructure | ~100 |

---

## Side-by-Side Summary

| Concern | DCB Decision Model | Aggregate Pattern |
|---|---|---|
| **Course capacity** | `CourseCapacity` projection | `CourseAggregate.Capacity` |
| **Student tier limit** | `StudentEnrollmentLimit` projection | `StudentAggregate.IsAtEnrollmentLimit` |
| **Duplicate check** | `AlreadyEnrolled` projection | `CourseAggregate._enrolledStudentIds` |
| **Cross-entity read** | Automatic — single `BuildDecisionModelAsync` read | Two independent reads via separate repos; safe because positions are globally monotonic |
| **Compound condition** | Automatic — union of projection queries | Manual — `CourseEnrollmentService` constructs `MAX(course.Version, student.Version)` |
| **Coordination owner** | None needed | `CourseEnrollmentService` domain service |
| **Objects to reconstitute** | None (ephemeral, stateless projections) | Two full aggregate objects |
| **Bespoke infrastructure** | None | ~100 lines across two new methods |
| **Single-aggregate commands** | Same effort | Simpler (`LoadAsync` + `SaveAsync`) |

---

## When to choose each

### Prefer **DCB Decision Model** when

- Business rules involve more than one entity type (as in this example).
- The consistency boundary is naturally defined by the *query* rather than an *object*.
- You want minimal infrastructure and maximum flexibility to compose invariants ad hoc.
- The domain is exploratory — projections are cheap to add, change, or remove.

### Prefer **Aggregate Pattern** when

- A rich set of invariants is *entirely internal* to one entity (e.g. complex order-line
  pricing rules that never reference another entity).
- Your team has strong DDD/OOP experience and prefers domain objects over data-folding
  functions.
- You want an explicit in-memory object graph for readability and testability.

### The key insight

Both patterns rely on **the same DCB primitive** — `AppendCondition` with a scoped
`FailIfEventsMatch` query — for concurrency control. The aggregate pattern does not escape
DCB thinking; it makes the mechanism explicit and delegates the wiring to the developer.
Opossum gives you both paths.

---

## Event log

Both patterns **share a single event log**. The same `CourseCreatedEvent`,
`StudentRegisteredEvent`, `StudentEnrolledToCourseEvent`, etc. are used by both approaches.
You do not need separate event stores — or even separate event types — to support both
patterns in the same application.

This is the flexibility Opossum provides: the event store is the source of truth; the
pattern on top is your choice.
