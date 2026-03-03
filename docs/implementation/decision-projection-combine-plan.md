# Implementation Plan: `DecisionProjection.Combine()` + Dynamic Product Price Sample

> **Branch:** `feature/decision-projection-combine`
> **Spec reference:** [dcb.events/examples/dynamic-product-price](https://dcb.events/examples/dynamic-product-price/)
> **Design decision:** [docs/analysis/dcb-compliance-analysis.md](../analysis/dcb-compliance-analysis.md)

---

## Overview

This plan implements `DecisionProjection.Combine()` — a compositor that merges a
runtime-determined, homogeneous collection of `IDecisionProjection<TState>` instances
into a single `IDecisionProjection<IReadOnlyDictionary<TKey, TState>>`. The result
is a first-class projection that plugs directly into the existing single-projection
`BuildDecisionModelAsync` overload and, crucially, **composes** with the existing
typed 2/3-projection overloads.

### Why `Combine()` and not a new `BuildDecisionModelAsync` overload

The existing 1/2/3-projection overloads are heterogeneous (each projection can have a
different `TState`) and compile-time-fixed. `Combine()` fills the homogeneous +
runtime-determined-N gap without closing off composability. Because the result of
`Combine()` is a real `IDecisionProjection<T>`, it can be mixed freely with other
projections in the 2/3-overloads:

```csharp
// N product prices AND a customer order limit checked in ONE DCB read:
var (feeStates, orderLimit, condition) = await eventStore.BuildDecisionModelAsync(
    command.Items
        .ToDictionary(item => item.CourseId, item => CourseFeeProjection(item.CourseId))
        .Combine(),                                    // IDecisionProjection<IReadOnlyDictionary<Guid, CourseFeeState>>
    OrderLimitProjection(command.StudentId));           // IDecisionProjection<int>
```

An `IDictionary` overload on `BuildDecisionModelAsync` cannot compose like this — it is
a dead end the moment a second independent projection is needed alongside the dynamic group.

### File tree produced by this plan

```
src/Opossum/DecisionModel/
└── DecisionProjectionCombineExtensions.cs         ← NEW (Step 1)

tests/Opossum.UnitTests/DecisionModel/
└── CombineDecisionProjectionTests.cs              ← NEW (Step 2)

tests/Opossum.IntegrationTests/DecisionModel/
└── CombineDecisionProjectionIntegrationTests.cs   ← NEW (Step 3)

tests/Opossum.BenchmarkTests/DecisionModel/
└── CombineDecisionModelBenchmarks.cs              ← NEW (Step 4)

Samples/Opossum.Samples.CourseManagement/
├── Events/
│   ├── PremiumCourseListedEvent.cs                ← NEW (Step 5)
│   ├── CourseFeePriceUpdatedEvent.cs              ← NEW (Step 5)
│   ├── PremiumCoursePurchasedEvent.cs             ← NEW (Step 5)
│   └── PremiumCourseBundlePurchasedEvent.cs       ← NEW (Step 5)
└── PremiumCourseEnrollment/
    ├── CourseFeeState.cs                          ← NEW (Step 6)
    ├── CourseFeeProjection.cs                     ← NEW (Step 6)
    ├── ListPremiumCourse.cs                       ← NEW (Step 7)
    ├── UpdateCourseFeePrice.cs                    ← NEW (Step 8)
    ├── PurchasePremiumCourse.cs                   ← NEW (Step 9)
    ├── PurchaseCourseBundle.cs                    ← NEW (Step 10)
    └── Endpoint.cs                                ← NEW (Step 11)

tests/Samples/Opossum.Samples.CourseManagement.UnitTests/
└── PremiumCourseEnrollmentProjectionTests.cs      ← NEW (Step 12)

tests/Samples/Opossum.Samples.CourseManagement.IntegrationTests/
└── PremiumCourseEnrollmentIntegrationTests.cs     ← NEW (Step 13)

docs/features/
└── decision-projection-combine.md                 ← NEW (Step 14)
```

**Files modified** (not created):
- `src/Opossum/DecisionModel/DecisionModelExtensions.cs` — Step 1: change `BuildUnionQuery` visibility
- `Samples/Opossum.Samples.CourseManagement/Program.cs` — Step 11: register new endpoints
- `Samples/Opossum.Samples.DataSeeder/DataSeeder.cs` — Step 11: seed premium course data
- `README.md` — Step 15
- `CHANGELOG.md` — Step 16
- `Opossum.slnx` — Step 17

---

## Domain context — Premium Course Enrollment

The DCB dynamic product price example is adapted to the private school / paid online
courses domain as follows:

| DCB spec concept | Opossum sample domain |
|---|---|
| Product | **Premium Course** (a course with a monetary enrollment fee) |
| `ProductDefined` | `PremiumCourseListedEvent` (course listed with initial fee) |
| `ProductPriceChanged` | `CourseFeePriceUpdatedEvent` (fee updated by admin) |
| `ProductOrdered` | `PremiumCoursePurchasedEvent` (student purchases single course) |
| `ProductsOrdered` (cart) | `PremiumCourseBundlePurchasedEvent` (student purchases course bundle) |
| `product:<id>` tag | `premiumCourseId:<guid>` tag |
| Price grace period | **Fee grace period** (default: 10 minutes, configurable) |

**Three feature milestones** (matching the spec exactly):

- **Feature 1**: Purchase a single premium course — displayed fee must match current fee.
- **Feature 2**: Course fees can be updated — the old fee remains valid for a configurable
  grace period after the change.
- **Feature 3**: Purchase a course bundle (multiple premium courses in one transaction) —
  each displayed fee is independently validated; `Combine()` is the key mechanism.

---

## Phase 1 — Core Library

---

### Step 1 — `DecisionProjection.Combine()` extension ❌ Not Started

**Why this step exists:**
`BuildUnionQuery` is currently `private static` inside `DecisionModelExtensions`.
`Combine()` lives in a separate file and needs to build the same union query.
The minimal, non-breaking change is to promote `BuildUnionQuery` to `internal static`
so that both classes in the same assembly can use it without duplication.

**Files changed:**
- `src/Opossum/DecisionModel/DecisionModelExtensions.cs`
  — Change `private static Query BuildUnionQuery(...)` to `internal static`
- `src/Opossum/DecisionModel/DecisionProjectionCombineExtensions.cs` — **create**

**Key implementation contract:**

```csharp
namespace Opossum.DecisionModel;

public static class DecisionProjectionCombineExtensions
{
    /// <summary>
    /// Merges a runtime-determined, homogeneous collection of decision projections
    /// into a single IDecisionProjection whose state is an IReadOnlyDictionary keyed
    /// by the same keys as <paramref name="projections"/>.
    /// </summary>
    /// <remarks>
    /// The combined projection's Query is the union of all sub-projection queries.
    /// During folding, each event is dispatched only to the sub-projections whose
    /// own Query matches it — equivalent to N independent BuildDecisionModelAsync
    /// calls in a single store read.
    ///
    /// Because the result is a full IDecisionProjection{T}, it composes freely with
    /// the existing 2/3-projection BuildDecisionModelAsync overloads.
    /// </remarks>
    public static IDecisionProjection<IReadOnlyDictionary<TKey, TState>> Combine<TKey, TState>(
        this IDictionary<TKey, IDecisionProjection<TState>> projections)
        where TKey : notnull
    { ... }
}
```

**Implementation notes:**
- Snapshot `projections` into a local array immediately to guard against dictionary
  mutation after capture by the closure.
- Build initial state as `projections.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.InitialState)`.
- The `apply` lambda must avoid allocating a new `Dictionary<TKey, TState>` when no
  sub-projection actually matches the event — return `state` unchanged in that case.
- Throw `ArgumentException` when `projections` is empty (meaningless and likely a bug).
- Follow all existing conventions: `ConfigureAwait(false)` is N/A here (pure sync),
  `ArgumentNullException.ThrowIfNull`, file-scoped namespace, no external usings in the
  `.cs` file (only `Opossum.*` usings, rest in `GlobalUsings.cs`).

---

### Step 2 — Unit tests for `Combine()` ❌ Not Started

**File created:** `tests/Opossum.UnitTests/DecisionModel/CombineDecisionProjectionTests.cs`

**Test cases (pure in-memory, no file system):**

| Test name | What it verifies |
|---|---|
| `Combine_EmptyDictionary_ThrowsArgumentException` | Guard clause |
| `Combine_NullDictionary_ThrowsArgumentNullException` | Guard clause |
| `Combine_SingleEntry_QueryMatchesSingleSubProjection` | Union query with one item |
| `Combine_MultipleEntries_UnionQuerySpansAllSubProjections` | All `QueryItem`s present |
| `Combine_Apply_DispatchesOnlyToMatchingSubProjection` | Event routed to correct key only |
| `Combine_Apply_DispatchesToMultipleMatchingSubProjections` | Event tagged for two keys |
| `Combine_Apply_NoMatch_ReturnsSameStateInstance` | Zero-allocation fast path |
| `Combine_InitialState_ContainsAllKeysWithSubProjectionInitialStates` | Initial state shape |
| `Combine_Result_IsValidIDecisionProjection` | Satisfies the interface contract |
| `Combine_ComposesWithTwoProjectionOverload` | `(combined, other, condition)` tuple builds |
| `Combine_QueryAll_SubProjection_MakesUnionQueryAll` | `Query.All()` edge case |

**Notes:**
- Use the same `MakeEvent` helper pattern already established in
  `CourseEnrollmentProjectionTests.cs`.
- No mocking; all state is built via pure `projection.Apply(state, evt)` calls.
- Mirror the DCB spec's test cases in JS comments to make the mapping explicit.

---

### Step 3 — Integration tests for `Combine()` ❌ Not Started

**File created:**
`tests/Opossum.IntegrationTests/DecisionModel/CombineDecisionProjectionIntegrationTests.cs`

**Test cases (real `FileSystemEventStore` in temp directory):**

| Test name | What it verifies |
|---|---|
| `BuildDecisionModel_WithCombine_ReadsAllRelevantEvents` | Single store read covers all keys |
| `BuildDecisionModel_WithCombine_AppendConditionSpansAllSubQueries` | Concurrent write on any key fails |
| `BuildDecisionModel_WithCombine_ComposesWithSecondProjection` | 2-projection overload + Combine |
| `BuildDecisionModel_WithCombine_EmptyStore_AllStatesAreInitial` | Edge case: no events |
| `BuildDecisionModel_WithCombine_RetryOnConflict` | `ExecuteDecisionAsync` retries correctly |

**Notes:**
- Use `TestDirectoryHelper` for temp directory management.
- Seed events via `eventStore.AppendAsync` before each test — no shared state between tests.

---

### Step 4 — Benchmark tests for `Combine()` ❌ Not Started

**File created:** `tests/Opossum.BenchmarkTests/DecisionModel/CombineDecisionModelBenchmarks.cs`

**Benchmark scenarios:**

| Benchmark | `N` parameter | Description |
|---|---|---|
| `BuildDecisionModel_WithCombine` | 2 / 5 / 10 | `Combine(N projections)` → `BuildDecisionModelAsync` |
| `BuildDecisionModel_ManualUnion` | 2 / 5 / 10 | Manually constructed equivalent single projection |

**Purpose:** Confirm that `Combine()` adds negligible overhead compared to a hand-rolled
multi-entity projection and that scaling N has O(N) complexity at worst.

**Notes:**
- Use `[Params(2, 5, 10)]` on `N`.
- Seed the store in `[GlobalSetup]` with events for all N entities.
- Follow the structure of `QueryBenchmarks.cs` (config, `TempFileSystemHelper`, etc.).

---

## Phase 2 — Sample App: Premium Course Enrollment

---

### Step 5 — Domain events ❌ Not Started

**Files created** (in `Samples/Opossum.Samples.CourseManagement/Events/`):

**`PremiumCourseListedEvent.cs`**
```csharp
// Records that a premium course has been made available with an initial enrollment fee.
public record PremiumCourseListedEvent(Guid CourseId, string Title, decimal InitialFee) : IEvent;
```
Tags to attach at write time: `premiumCourseId:<CourseId>`

**`CourseFeePriceUpdatedEvent.cs`**
```csharp
// Records that the enrollment fee for a premium course has been updated.
public record CourseFeePriceUpdatedEvent(Guid CourseId, decimal NewFee) : IEvent;
```
Tags to attach at write time: `premiumCourseId:<CourseId>`

**`PremiumCoursePurchasedEvent.cs`**
```csharp
// Records that a student purchased enrollment in a single premium course
// at a specific displayed fee.
public record PremiumCoursePurchasedEvent(Guid CourseId, Guid StudentId, decimal PaidFee) : IEvent;
```
Tags to attach at write time: `premiumCourseId:<CourseId>`

**`PremiumCourseBundlePurchasedEvent.cs`**
```csharp
// Records that a student purchased enrollment in multiple premium courses
// at their individually displayed fees.
public record PremiumCourseBundlePurchasedEvent(
    Guid StudentId,
    IReadOnlyList<BundlePurchaseItem> Items) : IEvent;

public record BundlePurchaseItem(Guid CourseId, decimal PaidFee);
```
Tags to attach at write time: one `premiumCourseId:<CourseId>` tag **per item** on the
single event — the multi-tag pattern is exactly what the DCB spec's Feature 3 requires.

---

### Step 6 — Decision projection: `CourseFeeProjection` ❌ Not Started

**Files created** (in `Samples/Opossum.Samples.CourseManagement/PremiumCourseEnrollment/`):

**`CourseFeeState.cs`**
```csharp
// Represents the valid fee state for a single premium course at command-handling time.
public sealed record CourseFeeState(
    decimal? LastValidOldFee,          // The last stable fee before any recent change.
                                       // Null when no price predates the grace window,
                                       // or when the course has never had a fee change.
    IReadOnlyList<decimal> ValidCurrentFees  // Fees set within the grace period — still
                                             // acceptable even though a newer fee may exist.
)
{
    // True when the displayed fee is acceptable for this course right now.
    public bool IsValidFee(decimal displayedFee) =>
        LastValidOldFee == displayedFee || ValidCurrentFees.Contains(displayedFee);
}
```

**`CourseFeeProjection.cs`**
```csharp
public static class CourseFeeProjections
{
    // Default grace period matching the DCB spec example.
    private static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromMinutes(10);

    public static IDecisionProjection<CourseFeeState?> ForCourse(
        Guid courseId,
        TimeSpan? gracePeriod = null)
    { ... }
}
```

**Implementation logic** (mirrors DCB spec Feature 2 exactly):
- Initial state: `null` (course not yet listed)
- `PremiumCourseListedEvent`:
  - If `UtcNow - evt.Metadata.Timestamp <= gracePeriod`:
    → `new CourseFeeState(LastValidOldFee: null, ValidCurrentFees: [initialFee])`
  - Else:
    → `new CourseFeeState(LastValidOldFee: initialFee, ValidCurrentFees: [])`
- `CourseFeePriceUpdatedEvent`:
  - If `UtcNow - evt.Metadata.Timestamp <= gracePeriod`:
    → keep `LastValidOldFee`, add `newFee` to `ValidCurrentFees`
  - Else:
    → `new CourseFeeState(LastValidOldFee: newFee, ValidCurrentFees: state.ValidCurrentFees)`
- Query: `EventTypes = [PremiumCourseListedEvent, CourseFeePriceUpdatedEvent]`,
  `Tags = [premiumCourseId:<courseId>]`

**Note:** `PremiumCoursePurchasedEvent` and `PremiumCourseBundlePurchasedEvent` are
intentionally **not** included in this query — the consistency boundary is purely
price-based, not inventory-based. This is the key DCB insight: the query is as
narrow as the business invariant requires.

---

### Step 7 — Feature 1: List premium course ❌ Not Started

**File created:** `Samples/Opossum.Samples.CourseManagement/PremiumCourseEnrollment/ListPremiumCourse.cs`

Contains:
- `ListPremiumCourseCommand(Guid CourseId, string Title, decimal InitialFee)`
- `ListPremiumCourseCommandHandler` — unconditional append (no DCB guard needed;
  listing is administrative, not subject to a consistency boundary).

This is the setup command that seeds the `PremiumCourseListedEvent` for Features 2 and 3.

---

### Step 8 — Feature 2: Update course fee price ❌ Not Started

**File created:** `Samples/Opossum.Samples.CourseManagement/PremiumCourseEnrollment/UpdateCourseFeePrice.cs`

Contains:
- `UpdateCourseFeePriceCommand(Guid CourseId, decimal NewFee)`
- `UpdateCourseFeePriceCommandHandler` — validates that the course exists (using
  `CourseFeeProjections.ForCourse(courseId)`) before appending `CourseFeePriceUpdatedEvent`.

The DCB guard here ensures the course existed when the update was issued and that
no concurrent listing race can sneak in.

---

### Step 9 — Feature 1 & 2: Purchase single premium course ❌ Not Started

**File created:** `Samples/Opossum.Samples.CourseManagement/PremiumCourseEnrollment/PurchasePremiumCourse.cs`

Contains:
- `PurchasePremiumCourseCommand(Guid CourseId, Guid StudentId, decimal DisplayedFee)`
- `PurchasePremiumCourseCommandHandler`

**Handler logic:**
```csharp
var model = await eventStore.BuildDecisionModelAsync(
    CourseFeeProjections.ForCourse(command.CourseId), ct);

if (model.State is null)
    return CommandResult.Fail($"Premium course '{command.CourseId}' does not exist.");

if (!model.State.IsValidFee(command.DisplayedFee))
    return CommandResult.Fail(
        $"The displayed fee {command.DisplayedFee:C} is no longer valid for this course. " +
        "Please refresh the course listing and try again.");

// Append with the DCB guard — concurrent fee change will force a retry.
await eventStore.AppendAsync(
    new PremiumCoursePurchasedEvent(command.CourseId, command.StudentId, command.DisplayedFee)
        .ToDomainEvent()
        .WithTag("premiumCourseId", command.CourseId.ToString())
        .WithTimestamp(DateTimeOffset.UtcNow)
        .Build(),
    model.AppendCondition, ct);

return CommandResult.Ok();
```

Wrap in `ExecuteDecisionAsync` for automatic retry on concurrent fee changes.

---

### Step 10 — Feature 3: Purchase course bundle ❌ Not Started

**File created:** `Samples/Opossum.Samples.CourseManagement/PremiumCourseEnrollment/PurchaseCourseBundle.cs`

Contains:
- `PurchaseCourseBundleCommand(Guid StudentId, IReadOnlyList<BundleItem> Items)`
- `BundleItem(Guid CourseId, decimal DisplayedFee)`
- `PurchaseCourseBundleCommandHandler`

**Handler logic — the primary `Combine()` showcase:**
```csharp
// Build one projection per course in the bundle, then merge them.
var combinedProjection = command.Items
    .ToDictionary(
        item => item.CourseId,
        item => CourseFeeProjections.ForCourse(item.CourseId));
    .Combine();

var model = await eventStore.BuildDecisionModelAsync(combinedProjection, ct);

// Validate every item independently against the combined state.
foreach (var item in command.Items)
{
    var feeState = model.State[item.CourseId];
    if (feeState is null)
        return CommandResult.Fail($"Premium course '{item.CourseId}' does not exist.");
    if (!feeState.IsValidFee(item.DisplayedFee))
        return CommandResult.Fail(
            $"The displayed fee {item.DisplayedFee:C} for course '{item.CourseId}' " +
            "is no longer valid. Please refresh the course listing and try again.");
}

// A single event records the entire bundle; tags span all course IDs.
var bundleEvent = new PremiumCourseBundlePurchasedEvent(
    command.StudentId,
    command.Items.Select(i => new BundlePurchaseItem(i.CourseId, i.DisplayedFee)).ToList())
    .ToDomainEvent()
    .WithTags(command.Items.Select(i =>
        new Tag("premiumCourseId", i.CourseId.ToString())).ToArray())
    .WithTimestamp(DateTimeOffset.UtcNow)
    .Build();

await eventStore.AppendAsync(bundleEvent, model.AppendCondition, ct);
return CommandResult.Ok();
```

The `AppendCondition` returned by `BuildDecisionModelAsync(combinedProjection)` spans
every course in the bundle — a fee change to any single course will reject the append
and trigger a retry for the entire bundle.

---

### Step 11 — API endpoints and DataSeeder ❌ Not Started

**File created:** `Samples/Opossum.Samples.CourseManagement/PremiumCourseEnrollment/Endpoint.cs`

| Method | Route | Handler | Feature |
|---|---|---|---|
| `POST` | `/premium-courses` | `ListPremiumCourseCommandHandler` | Setup |
| `PUT` | `/premium-courses/{courseId}/fee` | `UpdateCourseFeePriceCommandHandler` | Feature 2 |
| `POST` | `/premium-courses/{courseId}/purchase` | `PurchasePremiumCourseCommandHandler` | Feature 1 & 2 |
| `POST` | `/premium-courses/bundle-purchase` | `PurchaseCourseBundleCommandHandler` | Feature 3 |

**Files modified:**
- `Samples/Opossum.Samples.CourseManagement/Program.cs` — register the new endpoint group.
- `Samples/Opossum.Samples.DataSeeder/DataSeeder.cs` — add seed calls for 3–4 premium
  courses with initial fees (so the sample app starts with meaningful data for manual testing).

---

## Phase 3 — Tests

---

### Step 12 — Sample app unit tests ❌ Not Started

**File created:**
`tests/Samples/Opossum.Samples.CourseManagement.UnitTests/PremiumCourseEnrollmentProjectionTests.cs`

Tests `CourseFeeProjections.ForCourse(...)` in isolation — pure projection fold, no I/O.
Follow the pattern in `CourseEnrollmentProjectionTests.cs`.

**Test cases:**

| Test name | Feature | What it verifies |
|---|---|---|
| `ForCourse_InitialState_IsNull` | 1 | Course starts unlisted |
| `ForCourse_Query_ContainsPremiumCourseIdTag` | 1 | Tag scoping |
| `ForCourse_Apply_ListedEvent_RecentlyListed_SetsValidCurrentFee` | 1 & 2 | Recent listing → fee in `ValidCurrentFees` |
| `ForCourse_Apply_ListedEvent_ListedLongAgo_SetsLastValidOldFee` | 2 | Old listing → fee in `LastValidOldFee` |
| `ForCourse_Apply_FeeUpdated_RecentUpdate_AppendedToValidCurrentFees` | 2 | Recent change keeps old fee too |
| `ForCourse_Apply_FeeUpdated_OldUpdate_PromotesToLastValidOldFee` | 2 | Old change expires previous grace fees |
| `ForCourse_IsValidFee_DisplayedPriceMatchesLastValidOldFee_ReturnsTrue` | 2 | Old-but-stable fee accepted |
| `ForCourse_IsValidFee_DisplayedPriceInValidCurrentFees_ReturnsTrue` | 2 | Grace-period fee accepted |
| `ForCourse_IsValidFee_DisplayedPriceNotValid_ReturnsFalse` | 1 & 2 | Stale fee rejected |

**Notes:**
- Use a `MakeEvent(payload, position, timestamp, tags)` helper that accepts a
  `DateTimeOffset` parameter so timestamp-sensitive grace period logic can be exercised
  without real `Task.Delay`.
- Mirror the DCB spec's named test descriptions in XML doc comments for traceability.

---

### Step 13 — Sample app integration tests ❌ Not Started

**File created:**
`tests/Samples/Opossum.Samples.CourseManagement.IntegrationTests/PremiumCourseEnrollmentIntegrationTests.cs`

Uses the existing `IntegrationTestFixture` (Minimal API + real file system event store in temp directory).

**Test cases:**

| Test name | Feature | Spec test case |
|---|---|---|
| `PurchasePremiumCourse_InvalidDisplayedFee_ReturnsBadRequest` | 1 | "Order product with invalid displayed price" |
| `PurchasePremiumCourse_ValidDisplayedFee_Returns200` | 1 | "Order product with valid displayed price" |
| `PurchasePremiumCourse_FeeNeverValid_ReturnsBadRequest` | 2 | "Order product with a price that was never valid" |
| `PurchasePremiumCourse_FeeChangedMoreThan10MinutesAgo_ReturnsBadRequest` | 2 | "Order product with price changed >10 min ago" |
| `PurchasePremiumCourse_InitialFeeStillValidAfterOldChange_Returns200` | 2 | "Order product with initial valid price" |
| `PurchasePremiumCourse_FeeChangedLessThan10MinutesAgo_OldFeeStillValid_Returns200` | 2 | "Order product with price changed <10 min ago" |
| `PurchasePremiumCourse_NewFeeValidAfterRecentChange_Returns200` | 2 | "Order product with valid new price" |
| `PurchaseBundle_OneItemInvalidFee_ReturnsBadRequest` | 3 | "Order product with displayed price never valid" |
| `PurchaseBundle_OneItemFeeExpired_ReturnsBadRequest` | 3 | "Order product with price changed >10 min ago" |
| `PurchaseBundle_AllFeesValid_Returns200` | 3 | "Order product with initial valid price" |
| `PurchaseBundle_OneItemChangedWithinGracePeriod_OldFeeValid_Returns200` | 3 | "Order multiple products with valid prices" |
| `PurchaseBundle_MultipleCoursesAllValidFees_Returns200` | 3 | "Order multiple products with valid prices" |

**Notes:**
- To simulate "price changed N minutes ago" without actual waiting, seed events using
  `NewEvent.Metadata = new Metadata { Timestamp = DateTimeOffset.UtcNow.AddMinutes(-N) }`.
  This is the C# equivalent of the spec's `addEventMetadata(event, { minutesAgo: N })`.
- Each test class or `[Collection]` must use its own temp directory — never share state
  between tests.

---

## Phase 4 — Documentation

---

### Step 14 — Feature documentation ❌ Not Started

**File created:** `docs/features/decision-projection-combine.md`

**Sections:**
1. **Overview** — what `Combine()` does and when to use it.
2. **API reference** — full XML-doc-derived signature with parameter descriptions.
3. **Feature 1 walkthrough** — single course, single projection (no `Combine()` yet;
   establishes the baseline pattern).
4. **Feature 2 walkthrough** — grace period logic; shows how `evt.Metadata.Timestamp`
   drives the fold function.
5. **Feature 3 walkthrough** — the `Combine()` showcase; full code example with comments.
6. **Composing `Combine()` with other projections** — the advanced example mixing
   `Combine()` with a second independent projection in the 2-projection overload.
7. **When NOT to use `Combine()`** — guidance for heterogeneous state (use typed
   2/3-overloads instead) and for fixed-N cases (use the typed overloads directly).

---

### Step 15 — Update root `README.md` ❌ Not Started

Add `Combine()` to the **Core Concepts → Decision Model** section.

Suggested addition under the existing `BuildDecisionModelAsync` entry:

> **Dynamic-N projection groups** — when the number of entities is determined at runtime
> (e.g. a shopping cart), use `IDictionary<TKey, IDecisionProjection<TState>>.Combine()`
> to merge them into a single `IDecisionProjection<IReadOnlyDictionary<TKey, TState>>`
> that flows into the standard `BuildDecisionModelAsync` overload:
>
> ```csharp
> var model = await eventStore.BuildDecisionModelAsync(
>     command.Items
>         .ToDictionary(i => i.CourseId, i => CourseFeeProjections.ForCourse(i.CourseId))
>         .Combine());
> ```

---

### Step 16 — Update `CHANGELOG.md` ❌ Not Started

Add under `## [Unreleased]`:

```markdown
### Added
- **`DecisionProjection.Combine()`** — extension method on
  `IDictionary<TKey, IDecisionProjection<TState>>` that merges a runtime-determined,
  homogeneous collection of decision projections into a single
  `IDecisionProjection<IReadOnlyDictionary<TKey, TState>>`. Enables the DCB
  dynamic-N consistency boundary pattern (e.g. multi-item shopping cart price validation)
  without adding new overloads to `BuildDecisionModelAsync`. Because the result is a
  first-class `IDecisionProjection<T>`, it composes freely with the existing typed
  2/3-projection overloads.
- **Premium Course Enrollment feature in `Opossum.Samples.CourseManagement`** —
  demonstrates the `Combine()` pattern end-to-end through three progressive milestones
  matching the [dcb.events dynamic product price example](https://dcb.events/examples/dynamic-product-price/):
  single purchase with fee validation (Feature 1), fee changes with a 10-minute grace
  period (Feature 2), and bundle purchase of multiple courses in a single atomic
  transaction (Feature 3).
```

---

### Step 17 — Update `Opossum.slnx` ❌ Not Started

Add the following file entries in the correct sorted positions:

**Under `/docs/features/`:**
```xml
<File Path="docs/features/decision-projection-combine.md" />
```

**Under `/docs/implementation/`:**
```xml
<File Path="docs/implementation/decision-projection-combine-plan.md" />
```

---

## Pre-Completion Verification Checklist

To be completed after all steps are marked ✅ Done:

```markdown
- [ ] Build successful: `dotnet build`
- [ ] Zero warnings: `0 Warning(s)` in build output
- [ ] Unit tests passing: `dotnet test tests/Opossum.UnitTests/`
- [ ] Integration tests passing: `dotnet test tests/Opossum.IntegrationTests/`
- [ ] Sample unit tests passing: `dotnet test tests/Samples/Opossum.Samples.CourseManagement.UnitTests/`
- [ ] Sample integration tests passing: `dotnet test tests/Samples/Opossum.Samples.CourseManagement.IntegrationTests/`
- [ ] Benchmark project builds: `dotnet build tests/Opossum.BenchmarkTests/`
- [ ] Sample app starts without errors
- [ ] All 12 spec test cases from dcb.events covered by integration tests
- [ ] CHANGELOG.md updated
- [ ] README.md updated
- [ ] Opossum.slnx updated
- [ ] docs/features/decision-projection-combine.md written
- [ ] ConfigureAwait(false) on all awaits in src/Opossum/ (VSTHRD111 = 0)
- [ ] No Opossum.* usings in GlobalUsings.cs; no external usings in .cs files
```
