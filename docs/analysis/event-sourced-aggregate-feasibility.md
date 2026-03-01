# Event-Sourced Aggregate Example — Feasibility Analysis

> **Source:** [dcb.events — Event-Sourced Aggregate (DCB Approach)](https://dcb.events/examples/event-sourced-aggregate/#dcb-approach)
> **Scope:** Can we showcase this example in `Opossum.Samples.CourseManagement` using the current Opossum API?
> **Verdict:** ✅ Fully feasible with zero library changes required.

---

## What the DCB Example Demonstrates

The page shows a `CourseAggregate` (written in JavaScript) that:

1. Maintains private state: `id`, `capacity`, `numberOfSubscriptions`, and a `version` (position of the last consumed event).
2. Exposes two static factories: `create(id, title, capacity)` and `reconstitute(sequencedEvents)`.
3. Exposes business methods `changeCapacity(newCapacity)` and `subscribeStudent(studentId)` that validate invariants and internally record events before returning.
4. Exposes `pullRecordedEvents()` to flush accumulated events for persistence.

The key addition over the _traditional_ repository approach is the `DcbCourseRepository`, which:

- **Loads** the aggregate by querying the event store with a tag (`course:${courseId}`) instead of a named stream.
- **Saves** by appending the recorded events with an optimistic concurrency guard:
  ```js
  eventStore.append(eventsWithTags, {
    failIfEventsMatch: query,
    after: course.version,   // position of the last seen event
  })
  ```

This is the core DCB insight: a tag-scoped query replaces the traditional per-stream version lock, making the consistency boundary _dynamic_.

---

## API Mapping: DCB JavaScript → Opossum C#

| DCB JS concept | Opossum C# equivalent | Notes |
|---|---|---|
| `createQuery([{ tags }])` | `Query.FromTags(new Tag("courseId", id.ToString()))` | Key-value `Tag` replaces single-string `"course:c1"` — semantically identical |
| `eventStore.read(query)` | `await eventStore.ReadAsync(query, null)` | Returns `SequencedEvent[]` in ascending position order |
| `event.position` | `sequencedEvent.Position` | `long`, globally unique per store context |
| `failIfEventsMatch` + `after` | `new AppendCondition { FailIfEventsMatch = query, AfterSequencePosition = course.Version }` | Direct 1-to-1 mapping |
| Conflict → retry | `AppendConditionFailedException` | Already exists; callers catch and re-run the load → mutate → save cycle |
| Event payload | `IEvent` + `DomainEvent` + `NewEvent` | Full type round-trip via JSON serialisation already in place |

---

## Green — What Works Right Now

### 1. Core `IEventStore` API
`ReadAsync(Query, ReadOption[]?, long?)` and `AppendAsync(NewEvent[], AppendCondition?, CancellationToken)` are **exactly** what the DCB repository pattern needs. No wrappers required.

### 2. `AppendCondition`
`AppendCondition.FailIfEventsMatch` and `AppendCondition.AfterSequencePosition` map directly to the JS `{ failIfEventsMatch, after }` object. The guard semantics are identical.

### 3. Multi-event atomic append
`AppendAsync(NewEvent[], condition)` handles batches. When `create()` records two events (`CourseDefined` + `CourseCapacityChanged`), both are appended atomically in one call — no special support needed.

### 4. Tag-based routing
`Tag("courseId", courseId.ToString())` on every event for a given course, combined with `Query.FromTags(...)`, replicates the tag-scoped consistency boundary the DCB example relies on.

### 5. Position as version
`SequencedEvent.Position` is a `long` assigned by the store. The aggregate stores the position of its last-seen event as its `Version`. This is passed as `AfterSequencePosition` when saving — exactly the role of `course.version` in the JS code.

### 6. `AppendConditionFailedException`
Thrown automatically by Opossum when a concurrent write matches the condition's query after `AfterSequencePosition`. Callers catch it and retry the full load → mutate → save cycle.

### 7. Existing domain events
The sample already defines:

| DCB example event | Existing sample event |
|---|---|
| `CourseDefined` | `CourseCreatedEvent` |
| `CourseCapacityChanged` | `CourseStudentLimitModifiedEvent` |
| `StudentSubscribedToCourse` | `StudentEnrolledToCourseEvent` |

All three can be **reused directly** in the aggregate, eliminating duplication.

### 8. `ExecuteDecisionAsync` retry helper
`DecisionModelExtensions.ExecuteDecisionAsync` wraps a delegate and automatically retries on `AppendConditionFailedException` with exponential back-off. The aggregate command handler can use this to wrap the entire `load → command → save` cycle, matching the DCB spec's retry guidance.

---

## Yellow — Minor Design Choices

### 1. No aggregate base class
Opossum provides no `AggregateRoot<T>` base class. The `CourseAggregate` is implemented as **plain C#** — which is the entire point of the example (showing that no special framework support is needed). This is a feature, not a gap.

### 2. No repository abstraction
There is no `IRepository<T>` interface or DI registration helper. A `CourseAggregateRepository` would be a plain class registered manually in `Program.cs`. This is consistent with the rest of the sample app.

### 3. Global position as version
`SequencedEvent.Position` is a **store-wide** monotonically increasing number, not a per-aggregate counter. A course with 3 events might have `Version = 1042` if other events exist in the store. This is correct for the DCB guard (`AfterSequencePosition` is always global), but it differs from the traditional aggregate concept where `version == number of events for this aggregate`. Worth explaining clearly in the sample code comments.

### 4. Tag format convention
The DCB example tags all course events with `"course:c1"` (a single colon-separated string). Opossum uses `Tag(key, value)` pairs. The idiomatic Opossum mapping is `Tag("courseId", courseId.ToString())`, which already exists in the sample. Using the existing convention avoids a mismatch with the rest of the app.

### 5. Aggregate reconstitution vs Decision Projections
The existing `CourseEnrollment` feature uses **Decision Model projections** (`BuildDecisionModelAsync`) — the stateless, in-memory fold pattern. The aggregate example would sit next to this as **a different, equally valid approach to the same domain** — event-sourced state encapsulated inside an aggregate object, with the DCB repository replacing the stream-based one. The contrast is pedagogically valuable.

---

## Red — Nothing Is Blocking

There are **no gaps** in the Opossum API that would prevent implementing this example. All the primitives are present.

---

## Proposed Implementation Outline

### New files

```
Samples/Opossum.Samples.CourseManagement/
  CourseAggregate/
    CourseAggregate.cs          # Aggregate root (pure C#, no Opossum dependency)
    CourseAggregateRepository.cs # Repository using IEventStore directly
    CourseAggregateEndpoints.cs  # Minimal API endpoints
```

### `CourseAggregate.cs`
- Records a `long Version` property (position of last consumed `SequencedEvent`).
- `Create(Guid courseId, string name, int capacity)` — static factory that records `CourseCreatedEvent`.
- `Reconstitute(SequencedEvent[] events)` — replays events to rebuild state; sets `Version` to the last position.
- `ChangeCapacity(int newCapacity)` — validates that capacity ≥ current enrollment count, records `CourseStudentLimitModifiedEvent`.
- `SubscribeStudent(Guid studentId)` — validates that the course is not full, records `StudentEnrolledToCourseEvent`.
- `PullRecordedEvents()` — returns and clears the internal `List<IEvent>`.

### `CourseAggregateRepository.cs`
```
load(courseId):
  query = Query.FromTags(new Tag("courseId", courseId.ToString()))
  events = await eventStore.ReadAsync(query, null)
  return CourseAggregate.Reconstitute(events)

save(aggregate):
  query = Query.FromTags(new Tag("courseId", aggregate.CourseId.ToString()))
  condition = new AppendCondition {
      FailIfEventsMatch     = query,
      AfterSequencePosition = aggregate.Version == 0 ? null : aggregate.Version
  }
  newEvents = aggregate.PullRecordedEvents()
      .Select(e => e.ToDomainEvent()
          .WithTag("courseId", aggregate.CourseId.ToString())
          .WithTimestamp(DateTimeOffset.UtcNow)
          .Build())
      .ToArray()
  await eventStore.AppendAsync(newEvents, condition)
```

### Endpoints (suggested)
| Method | Route | Purpose |
|---|---|---|
| `POST` | `/courses/aggregate` | Create a course via the aggregate |
| `PATCH` | `/courses/aggregate/{courseId}/capacity` | Change capacity via the aggregate |
| `POST` | `/courses/aggregate/{courseId}/subscribe` | Subscribe a student via the aggregate |

These sit alongside the existing DCB Decision Model endpoints to highlight the two complementary patterns.

### Retry in command handler
Wrap the load → mutate → save cycle in `ExecuteDecisionAsync` (or a simple `catch AppendConditionFailedException` loop) to fulfil the DCB spec's retry requirement.

---

## Summary

| Concern | Status |
|---|---|
| Core event store operations (`Read`, `Append`) | ✅ Fully supported |
| Optimistic concurrency guard (`AppendCondition`) | ✅ Direct mapping |
| Multi-event batch append | ✅ Supported |
| Position-as-version | ✅ Works; global vs per-aggregate difference needs documentation |
| Event reuse (existing sample events) | ✅ All three events already exist |
| Aggregate class (pure C#) | ✅ No library changes needed |
| Repository class (plain class + DI) | ✅ No library changes needed |
| Retry on concurrency conflict | ✅ Via `ExecuteDecisionAsync` or manual catch |
| Breaking changes to existing API | ✅ None |
| New library features required | ✅ None |
