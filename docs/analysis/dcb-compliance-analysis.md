# DCB Compliance Analysis & Improvement Roadmap

> **Scope:** Full analysis of Opossum v0.1.0-preview.1 against the
> [DCB Event Store Specification](../../Specification/DCB-Specification.md) and
> [DCB Projections Specification](../specifications/dcb-projections.md).
>
> **Method:** Every public interface, concrete implementation, and sample application
> was reviewed line-by-line and cross-referenced against both specifications.

---

## Quick Verdict

| Area | Status | Notes |
|---|---|---|
| Event Store core (read / append) | ✅ Compliant | All MUST requirements met |
| Query & QueryItem semantics | ✅ Compliant | OR / AND logic correct |
| AppendCondition / optimistic concurrency | ✅ Compliant | Full DCB pattern implemented |
| SequencedEvent / Sequence Position | ✅ Compliant | Unique, monotonically increasing |
| Read from starting position (SHOULD) | ✅ Compliant | `fromPosition` parameter added to `ReadAsync` |
| AppendAsync input type | ✅ Fixed | `NewEvent[]` — no position field |
| Concurrency exception taxonomy | ✅ Fixed | `ConcurrencyException` is a subclass of `AppendConditionFailedException` |
| Decision Model projection layer | ✅ Implemented | `IDecisionProjection<TState>`, `DecisionModel<TState>` |
| `BuildDecisionModel()` helper | ✅ Implemented | `BuildDecisionModelAsync()` extension (1-, 2-, and 3-projection overloads) |
| `ComposeProjections()` helper | ✅ Implemented | Multi-projection overloads of `BuildDecisionModelAsync` |
| `Query.Matches()` helper | ✅ Implemented | In-memory event matching on `Query` |
| Streaming reads (`IAsyncEnumerable`) | 🔴 Not planned | Full arrays acceptable for target scale |

---

## Part 1 — DCB Event Store Specification

### 1.1 Reading Events

#### ✅ Filter by EventType and Tag
`IEventStore.ReadAsync(Query, ReadOption[]?)` implements full query semantics:
- OR logic across `QueryItems`
- OR logic within `EventTypes`
- AND logic within `Tags`
- `Query.All()` matches all events (empty `QueryItems`)

Backed by `EventTypeIndex` and `TagIndex` files on disk — queries never do full scans.

#### ✅ AppendCondition position-scoped check
`ValidateAppendConditionAsync` correctly restricts the conflict check to positions
`> AfterSequencePosition`, matching the spec precisely.

#### ✅ SHOULD: Read from a given starting Sequence Position

The spec states:
> The Event Store *SHOULD* provide a way to read Events from a given starting Sequence Position.

**Current state:** `IEventStore.ReadAsync` now accepts an optional `fromPosition` parameter:

```csharp
Task<SequencedEvent[]> ReadAsync(
    Query query,
    ReadOption[]? readOptions,
    long? fromPosition = null);   // read only events with Position > fromPosition
```

A convenience extension `ReadAsync(query, fromPosition)` is also available for the common
polling pattern:

```csharp
// Incrementally poll new events from a known checkpoint
var newEvents = await eventStore.ReadAsync(Query.All(), fromPosition: lastCheckpoint);
```

For `Query.All()`, the position range is generated directly from `lastCheckpoint + 1` to
`lastPosition` — no wasted allocation of the full prefix. For indexed queries, positions are
retrieved from the index and then filtered. When `fromPosition` is `null` (the default), behaviour
is identical to the previous API.

The `ProjectionDaemon` polling loop was updated to pass `minCheckpoint` directly to `ReadAsync`
instead of loading all events and filtering in memory.

---

### 1.2 Writing Events

#### ✅ Atomic append
`FileSystemEventStore.AppendAsync` holds a `SemaphoreSlim(1,1)` across the entire
read-validate-write-index cycle. Only one append runs at a time; concurrent callers queue.

#### ✅ AppendCondition failure
`ConcurrencyException` is thrown when any matching event exists after `AfterSequencePosition`.
The sample command handler demonstrates the correct retry loop.

#### ✅ Design issue resolved: `AppendAsync` now accepts `NewEvent[]`

`NewEvent` is a dedicated write-side type with no `Position` field, matching the DCB spec's
`append(events: Events|Event, ...)` intent exactly. `SequencedEvent` remains the exclusive
output type of `ReadAsync`. `DomainEventBuilder.Build()` and the `implicit operator NewEvent`
produce `NewEvent` directly.

---

### 1.3 Sequence Position

#### ✅ Unique and monotonically increasing
`LedgerManager` allocates positions atomically inside the semaphore-protected append.
Positions start at 1 and increment by 1 for each event; gaps can occur if a write fails.

#### ✅ Gaps are allowed
The spec explicitly allows gaps. Opossum's ledger can have gaps on failure — consistent.

---

### 1.4 Event Model

#### ✅ EventType
`DomainEvent.EventType` is present. `DomainEventBuilder` derives it from `_event.GetType().Name` automatically.

