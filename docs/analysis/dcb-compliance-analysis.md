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
| Read from starting position (SHOULD) | ⚠️ Missing | Spec SHOULD – not yet implemented |
| AppendAsync input type | ⚠️ Design issue | Accepts `SequencedEvent[]`; position ignored |
| Concurrency exception taxonomy | ⚠️ Inconsistency | Two exception types for same condition |
| Decision Model projection layer | 🔴 Missing | Core DCB pattern has no first-class API |
| `BuildDecisionModel()` helper | 🔴 Missing | Manual wiring required in every handler |
| `ComposeProjections()` helper | 🔴 Missing | Manual composition required |
| `Query.Matches()` helper | 🔴 Missing | No in-memory event matching |
| Streaming reads (`IAsyncEnumerable`) | 🔴 Missing | Full arrays loaded into memory |

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

#### ⚠️ SHOULD: Read from a given starting Sequence Position

The spec states:
> The Event Store *SHOULD* provide a way to read Events from a given starting Sequence Position.

**Current state:** `ReadAsync` always reads from position 0. There is no `fromPosition` / `afterPosition` parameter.

**Impact:**
- Projection incremental updates (daemon polling) must re-query all matching events and
  filter in memory by position — this gets slower as the event log grows.
- Any application that wants to "tail" the event log from a known position must
  load all events from the beginning and skip.
- A `ProjectionManager.RebuildAsync` that was interrupted mid-way always restarts from
  zero because there is no way to resume from the checkpoint position.

**Proposed addition:**
```csharp
// IEventStore
Task<SequencedEvent[]> ReadAsync(
    Query query,
    ReadOption[]? readOptions,
    long? fromPosition = null);   // NEW: read only events with Position > fromPosition
```

---

### 1.2 Writing Events

#### ✅ Atomic append
`FileSystemEventStore.AppendAsync` holds a `SemaphoreSlim(1,1)` across the entire
read-validate-write-index cycle. Only one append runs at a time; concurrent callers queue.

#### ✅ AppendCondition failure
`ConcurrencyException` is thrown when any matching event exists after `AfterSequencePosition`.
The sample command handler demonstrates the correct retry loop.

#### ⚠️ Design issue: `AppendAsync` accepts `SequencedEvent[]` but overwrites Position

The spec's `append` receives *Events* (unsequenced). Opossum's public interface receives
`SequencedEvent[]`, meaning callers must construct an object that includes a `Position`
field — yet that field is silently overwritten by the store:

```csharp
// FileSystemEventStore.AppendAsync – position is always reassigned
events[i].Position = startPosition + i;
```

`DomainEventBuilder.Build()` works around this by setting `Position = 0` as a placeholder,
and `AppendEventAsync` / `AppendEventsAsync` extensions do the same.

**Problems this causes:**
- The input type misleads callers into thinking they control the position.
- `SequencedEvent` (an output concept from `ReadAsync`) doubles as an input type
  for `AppendAsync`, breaking the clear read-vs-write model.
- The `implicit operator SequencedEvent(DomainEventBuilder)` hides the conversion.

**Proposed fix — two-level model:**
```csharp
// New type for append input (no Position)
public class NewEvent
{
    public required IEvent Event { get; set; }
    public List<Tag> Tags { get; set; } = [];
    public Metadata? Metadata { get; set; }
}

// Cleaner interface
public interface IEventStore
{
    Task<SequencedEvent[]> ReadAsync(Query query, ReadOption[]? readOptions, long? fromPosition = null);
    Task AppendAsync(NewEvent[] events, AppendCondition? condition = null);
}
```

This matches the spec's `append(events: Events|Event, condition?: AppendCondition)` intent exactly.

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

#### ⚠️ Two exception types for one concept

Both `ConcurrencyException` and `AppendConditionFailedException` exist, and the sample
command handler catches both:

```csharp
catch (ConcurrencyException) when (attempt < MaxRetryAttempts - 1)   { ... }
catch (AppendConditionFailedException) when (attempt < MaxRetryAttempts - 1) { ... }
```

This is confusing. Per the spec there is exactly one failure mode: the append condition
was violated. The library should throw a single, well-named exception. `AppendConditionFailedException`
is the right name; `ConcurrencyException` should either be removed or made a subclass.

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

### 2.1 Missing: `IDecisionProjection<TState>`

The spec's projection type is:

```typescript
type Projection<S> = {
  initialState: S
  apply(state: S, event: SequencedEvent): S
  query: Query
}
```

