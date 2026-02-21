# Implementation Plan: Decision Model Projections (P1.1 – P1.6)

> **Branch:** `feature/DCB-compliannce`
> **Source analysis:** [docs/analysis/dcb-compliance-analysis.md](../analysis/dcb-compliance-analysis.md)
> **Spec reference:** [docs/specifications/dcb-projections.md](../specifications/dcb-projections.md)

---

## Overview

Adds a first-class **Decision Model projection layer** to Opossum — the write-side pattern
defined by the DCB Projections specification. This is purely additive: no existing API,
interface, or type is changed.

### What is being added

```
src/Opossum/
├── Core/
│   └── Query.cs                         ← Step 1: add Matches(SequencedEvent) method
└── DecisionModel/                        ← Steps 2–5: new folder
    ├── IDecisionProjection.cs            ← Step 2: interface
    ├── DecisionProjection.cs             ← Step 2: concrete implementation
    ├── DecisionModel.cs                  ← Step 3+4: result record
    └── DecisionModelExtensions.cs        ← Step 3+4: BuildDecisionModelAsync()
                                            Step 5: composed overloads (2- and 3-projection)
```

### Benchmark verdict

**No new benchmarks required.** All hot paths introduced by this layer are already
covered by existing benchmarks:

| New operation | Covered by existing benchmark |
|---|---|
| `BuildDecisionModelAsync` I/O path | `ReadBenchmarks`, `QueryBenchmarks` |
| In-memory fold inside `BuildDecisionModelAsync` | `ProjectionRebuildBenchmarks` (equivalent fold) |
| `Query.Matches()` | Pure in-memory — no I/O; not worth benchmarking |
| `ComposeProjections` union query | `QueryBenchmarks` (multi-item OR queries) |

Existing benchmarks need no adjustment: this branch touches no existing code paths.

### README verdict

The `## Core Concepts → Projections` section currently describes only read-side
projections. A new `## Core Concepts → Decision Model Projections` subsection must be
added once Step 4 is complete and the API is stable. This is part of **Step 6**.

---

## Steps

### Step 1 — `Query.Matches(SequencedEvent)` ✅ Done

**Files changed:**
- `src/Opossum/Core/Query.cs` — add instance method
- `tests/Opossum.UnitTests/Core/QueryMatchesTests.cs` — new unit test file

**What it does:**
Adds an in-memory event matching helper that mirrors the OR/AND semantics the event
store already applies on disk. Required by Step 5 (each sub-projection filters the
already-loaded event set before folding).

**Test coverage required:**
- `Query.All()` matches every event
- Empty `EventTypes` list matches any type
- Exact `EventType` match
- Non-matching `EventType` returns false
- Single tag match (Key + Value both must match)
- All tags must match (AND logic)
- One tag miss out of multiple tags returns false
- QueryItem with both types and tags (both must match)
- Multiple QueryItems (OR logic — matching either returns true)
- Null-safety: null event or null event data does not throw

**Status:** ⬜ Not started

---

### Step 2 — `IDecisionProjection<TState>` + `DecisionProjection<TState>` ✅ Done

**Files changed:**
- `src/Opossum/DecisionModel/IDecisionProjection.cs` — new interface
- `src/Opossum/DecisionModel/DecisionProjection.cs` — concrete delegate-based implementation
- `tests/Opossum.UnitTests/DecisionModel/DecisionProjectionTests.cs` — new unit test file

**What it does:**
Introduces the typed, composable, in-memory projection definition. No I/O. No DI.
No background services. A pure data + fold-function specification.

```csharp
// Interface
public interface IDecisionProjection<TState>
{
    TState InitialState { get; }
    Query Query { get; }
    TState Apply(TState state, SequencedEvent evt);
}

// Concrete — delegate-based factory support
public sealed class DecisionProjection<TState>(
    TState initialState,
    Query query,
    Func<TState, SequencedEvent, TState> apply) : IDecisionProjection<TState> { ... }
```

**Test coverage required (pure unit tests, no file system):**
- `InitialState` is returned correctly
- `Query` is returned correctly
- `Apply` delegate is called with correct state and event
- Multiple Apply calls fold state correctly (full sequence test)
- Factory function pattern: static method returning `new DecisionProjection<bool>(...)` works
- Two projections with same events, different queries produce independent states

**Status:** ⬜ Not started

---

### Step 3+4 — `DecisionModel<TState>` + `BuildDecisionModelAsync()` ✅ Done

**Files changed:**
- `src/Opossum/DecisionModel/DecisionModel.cs` — new result record
- `src/Opossum/DecisionModel/DecisionModelExtensions.cs` — new extension on `IEventStore`
- `tests/Opossum.UnitTests/DecisionModel/BuildDecisionModelTests.cs` — unit tests (pure fold logic)
- `tests/Opossum.IntegrationTests/DecisionModel/BuildDecisionModelIntegrationTests.cs` — integration tests with real event store

**What it does:**

```csharp
// Result type
public sealed record DecisionModel<TState>(TState State, AppendCondition AppendCondition);

// Extension: reads from store, folds, returns (state + condition) in one call
public static async Task<DecisionModel<TState>> BuildDecisionModelAsync<TState>(
    this IEventStore eventStore,
    IDecisionProjection<TState> projection,
    CancellationToken cancellationToken = default)
```

The `AppendCondition` is constructed automatically:
- `FailIfEventsMatch` = `projection.Query` (same query used to read)
- `AfterSequencePosition` = max position of loaded events, or `null` if no events