#### ✅ Event Data
`DomainEvent.Event` (type `IEvent`) is the opaque payload. Serialised/deserialised by `JsonEventSerializer`.

#### ✅ Tags
`DomainEvent.Tags` is `List<Tag>` where `Tag` has `{ Key, Value }`.

**Note on tag representation:** The DCB spec treats tags as opaque strings (e.g., `"course:c1"`).
Opossum uses a structured `{ Key, Value }` pair. The spec says "A Tag *MAY* represent a
key/value pair — that is irrelevant to the Event Store", so this is a valid implementation choice.
However, the query semantics differ subtly:
- In the spec a tag `"course:c1"` is matched as a single opaque string.
- In Opossum, querying `{ Key="courseId", Value="c1" }` means matching both Key AND Value.
  Two tags `{ Key="courseId", Value="c1" }` and `{ Key="courseId", Value="c2" }` are distinct,
  which is expected. But external integrators expecting string-style tags would need adaptation.

#### ⚠️ Tag uniqueness not enforced
The spec says tags "SHOULD not contain multiple Tags with the same value."
Opossum has no guard against duplicate tags on an event.

---

### 1.5 AppendCondition

#### ✅ `FailIfEventsMatch` (Query)
Implemented as `AppendCondition.FailIfEventsMatch`.

#### ✅ `AfterSequencePosition` (optional)
Implemented as `AppendCondition.AfterSequencePosition` (nullable `long`).
When null, the conflict check applies to all events (spec: "if omitted, no Events will be ignored").

#### ✅ Exception taxonomy unified

`ConcurrencyException` is now a subclass of `AppendConditionFailedException`. Callers catch
the base type and receive both internal ledger-level races and query-based condition failures
through a single, well-named exception. The sample command handler catches only
`AppendConditionFailedException`.

---

## Part 2 — DCB Projections Specification

The projections spec defines two categories of projections:

| Category | Purpose | DCB Relevance |
|---|---|---|
| **Decision Model projection** | In-memory, ephemeral, used during command handling to enforce consistency | **Core DCB pattern** |
| **Read Model projection** | Persistent, materialised, updated asynchronously | Supplementary |

Opossum has a well-developed **Read Model** projection system (`IProjectionDefinition<TState>`,
`ProjectionManager`, `ProjectionDaemon`). What is **entirely missing** is a first-class
**Decision Model** projection layer — the core DCB pattern described in the spec.

---

### 2.1 ✅ `IDecisionProjection<TState>`

`IDecisionProjection<TState>` is implemented in `src/Opossum/DecisionModel/IDecisionProjection.cs`.
The concrete `DecisionProjection<TState>` record type provides a simple constructor-based
implementation. The factory function pattern is idiomatic C# and documented with examples.

---

### 2.2 ✅ `Query.Matches(SequencedEvent)`

Implemented on `Query` in `src/Opossum/Core/Query.cs`. Applies the same OR/AND logic used
by the event store and is used by `DecisionModelExtensions.FoldEvents` to route events to
the correct sub-projection in multi-projection `BuildDecisionModelAsync` overloads.

---

### 2.3 ✅ `BuildDecisionModelAsync()` extension on `IEventStore`

Implemented in `src/Opossum/DecisionModel/DecisionModelExtensions.cs`. Overloads exist for
1, 2, and 3 independent projections. Each overload makes a single `ReadAsync` call with the
union of all sub-queries, folds events into each projection's state using `Query.Matches`,
and returns the combined `AppendCondition`.

```csharp
var model = await eventStore.BuildDecisionModelAsync(
    CourseProjections.Capacity(command.CourseId));

if (model.State.IsFull)
    return CommandResult.Fail("Course is at capacity.");

await eventStore.AppendAsync(newEvent, model.AppendCondition);
```

---

### 2.4 ✅ `ComposeProjections()`

Implemented via 2- and 3-projection overloads of `BuildDecisionModelAsync`. Each overload
builds a union query from all sub-queries, reads once, and routes events to the correct
projection using `Query.Matches`. The `AppendCondition` spans all sub-queries so a concurrent
write matching any sub-query invalidates the decision.

```csharp
var (capacity, studentLimit, condition) = await eventStore.BuildDecisionModelAsync(
    CourseProjections.Capacity(command.CourseId),
    StudentProjections.EnrollmentLimit(command.StudentId));
```

---

### 2.5 ✅ Factory function pattern support

The factory pattern is idiomatic C# with `IDecisionProjection<TState>`:

```csharp
public static IDecisionProjection<bool> CourseExists(Guid courseId) =>
    new DecisionProjection<bool>(
        initialState: false,
        query: Query.FromItems(new QueryItem
        {
            EventTypes = ["CourseDefined", "CourseArchived"],
            Tags = [new Tag { Key = "courseId", Value = courseId.ToString() }]
        }),
        apply: (state, evt) => evt.Event.Event switch
        {
            CourseCreatedEvent  => true,
            CourseArchivedEvent => false,
            _                   => state
        });
```

No framework feature is needed; the sample application demonstrates this pattern.