In Opossum today, this pattern is implemented **manually** in each command handler.
`CourseEnrollmentAggregate` is a decision model projection, but it is:
- Not expressed through any Opossum interface
- Not composable with other projections
- Requires manual query construction (`Queries.GetCourseEnrollmentQuery()`)
- Requires manual `AppendCondition` construction

**Proposed interface:**
```csharp
/// <summary>
/// Defines an in-memory, ephemeral projection used to build a Decision Model for
/// enforcing consistency during command handling (the DCB write-side pattern).
/// </summary>
public interface IDecisionProjection<TState>
{
    /// <summary>Starting state before any events are applied.</summary>
    TState InitialState { get; }

    /// <summary>
    /// The query that selects events relevant to this projection.
    /// Used to load events from the store and as the FailIfEventsMatch query.
    /// </summary>
    Query Query { get; }

    /// <summary>Folds a single event into the current state.</summary>
    TState Apply(TState state, SequencedEvent evt);
}
```

---

### 2.2 Missing: `Query.Matches(SequencedEvent)`

The spec's library exposes `projection.query.matchesEvent(event)` for in-memory filtering.
Opossum's `Query` has no such helper. This is needed when:

- Unit-testing command handlers without touching the file system
- Filtering an already-loaded batch of events for a specific sub-projection
- Implementing `ComposeProjections` (see below)

**Proposed addition to `Query`:**
```csharp
/// <summary>
/// Returns true if the given event matches this query.
/// Implements the same OR/AND logic used by the event store.
/// </summary>
public bool Matches(SequencedEvent evt)
{
    // Query.All() matches everything
    if (QueryItems.Count == 0) return true;

    return QueryItems.Any(item =>
    {
        var typeMatch = item.EventTypes.Count == 0 ||
                        item.EventTypes.Contains(evt.Event.EventType);
        var tagMatch  = item.Tags.Count == 0 ||
                        item.Tags.All(t => evt.Event.Tags.Any(et =>
                            et.Key == t.Key && et.Value == t.Value));
        return typeMatch && tagMatch;
    });
}
```

---

### 2.3 Missing: `BuildDecisionModel()` extension on `IEventStore`

The spec's central helper:

```js
const { state, appendCondition } = buildDecisionModel(eventStore, {
  courseExists: CourseExistsProjection("c1"),
  courseTitle:  CourseTitleProjection("c1"),
})
```

In Opossum today, every command handler manually performs all three steps:

```csharp
// Step 1: manually build query
var query = command.GetCourseEnrollmentQuery();
// Step 2: manually read
var events = await eventStore.ReadAsync(enrollmentQuery, ReadOption.None);
// Step 3: manually fold
var aggregate = events.OrderBy(e => e.Position)
    .Select(e => e.Event.Event)
    .Aggregate(new CourseEnrollmentAggregate(...), (a, e) => a.Apply(e));
// Step 4: manually build AppendCondition
var condition = new AppendCondition
{
    AfterSequencePosition = events.Length > 0 ? events.Max(e => e.Position) : null,
    FailIfEventsMatch = enrollmentQuery
};
```

This is ~15 lines of plumbing that every command handler must duplicate. The spec
reduces this to a single call that returns `(state, appendCondition)`.

**Proposed extension:**
```csharp
public static class DecisionModelExtensions
{
    /// <summary>
    /// Builds a Decision Model by reading relevant events, folding them into state,
    /// and returning the AppendCondition that guards the decision.
    /// </summary>
    public static async Task<DecisionModel<TState>> BuildDecisionModelAsync<TState>(
        this IEventStore eventStore,
        IDecisionProjection<TState> projection,
        CancellationToken cancellationToken = default)
    {
        var events = await eventStore.ReadAsync(projection.Query, null)
            .ConfigureAwait(false);

        var state = events
            .OrderBy(e => e.Position)
            .Aggregate(projection.InitialState, projection.Apply);

        var appendCondition = new AppendCondition
        {
            FailIfEventsMatch = projection.Query,
            AfterSequencePosition = events.Length > 0
                ? events.Max(e => e.Position)
                : null
        };

        return new DecisionModel<TState>(state, appendCondition);
    }
}

public sealed record DecisionModel<TState>(TState State, AppendCondition AppendCondition);
```

---

### 2.4 Missing: `ComposeProjections()`

The spec recommends composing multiple single-purpose projections instead of building
monolithic ones:

```js
const compositeProjection = composeProjections({
  courseExists: CourseExistsProjection("c1"),
  courseTitle:  CourseTitleProjection("c1"),
})
```

**Why this matters:** Each small projection has its own `Query`, so only events relevant
to at least one sub-projection are loaded. The combined query is the union (OR) of all
sub-queries — the consistency boundary is as narrow as the business decision requires.