**Unit test coverage (pure logic, no file system):**
The unit tests verify the fold behaviour in isolation using a pre-built array of
`SequencedEvent` objects (no event store involved):
- Empty event set: state is `InitialState`, `AppendCondition.AfterSequencePosition` is null
- Single matching event: state is updated, `AfterSequencePosition` equals that event's position
- Multiple events: state reflects all applied events, position equals max
- Events are folded in position order (ascending)
- `AppendCondition.FailIfEventsMatch` equals the projection's `Query`

**Integration test coverage (real file system event store):**
- Append events, build decision model, verify state reflects appended events
- Build decision model on empty store: state is `InitialState`, condition has no position
- Build decision model after concurrent append: the returned `AppendCondition` correctly
  causes `AppendAsync` to fail when used against a stale read (DCB round-trip test)

**Status:** ⬜ Not started

---

### Step 5 — `ComposeProjections()` / multi-projection overloads ✅ Done

**Files changed:**
- `src/Opossum/DecisionModel/DecisionModelExtensions.cs` — add overloads
- `tests/Opossum.UnitTests/DecisionModel/ComposeProjectionsTests.cs` — new unit test file
- `tests/Opossum.IntegrationTests/DecisionModel/BuildDecisionModelIntegrationTests.cs` — add composed tests

**What it does:**

Strongly-typed tuple overloads for composing 2 and 3 projections into a single
`ReadAsync` call with a single `AppendCondition`. The union query is built from all
sub-queries; each sub-projection folds only its own matching events (using `Query.Matches`
from Step 1).

```csharp
// 2-projection overload
Task<(T1, T2, AppendCondition)> BuildDecisionModelAsync<T1, T2>(
    this IEventStore store,
    IDecisionProjection<T1> first,
    IDecisionProjection<T2> second,
    CancellationToken cancellationToken = default)

// 3-projection overload
Task<(T1, T2, T3, AppendCondition)> BuildDecisionModelAsync<T1, T2, T3>(...)
```

Internal composition logic:
1. Build union query: `Query.FromItems([..p1.QueryItems, ..p2.QueryItems])`
2. Read events once: `await store.ReadAsync(unionQuery, null)`
3. Fold each projection over its own matching subset: `events.Where(e => px.Query.Matches(e))`
4. `AppendCondition` uses union query + max position across all events

**Unit test coverage:**
- Two projections with non-overlapping queries: each state is independent
- Two projections with overlapping queries: shared events correctly applied to both
- Union query is the OR of both sub-queries (verified via `Query.Matches`)
- Three-projection overload: all three states returned correctly
- Empty event store: all states are `InitialState`, condition has null position

**Integration test coverage:**
- Composed two-projection build against real event store
- Verify `AppendCondition` spans both projection queries (concurrency test)

**Status:** ⬜ Not started

---

### Step 6 — Sample app refactor + README + CHANGELOG ✅ Done

**Files changed:**
- `Samples/Opossum.Samples.CourseManagement/CourseEnrollment/EnrollStudentToCourseCommand.cs` — use new API
- `Samples/Opossum.Samples.CourseManagement/CourseEnrollment/CourseEnrollmentAggregate.cs` — decompose into factory projections
- `Samples/Opossum.Samples.CourseManagement/CourseEnrollment/Queries.cs` — remove (replaced by projection factories)
- `tests/Samples/Opossum.Samples.CourseManagement.UnitTests/CourseEnrollmentAggregateTests.cs` — update to test new projection factories
- `tests/Samples/Opossum.Samples.CourseManagement.IntegrationTests/CourseEnrollmentIntegrationTests.cs` — verify no regression
- `README.md` — add `Decision Model Projections` subsection to Core Concepts
- `CHANGELOG.md` — document all added features

**Planned decomposition of `CourseEnrollmentAggregate`:**

The current monolithic aggregate bundles three independent concerns:
1. Course capacity check → `CourseCapacityProjection(courseId)`
2. Student enrollment limit check → `StudentEnrollmentLimitProjection(studentId)`
3. Duplicate enrollment check → `DuplicateEnrollmentProjection(courseId, studentId)`

After refactor:
```csharp
var (courseCapacity, studentLimit, duplicateCheck, appendCondition) =
    await eventStore.BuildDecisionModelAsync(
        CourseProjections.Capacity(command.CourseId),
        StudentProjections.EnrollmentLimit(command.StudentId),
        EnrollmentProjections.DuplicateCheck(command.CourseId, command.StudentId));
```

**Status:** ✅ Done

---

## Progress Tracker

| Step | Description | Status | PR / Commit |
|---|---|---|---|
| 1 | `Query.Matches(SequencedEvent)` | ✅ Done | — |
| 2 | `IDecisionProjection<T>` + `DecisionProjection<T>` | ✅ Done | — |
| 3+4 | `DecisionModel<T>` + `BuildDecisionModelAsync()` | ✅ Done | — |
| 5 | `ComposeProjections()` / tuple overloads | ✅ Done | — |
| 6 | Sample app refactor + README + CHANGELOG | ✅ Done | — |

---

## Definition of Done (entire feature)

- [ ] Build: `dotnet build` — 0 errors, 0 warnings
- [ ] Unit tests pass: `dotnet test tests/Opossum.UnitTests/`
- [ ] Integration tests pass: `dotnet test tests/Opossum.IntegrationTests/`
- [ ] Sample unit tests pass: `dotnet test tests/Samples/Opossum.Samples.CourseManagement.UnitTests/`
- [ ] Sample integration tests pass: `dotnet test tests/Samples/Opossum.Samples.CourseManagement.IntegrationTests/`
- [ ] No new benchmarks needed (verified above)
- [ ] README updated with Decision Model Projections section
- [ ] CHANGELOG updated
- [ ] `Opossum.slnx` updated with all new files