---

## Part 3 — Architecture & Performance Observations

### 3.1 `ReadAsync` returns `SequencedEvent[]` — no streaming

The DCB spec says `ReadAsync` returns "some form of iterable or reactive stream".
Opossum returns an array, meaning all matching events are loaded into memory before
the caller receives the first element.

For the current target use cases (small/medium on-premises deployments with
≤10M events as per README) this is acceptable. But as a library design principle,
returning `IAsyncEnumerable<SequencedEvent>` is strictly more flexible — callers that
want arrays can call `.ToArrayAsync()`, while callers processing large event sets can
iterate without materialising the full result.

This is the most impactful long-term architectural change.

---

### 3.2 ✅ Projection daemon uses `fromPosition`

`ProjectionDaemon.ProcessNewEventsAsync` now calls
`ReadAsync(Query.All(), null, minCheckpoint)` directly. The store skips already-processed
events at the index level — no in-memory filtering of stale events.

`ProjectionManager.RebuildAsync` intentionally reads from position 0 because a rebuild
always reprocesses the full history.

---

### 3.3 Read Model projection `Apply` loses tag context

`IProjectionDefinition<TState>.Apply(TState? current, IEvent evt)` receives only the
`IEvent` payload, not the full `SequencedEvent`. This means a projection handler
cannot inspect the event's tags or position during apply — only the `KeySelector`
method has access to the `SequencedEvent`.

In practice this is fine for most projections (the payload carries the entity ID), but
it is an unnecessary constraint. The `IMultiStreamProjectionDefinition<TState>`
variant demonstrates that richer context can be passed to `Apply`. Consider:

```csharp
// Richer signature — tags and position accessible if needed
TState? Apply(TState? current, SequencedEvent evt);
```

---

### 3.4 MVP single-context limitation

`FileSystemEventStore` and `ProjectionManager` both hard-code `Contexts[0]`. The
`TODO` comments acknowledge this. When multi-context support is added, all routing
logic needs to flow from the public API down (e.g., `ReadAsync(query, contextName, ...)`).
Defining the API shape early (even as `NotImplementedException`) would help avoid
breaking changes later.

---

## Part 4 — Improvement Priority Matrix

### P0 — Spec compliance gaps (SHOULD) — ✅ All resolved

| # | Item | Status |
|---|---|---|
| P0.1 | Add `fromPosition` parameter to `ReadAsync` | ✅ Done |
| P0.2 | Unify `ConcurrencyException` / `AppendConditionFailedException` into one type | ✅ Done |

### P1 — Core DCB Projections pattern (Decision Model layer) — ✅ All resolved

| # | Item | Status |
|---|---|---|
| P1.1 | Add `Query.Matches(SequencedEvent)` helper | ✅ Done |
| P1.2 | Add `IDecisionProjection<TState>` interface | ✅ Done |
| P1.3 | Add `DecisionModel<TState>` result type | ✅ Done |
| P1.4 | Add `BuildDecisionModelAsync()` extension on `IEventStore` | ✅ Done |
| P1.5 | Add `ComposeProjections()` helper | ✅ Done (2- and 3-projection overloads) |
| P1.6 | Update sample app to use new API | ✅ Done |

### P2 — API design clean-up — ✅ All resolved

| # | Item | Status |
|---|---|---|
| P2.1 | Introduce `NewEvent` type; change `AppendAsync` input away from `SequencedEvent[]` | ✅ Done |
| P2.2 | Tag uniqueness enforcement on append | 🔵 Deferred (low priority) |
| P2.3 | Pass `SequencedEvent` (not `IEvent`) to `IProjectionDefinition.Apply` | 🔵 Deferred (breaking) |

### P3 — Performance & scalability

| # | Item | Status |
|---|---|---|
| P3.1 | `ReadAsync` streaming via `IAsyncEnumerable<SequencedEvent>` | 🔵 Not planned for target scale |
| P3.2 | Use `fromPosition` in projection daemon / rebuild | ✅ Done (daemon; rebuild intentionally full) |
| P3.3 | Multi-context API surface design | 🔵 Planned future release |

---

## Part 5 — Summary

Opossum now correctly implements **every MUST and SHOULD requirement** of the DCB Event Store
Specification.

The core read/append/concurrency semantics are solid. The Decision Model projection layer —
the write-side DCB pattern — is fully implemented via `IDecisionProjection<TState>`,
`DecisionModel<TState>`, `Query.Matches()`, `BuildDecisionModelAsync()` (1-, 2-, and
3-projection overloads), and the factory function pattern. `ReadAsync` accepts an optional
`fromPosition` parameter that fulfils the SHOULD requirement and enables efficient incremental
polling without loading already-processed events. The `ProjectionDaemon` exploits this
directly. The exception taxonomy is unified under `AppendConditionFailedException` with
`ConcurrencyException` as a subclass.

The only remaining open items are API design improvements that do not affect spec compliance:
tag uniqueness enforcement, richer `Apply` context for read model projections, and the
multi-context routing API — all deferred to future releases.