Without `ComposeProjections`, developers are tempted to write one large projection that
combines multiple concerns (which is what `CourseEnrollmentAggregate` currently does):
loading more events than strictly necessary for any single invariant.

**Proposed API:**
```csharp
/// <summary>
/// Combines multiple IDecisionProjection instances into one composite projection.
/// The composite query is the union of all sub-queries.
/// The composite state is a dictionary keyed by projection name.
/// </summary>
public static IDecisionProjection<IReadOnlyDictionary<string, object?>> ComposeProjections(
    IReadOnlyDictionary<string, IDecisionProjection<object?>> projections)
```

A strongly-typed variant using source generators or a builder API would provide
better ergonomics for the typical 2–5 projection composition case.

---

### 2.5 Missing: Factory function pattern support

The spec shows projection factories that inject dynamic entity identifiers:

```js
const CourseExistsProjection = (courseId) => createProjection({
  tagFilter: [`course:${courseId}`]
})
```

In Opossum today, the equivalent is implemented as a static class with an extension
method returning a hand-built `Query` (`Queries.GetCourseEnrollmentQuery()`).
With `IDecisionProjection<TState>` this becomes a conventional C# pattern:

```csharp
// The factory pattern becomes idiomatic C#
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

No new framework feature is needed here; this is a design guidance / documentation gap.

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

### 3.2 Projection rebuild ignores checkpoint position

`ProjectionManager.RebuildAsync` builds the query from event types alone:

```csharp
var query = Query.FromEventTypes(registration.EventTypes);
var events = await _eventStore.ReadAsync(query, null);
```

Even with checkpoint support, the daemon's incremental update passes all events since
last-processed position through an in-memory position filter, rather than asking the
store to start reading from that position. Once a `fromPosition` parameter exists on
`ReadAsync` (see §1.1), the projection daemon and rebuild path should be updated to
pass the checkpoint position directly to the store, eliminating the unnecessary load
of already-processed events.

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

### P0 — Spec compliance gaps (SHOULD)

| # | Item | Effort |
|---|---|---|
| P0.1 | Add `fromPosition` parameter to `ReadAsync` | Medium |
| P0.2 | Unify `ConcurrencyException` / `AppendConditionFailedException` into one type | Small |

### P1 — Core DCB Projections pattern (Decision Model layer)

| # | Item | Effort |
|---|---|---|
| P1.1 | Add `Query.Matches(SequencedEvent)` helper | Small |
| P1.2 | Add `IDecisionProjection<TState>` interface | Small |
| P1.3 | Add `DecisionModel<TState>` result type | Small |
| P1.4 | Add `BuildDecisionModelAsync()` extension on `IEventStore` | Small |
| P1.5 | Add `ComposeProjections()` helper | Medium |
| P1.6 | Update sample app `CourseEnrollmentAggregate` to use new API | Medium |

### P2 — API design clean-up

| # | Item | Effort |
|---|---|---|
| P2.1 | Introduce `NewEvent` type; change `AppendAsync` input away from `SequencedEvent[]` | Medium |
| P2.2 | Tag uniqueness enforcement on append | Small |
| P2.3 | Pass `SequencedEvent` (not `IEvent`) to `IProjectionDefinition.Apply` | Medium (breaking) |

### P3 — Performance & scalability

| # | Item | Effort |
|---|---|---|
| P3.1 | `ReadAsync` streaming via `IAsyncEnumerable<SequencedEvent>` | Large |
| P3.2 | Use `fromPosition` in projection daemon / rebuild once P0.1 is done | Small |
| P3.3 | Multi-context API surface design (even if implementation is later) | Medium |

---

## Part 5 — Summary

Opossum correctly implements every **MUST** requirement of the DCB Event Store Specification.
The core read/append/concurrency semantics are solid and the sample application demonstrates
the full DCB pattern working end-to-end.

The main gap is the **Decision Model projection layer** — the half of the DCB spec that
describes how to build the state used to enforce invariants during command handling.
Currently this is entirely manual boilerplate in each command handler. Adding `IDecisionProjection<TState>`,
`Query.Matches()`, `BuildDecisionModelAsync()`, and `ComposeProjections()` would make
Opossum a genuinely complete DCB library rather than a DCB-capable event store that
leaves the projection wiring to the caller.

The secondary gap is `ReadAsync` lacking a `fromPosition` parameter, which is a
`SHOULD` requirement in the spec and would also unlock more efficient incremental
projection rebuilding.

All other findings are API design improvements that do not affect spec compliance.
