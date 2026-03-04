# Feasibility Analysis: "Dynamic Course Book Price" DCB Example in Opossum

**Source:** https://dcb.events/examples/dynamic-product-price/  
**Date:** 2025  
**Scope:** Can the "Dynamic Product Price" DCB example — adapted to Course Books — be implemented
in `Opossum.Samples.CourseManagement` using the current state of the Opossum library?
**Update:** Both minor gaps identified in §3 are now promoted to in-scope library enhancements and will be implemented as part of this task.

---

## 1. What the DCB Example Demonstrates

The original example solves the **stale-price problem**: a customer sees a price, but by the time
they check out the price may have changed. The system must enforce that the price displayed to the
customer is the price charged, with a configurable grace period for recent price changes.

### Domain Adaptation: Products → Course Books

| Original | Adapted |
|---|---|
| Generic product | Course Book a student can purchase to complement a course |
| `ProductDefined` | `CourseBookDefinedEvent` |
| `ProductPriceChanged` | `CourseBookPriceChangedEvent` |
| `ProductOrdered` / `ProductsOrdered` | `CourseBookPurchasedEvent` / `CourseBooksOrderedEvent` |
| `product:{id}` tag | `bookId:{id}` tag |

### Business Rules (unchanged from spec)

1. The displayed price **must** match the stored price to complete a purchase.
2. If the price is no longer valid the purchase **must fail**.
3. Course book prices can be changed at any time by an administrator.
4. If a price was changed, the **previous price remains valid for a configurable grace period**
   (e.g. 30 minutes) to avoid surprising a student mid-checkout.

### Three Progressive Features

| Feature | Challenge |
|---|---|
| **F1** — Buy one book, fixed price | Single-entity, no history |
| **F2** — Price changes with grace period | Time-aware decision projection |
| **F3** — Shopping cart (multiple books) | Dynamic set of entities per command |

---

## 2. Mapping to Opossum Primitives

### 2.1 Tags — ✅ Fully Supported

The DCB spec uses `product:{id}` tags. Opossum's `Tag(string Key, string Value)` record is a
direct match:

```csharp
.WithTag("bookId", command.BookId.ToString())
```

Tags are indexed at write time and are queryable via `QueryItem.Tags`. Multiple tags on a single
event (needed for Feature 3's `CourseBooksOrderedEvent`) are also supported:

```csharp
// CourseBooksOrderedEvent carries one bookId tag per item
foreach (var item in command.Items)
    builder.WithTag("bookId", item.BookId.ToString());
```

### 2.2 Decision Projections — ✅ Fully Supported

`DecisionProjection<TState>` maps directly to the spec's `createProjection(...)` factory. The
C# equivalent of the F1 price projection is:

```csharp
public static IDecisionProjection<decimal> CurrentPrice(Guid bookId) =>
    new DecisionProjection<decimal>(
        initialState: -1m,                // -1 = book does not exist
        query: Query.FromItems(new QueryItem
        {
            EventTypes = [nameof(CourseBookDefinedEvent)],
            Tags = [new Tag("bookId", bookId.ToString())]
        }),
        apply: (state, evt) => evt.Event.Event switch
        {
            CourseBookDefinedEvent e => e.Price,
            _ => state
        });
```

### 2.3 Grace Period — ✅ Supported via `Metadata.Timestamp`

The DCB JS example attaches `minutesAgo` as synthetic metadata. In Opossum,
`SequencedEvent.Metadata.Timestamp` is a `DateTimeOffset` that the store persists at append time.
The decision projection receives the full `SequencedEvent` (including `Metadata`), so the elapsed
time is always available:

```csharp
// Inside the apply function:
var age = timeProvider.GetUtcNow() - evt.Metadata.Timestamp;
bool withinGracePeriod = age <= gracePeriod;
```

Where `timeProvider` is injected into the projection factory (see §3.1 for the resolved implementation).

### 2.4 `BuildDecisionModelAsync` — ✅ Supported for F1 and F2

`DecisionModelExtensions.BuildDecisionModelAsync<TState>` (single-projection overload) covers
Feature 1 and Feature 2 with no changes:

```csharp
var model = await eventStore.BuildDecisionModelAsync(
    CourseBookProjections.PriceWithGracePeriod(command.BookId)); // TimeProvider.System by default

if (!model.State.IsValidPrice(command.DisplayedPrice))
    return CommandResult.Fail($"Displayed price {command.DisplayedPrice} is no longer valid.");

await eventStore.AppendAsync(purchaseEvent, model.AppendCondition);
```

### 2.5 `AppendCondition` — ✅ Fully Supported

`AppendCondition.FailIfEventsMatch` scoped to a per-book query means that a concurrent price
change for the **same book** rejects the purchase (correct), while a price change for a **different
book** is invisible to the condition (also correct — DCB's narrowly-scoped consistency guarantee).

### 2.6 `ExecuteDecisionAsync` — ✅ Fully Supported

The retry helper handles `AppendConditionFailedException` automatically with exponential back-off,
matching the spec's expectation that clients retry the full read → decide → append cycle.

### 2.7 Read-Side Projections (Catalog & Order History) — ✅ Fully Supported

`IProjectionDefinition<TState>` + `[ProjectionDefinition]` + `[ProjectionTags]` are sufficient to
build a persisted Course Book catalog projection and an order-history projection without any library
changes.

---

## 3. Gap Analysis

### 3.1 ✅ Time Injection in Decision Projections — Resolved via `TimeProvider` Constructor Overload

**Issue:** The grace period calculation in the fold function needs to know "now". Capturing
`DateTimeOffset.UtcNow` directly inside the projection factory is not unit-testable in isolation —
tests that want to simulate an expired grace period cannot override wall-clock time.

**Previous workaround (removed):** Passing a `DateTimeOffset now` parameter to the projection
factory is functional but ad-hoc — it leaks the "capture time" concern into every caller and is
not a standardised library convention.

**Resolution — `TimeProvider` constructor overload in `DecisionProjection<TState>`:**

A new constructor overload is added to `DecisionProjection<TState>` in `src/Opossum/`:

```csharp
/// <summary>
/// Creates a time-aware <see cref="DecisionProjection{TState}"/> whose fold function
/// receives the current time from a <see cref="TimeProvider"/>.
/// Defaults to <see cref="TimeProvider.System"/> in production.
/// Inject a custom <see cref="TimeProvider"/> in tests to control wall-clock time.
/// </summary>
public DecisionProjection(
    TState initialState,
    Query query,
    Func<TState, SequencedEvent, TimeProvider, TState> apply,
    TimeProvider? timeProvider = null)
```

The `timeProvider` parameter defaults to `TimeProvider.System` (wall-clock time), so all
existing production code passes without modification. The overload wraps the time-aware
apply into the standard `Func<TState, SequencedEvent, TState>` delegate via closure —
`IDecisionProjection<TState>.Apply(TState, SequencedEvent)` is unchanged, making this a
**fully non-breaking additive change**.

**Updated projection factory:**
```csharp
// Before: explicit 'now' — leaks time-capture concern into callers
public static IDecisionProjection<CourseBookPriceState> PriceWithGracePeriod(
    Guid bookId, DateTimeOffset now) => ...

// After: TimeProvider — standardised, injectable, testable
public static IDecisionProjection<CourseBookPriceState> PriceWithGracePeriod(
    Guid bookId, TimeProvider? timeProvider = null) =>
    new DecisionProjection<CourseBookPriceState>(
        initialState: CourseBookPriceState.Empty,
        query: ...,
        apply: (state, evt, tp) =>
        {
            var age = tp.GetUtcNow() - evt.Metadata.Timestamp;
            return evt.Event.Event switch
            {
                CourseBookDefinedEvent e   => state.ApplyDefined(e.Price, age),
                CourseBookPriceChangedEvent e => state.ApplyPriceChanged(e.NewPrice, age),
                _ => state
            };
        },
        timeProvider: timeProvider);
```

**Updated command handler:**
```csharp
// Before: caller must capture 'now' and pass it in manually
var now = DateTimeOffset.UtcNow;
var model = await eventStore.BuildDecisionModelAsync(
    CourseBookProjections.PriceWithGracePeriod(command.BookId, now));

// After: TimeProvider.System used by default — callers need no changes
var model = await eventStore.BuildDecisionModelAsync(
    CourseBookProjections.PriceWithGracePeriod(command.BookId));
```

**Test pattern — no external packages needed:**
`TimeProvider` is an abstract class built into .NET 8+. Tests subclass it directly:

```csharp
// Minimal fake — zero external dependencies
private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

// Test: grace period NOT expired (5 minutes after event)
var tp = new FixedTimeProvider(eventTimestamp + TimeSpan.FromMinutes(5));
var projection = CourseBookProjections.PriceWithGracePeriod(bookId, tp);
// → state.IsValidPrice(oldPrice) is true

// Test: grace period HAS expired (2 hours after event)
var tp2 = new FixedTimeProvider(eventTimestamp + TimeSpan.FromHours(2));
var projection2 = CourseBookProjections.PriceWithGracePeriod(bookId, tp2);
// → state.IsValidPrice(oldPrice) is false
```

**Files to change:**

| File | Change |
|---|---|
| `src/Opossum/DecisionModel/DecisionProjection.cs` | Add `TimeProvider`-aware constructor overload |
| `tests/Opossum.UnitTests/DecisionModel/DecisionProjectionTests.cs` | Add unit tests for the new overload |
| `Samples/.../CourseBookPriceProjections.cs` | Replace `DateTimeOffset now` with `TimeProvider? timeProvider = null` |
| `Samples/.../PurchaseCourseBookCommand.cs` | Remove manual `now` capture |
| `Samples/.../OrderCourseBooksCommand.cs` | Remove manual `now` capture |

---

### 3.2 ✅ No N-ary / Dynamic `BuildDecisionModelAsync` — Resolved via List Overload

**Issue:** The DCB JS example for Feature 3 dynamically creates one `ProductPriceProjection(id)`
per product in the cart and passes them all as a dictionary to `buildDecisionModel(...)`.
Opossum has typed overloads for 1, 2, and 3 projections. There is no
`BuildDecisionModelAsync(IEnumerable<IDecisionProjection<T>>)` overload for a runtime-variable
number of **homogeneous** projections.

**Why this matters:** A shopping cart can contain N books where N is only known at runtime.

**Clean workaround — Compound Projection Pattern:** Because all books share the same projection
shape `CourseBookPriceState`, the entire cart can be expressed as **one** compound projection
whose state is `IReadOnlyDictionary<Guid, CourseBookPriceState>` and whose query is the OR-union
of all per-book QueryItems:

```csharp
public static IDecisionProjection<IReadOnlyDictionary<Guid, CourseBookPriceState>>
    CartPrices(IReadOnlyList<Guid> bookIds, DateTimeOffset now) =>
        new DecisionProjection<IReadOnlyDictionary<Guid, CourseBookPriceState>>(
            initialState: new Dictionary<Guid, CourseBookPriceState>(),
            query: Query.FromItems(bookIds.Select(id => new QueryItem
            {
                EventTypes = [
                    nameof(CourseBookDefinedEvent),
                    nameof(CourseBookPriceChangedEvent)
                ],
                Tags = [new Tag("bookId", id.ToString())]
            }).ToArray()),
            apply: (state, evt) =>
            {
                var bookId = evt.Event.Tags
                    .FirstOrDefault(t => t.Key == "bookId")?.Value;
                if (bookId is null || !Guid.TryParse(bookId, out var id))
                    return state;

                var dict = new Dictionary<Guid, CourseBookPriceState>(state);
                dict[id] = ApplyPriceEvent(dict.GetValueOrDefault(id), evt, now);
                return dict;
            });
```

This is one `BuildDecisionModelAsync` call, one event-store read, and one `AppendCondition`
that spans all books in the cart — functionally identical to the JS example's multi-projection
approach.

**Resolution — N-ary `BuildDecisionModelAsync` overload:**

A new overload is added to `DecisionModelExtensions` in `src/Opossum/`:

```csharp
/// <summary>
/// Builds a Decision Model from a runtime-variable list of homogeneous projections
/// using a single event-store read. Returns one state per projection in the same order
/// as the input list, plus the <see cref="AppendCondition"/> spanning all queries.
/// </summary>
/// <exception cref="ArgumentException">
/// Thrown when <paramref name="projections"/> is empty.
/// </exception>
public static async Task<(IReadOnlyList<TState> States, AppendCondition Condition)>
    BuildDecisionModelAsync<TState>(
        this IEventStore eventStore,
        IReadOnlyList<IDecisionProjection<TState>> projections,
        CancellationToken cancellationToken = default)
```

Implementation strategy (reuses `BuildUnionQuery` and `FoldEvents` private helpers):
1. Build a union query from all projection queries — one `ReadAsync` call.
2. `FoldEvents` independently for each projection against the shared event set.
3. Return `(states, appendCondition)` where `states[i]` corresponds to `projections[i]`.

**Updated Feature 3 command handler — N-ary overload replaces the compound projection:**
```csharp
// Before (compound projection workaround):
var model = await eventStore.BuildDecisionModelAsync(
    CartProjections.CartPrices(bookIds, now));
// state is IReadOnlyDictionary<Guid, CourseBookPriceState> — requires tag dispatch in the fold

// After (N-ary overload — direct DCB spec mapping):
var bookIds = command.Items.Select(i => i.BookId).ToList();
var projections = bookIds
    .Select(id => CourseBookProjections.PriceWithGracePeriod(id))
    .ToList();
var (states, condition) = await eventStore.BuildDecisionModelAsync(projections);
for (var i = 0; i < bookIds.Count; i++)
{
    if (!states[i].IsValidPrice(command.Items[i].DisplayedPrice))
        return CommandResult.Fail($"Price for book {bookIds[i]} is no longer valid.");
}
await eventStore.AppendAsync(orderedEvent, condition);
```

The `CartPrices` compound projection is **no longer needed** and is not implemented. This
simplifies `CourseBookPriceProjections.cs` and makes Feature 3 a direct 1:1 mapping of
the DCB JS example.

**Files to change:**

| File | Change |
|---|---|
| `src/Opossum/DecisionModel/DecisionModelExtensions.cs` | Add `IReadOnlyList<IDecisionProjection<TState>>` overload |
| `tests/Opossum.UnitTests/DecisionModel/BuildDecisionModelTests.cs` | Add unit tests for N-ary overload |
| `tests/Opossum.IntegrationTests/DecisionModel/BuildDecisionModelIntegrationTests.cs` | Add integration tests |
| `Samples/.../CourseBookPriceProjections.cs` | Remove `CartPrices` compound projection |
| `Samples/.../OrderCourseBooksCommand.cs` | Use N-ary `BuildDecisionModelAsync` |

---

### 3.3 ✅ Both Gaps Resolved — Library Enhancements In Scope

All three DCB features are implemented with **no workarounds**. The two enhancements above are
purely additive, non-breaking changes to `src/Opossum/` that make the N-ary DCB pattern and
time-injectable projections first-class library features. The `CartPrices` compound projection
and the manual `DateTimeOffset now` parameter pattern are both eliminated.

---

## 4. Maturity Verdict

| Capability | Status | Notes |
|---|---|---|
| Tags per event | ✅ | `Tag(key, value)` record, multi-tag support |
| Tag-scoped queries | ✅ | `QueryItem.Tags` |
| Decision projections | ✅ | `DecisionProjection<TState>` |
| `Metadata.Timestamp` for grace period | ✅ | `SequencedEvent.Metadata.Timestamp` |
| Single-book price check (F1) | ✅ | `BuildDecisionModelAsync<TState>` |
| Grace period check (F2) | ✅ | Explicit-`now` pattern |
| Multi-book cart (F3) | ✅ | Compound projection pattern |
| Concurrency guard | ✅ | `AppendCondition.FailIfEventsMatch` |
| Retry on conflict | ✅ | `ExecuteDecisionAsync` |
| Read-side catalog projection | ✅ | `IProjectionDefinition<TState>` |
| N-ary typed projection overload | ✅ | `BuildDecisionModelAsync(IReadOnlyList<IDecisionProjection<TState>>)` added |
| Clock injection standard | ✅ | `TimeProvider` constructor overload in `DecisionProjection<TState>` added |

**Conclusion: Opossum is fully capable of implementing the Dynamic Course Book Price example with
no workarounds.** Both previously identified gaps are resolved as in-scope library enhancements.

---

## 5. Implementation Plan

### 5.1 File Structure

#### Library Changes (`src/Opossum/`)

```
src/Opossum/DecisionModel/
├── DecisionProjection.cs      ← add TimeProvider-aware constructor overload
└── DecisionModelExtensions.cs ← add IReadOnlyList<IDecisionProjection<TState>> overload

tests/Opossum.UnitTests/DecisionModel/
└── DecisionProjectionTests.cs ← add tests for TimeProvider overload
    BuildDecisionModelTests.cs ← add tests for N-ary overload

tests/Opossum.IntegrationTests/DecisionModel/
└── BuildDecisionModelIntegrationTests.cs ← add N-ary integration tests
```

#### Sample Application (`Samples/Opossum.Samples.CourseManagement/`)

All new files follow the existing feature-folder convention:

```
CourseBookManagement/
├── Events/
│   ├── CourseBookDefinedEvent.cs
│   ├── CourseBookPriceChangedEvent.cs
│   ├── CourseBookPurchasedEvent.cs          ← F1 (single book)
│   └── CourseBooksOrderedEvent.cs           ← F3 (shopping cart)
├── CourseBookCatalog/
│   ├── CourseBookCatalogProjection.cs       ← read-side, persisted
│   └── GetCourseBookCatalog.cs              ← query endpoint
├── CourseBookPurchase/
│   ├── CourseBookPriceState.cs              ← shared projection state record
│   ├── CourseBookPriceProjections.cs        ← F1 CurrentPrice, F2 PriceWithGracePeriod only
│   │                                           (CartPrices removed — use N-ary overload)
│   ├── DefineCourseBook.cs                  ← admin command + endpoint
│   ├── ChangeCourseBookPrice.cs             ← admin command + endpoint
│   ├── PurchaseCourseBookCommand.cs         ← F1 + F2 command + endpoint
│   └── OrderCourseBooksCommand.cs           ← F3 command + endpoint (uses N-ary overload)
├── CourseBookOrderHistory/
│   ├── CourseBookOrderHistoryProjection.cs  ← read-side, persisted
│   └── GetCourseBookOrderHistory.cs         ← query endpoint
```

### 5.2 Events

#### `CourseBookDefinedEvent`
```csharp
// Tags: bookId:{bookId}
public sealed record CourseBookDefinedEvent(
    Guid BookId,
    string Title,
    string Author,
    string Isbn,
    decimal Price) : IEvent;
```

#### `CourseBookPriceChangedEvent`
```csharp
// Tags: bookId:{bookId}
public sealed record CourseBookPriceChangedEvent(
    Guid BookId,
    decimal NewPrice) : IEvent;
```

#### `CourseBookPurchasedEvent` (Feature 1 & 2)
```csharp
// Tags: bookId:{bookId}, studentId:{studentId}
public sealed record CourseBookPurchasedEvent(
    Guid BookId,
    Guid StudentId,
    decimal PricePaid) : IEvent;
```

#### `CourseBooksOrderedEvent` (Feature 3)
```csharp
// Tags: bookId:{bookId} for EACH item in the cart
public sealed record CourseBooksOrderedEvent(
    Guid StudentId,
    IReadOnlyList<CourseBookOrderItem> Items) : IEvent;

public sealed record CourseBookOrderItem(Guid BookId, decimal PricePaid);
```

### 5.3 Decision Projections

#### Feature 1 — `CurrentPrice`
```
State: decimal (negative sentinel = book not found)
Query: CourseBookDefinedEvent tagged bookId:{id}
Apply: CourseBookDefinedEvent → event.Price
```

#### Feature 2 — `PriceWithGracePeriod`
```
Signature: PriceWithGracePeriod(Guid bookId, TimeProvider? timeProvider = null)
State:     CourseBookPriceState(decimal? LastValidOldPrice, IReadOnlyList<decimal> ValidNewPrices)
Query:     CourseBookDefinedEvent + CourseBookPriceChangedEvent tagged bookId:{id}
Apply:     TimeProvider-aware — age = tp.GetUtcNow() - evt.Metadata.Timestamp
  CourseBookDefinedEvent:
    if age ≤ gracePeriod → { LastValidOldPrice: null, ValidNewPrices: [price] }
    else                 → { LastValidOldPrice: price, ValidNewPrices: [] }
  CourseBookPriceChangedEvent:
    if age ≤ gracePeriod → ValidNewPrices += newPrice
    else                 → LastValidOldPrice = newPrice, ValidNewPrices unchanged
```

The `IsValidPrice(decimal displayed)` method on the state record:
```csharp
public bool IsValidPrice(decimal displayed) =>
    LastValidOldPrice == displayed || ValidNewPrices.Contains(displayed);
```

#### Feature 3 — No Dedicated `CartPrices` Projection

F3 uses the N-ary `BuildDecisionModelAsync` overload directly in the command handler.
One `PriceWithGracePeriod` projection is created per cart item; the overload issues one
`ReadAsync` and returns a per-book state list. No separate compound projection is needed.

### 5.4 Command Handlers

#### `DefineCourseBookCommandHandler`
Simple unconditional append of `CourseBookDefinedEvent`.
Could optionally add a `CourseBookExists` decision projection to prevent redefining a book
(same pattern as existing `CourseCreated` guard in course enrollment).

#### `ChangeCourseBookPriceCommandHandler`
Reads `CourseBookExists` projection (guard: book must exist) and appends
`CourseBookPriceChangedEvent` with the book's `AppendCondition`.

#### `PurchaseCourseBookCommandHandler` (F1 / F2)
```
1. BuildDecisionModelAsync(PriceWithGracePeriod(bookId))
   — TimeProvider.System used by default; no 'now' capture needed
2. Validate: state.IsValidPrice(command.DisplayedPrice)
3. AppendAsync(CourseBookPurchasedEvent, condition)
```
Wrapped in `ExecuteDecisionAsync` for automatic retry.

#### `OrderCourseBooksCommandHandler` (F3)
```
1. Build List<IDecisionProjection<CourseBookPriceState>> — one PriceWithGracePeriod per item
2. BuildDecisionModelAsync(projections)  ← N-ary overload; single ReadAsync
3. For each item at index i: validate states[i].IsValidPrice(item.DisplayedPrice)
4. AppendAsync(CourseBooksOrderedEvent, condition)
```
Wrapped in `ExecuteDecisionAsync`.

### 5.5 Read-Side Projections

#### `CourseBookCatalogProjection`
- Persisted projection using `[ProjectionDefinition]` / `[ProjectionTags]`
- State: `Dictionary<Guid, CourseBookCatalogEntry>` (bookId → title, author, isbn, currentPrice)
- Folds: `CourseBookDefinedEvent` (add entry), `CourseBookPriceChangedEvent` (update price)
- Endpoint: `GET /course-books`

#### `CourseBookOrderHistoryProjection`
- State: list of `CourseBookOrderHistoryEntry` (studentId, bookId, pricePaid, timestamp)
- Folds: `CourseBookPurchasedEvent` and `CourseBooksOrderedEvent`
- Endpoint: `GET /course-books/orders` (optionally filtered by studentId)

### 5.6 API Endpoints

All endpoints registered in `Program.cs` under the **"Course Books (Dynamic Price)"** Scalar tag:

| Method | Route | Description |
|---|---|---|
| `POST` | `/course-books` | Define a new course book (admin) |
| `PATCH` | `/course-books/{bookId}/price` | Change price (admin) |
| `POST` | `/course-books/{bookId}/purchase` | Purchase single book — F1/F2 |
| `POST` | `/course-books/order` | Order multiple books — F3 (shopping cart) |
| `GET` | `/course-books` | List current catalog with prices |
| `GET` | `/course-books/orders` | List order history |

### 5.7 Tests

#### Unit Tests (`Opossum.Samples.CourseManagement.UnitTests`)

Projection logic only — no I/O, no mocks:

**Library unit tests (`Opossum.UnitTests`):**

| Test class | What it tests |
|---|---|
| `DecisionProjectionTests` | `TimeProvider` overload: default (`TimeProvider.System`), custom, null-guard |
| `BuildDecisionModelTests` | N-ary overload: empty-list guard, single-item, multi-item, independent fold per projection |

**Sample unit tests (`Opossum.Samples.CourseManagement.UnitTests`):**

| Test class | What it tests |
|---|---|
| `CourseBookPriceProjectionsTests` | F1 projection initial state, apply for defined event |
| `CourseBookPriceWithGracePeriodTests` | F2: within grace, past grace, never-valid price, multiple price changes |
| `CourseBookPriceStateTests` | `IsValidPrice` boundary cases |

Minimum target: **~18 unit tests** covering all projection variants, library overloads, and edge cases.

#### Integration Tests (`Opossum.Samples.CourseManagement.IntegrationTests`)

Full HTTP round-trip against real file-system event store:

| Test class | What it tests |
|---|---|
| `CourseBookManagementIntegrationTests` | Define book, change price → catalog updated |
| `CoursBookPurchaseIntegrationTests` | F1 valid price, F1 wrong price; F2 grace period valid, F2 expired |
| `CourseBooksOrderIntegrationTests` | F3 single item, F3 multiple items, F3 one invalid price |

Minimum target: **~12 integration tests**.

### 5.8 Step-by-Step Implementation Order

#### Phase 0 — Library Enhancements (`src/Opossum/`)

1. **`TimeProvider` constructor overload** — add to `DecisionProjection<TState>`; add unit tests
2. **N-ary `BuildDecisionModelAsync` overload** — add to `DecisionModelExtensions`;
   add unit tests and integration tests
3. **Build + zero warnings** — verify `dotnet build` before proceeding to sample code

#### Phase 1 — Sample Application

4. **Events** — create the four event records
5. **Projections (write-side)** — `CourseBookPriceState`, `CourseBookPriceProjections`
   (F1 `CurrentPrice`, F2 `PriceWithGracePeriod` with `TimeProvider`; no `CartPrices`)
6. **Unit tests** — validate all projection logic before writing any HTTP code
7. **Admin commands** — `DefineCourseBook`, `ChangeCourseBookPrice`
8. **Feature 1 command** — `PurchaseCourseBookCommand` (single book, current price)
9. **Feature 2 command** — extend `PurchaseCourseBookCommand` with `PriceWithGracePeriod`
10. **Feature 3 command** — `OrderCourseBooksCommand` using N-ary `BuildDecisionModelAsync`
11. **Read-side projections** — `CourseBookCatalogProjection`, ~~`CourseBookOrderHistoryProjection`~~
    > ⚠️ `CourseBookOrderHistoryProjection` was implemented but later **removed**.
    > It used `evt.Position` as the key selector, creating one projection file per purchase event
    > (O(Events) cardinality). On a Large-seeded database this produced 700,000+ files and never
    > completed rebuilding. Only `CourseBookCatalogProjection` (keyed by `bookId`, O(Books)) was
    > correct. See `docs/lessons-learned/course-book-order-history-projection-mistake.md`.
12. **API endpoints** — wire everything in `Program.cs`
13. **Integration tests** — test all endpoints
14. **CHANGELOG** — update `## [Unreleased]` section

---

## 6. Open Questions / Decisions Needed Before Implementation

| # | Question | Default Assumption |
|---|---|---|
| 1 | Grace period duration? | 30 minutes |
| 2 | Should `DefineCourseBook` guard against redefining an existing book? | Yes — append a `CourseBookExists` projection guard |
| 3 | Should students have to be registered to purchase? | Yes — reuse the existing `StudentRegistered` projection |
| 4 | Should `CourseBooksOrderedEvent` also carry a `studentId` tag for order-history queries? | Yes |
| 5 | Should the catalog and order-history projections be tag-scoped (per book) or global? | Global (one projection file per store) ⚠️ **This assumption was ambiguous and led to a design mistake.** "Global" was interpreted as one file per event (key = `evt.Position`) instead of one file per entity. The catalog was correctly implemented per-book (key = `bookId`). The order-history was incorrectly implemented and subsequently removed. See `docs/lessons-learned/course-book-order-history-projection-mistake.md`. |
| 6 | Should Feature 1 and Feature 2 share the same endpoint or be separate? | Same endpoint — the handler always uses `PriceWithGracePeriod`; a fixed price is just one where no price changes have occurred |

---

## 7. Relationship to Prior Analysis Documents

### Was this example analyzed before?

**No.** A search across all five existing analysis documents (`dcb-compliance-analysis.md`,
`event-sourced-aggregate-feasibility.md`, `prevent-record-duplication-feasibility.md`,
`aggregate-vs-dcb-comparison.md`, `use-case-fit-post-adr005.md`) finds zero mentions of
"dynamic product price", "grace period", "time injection", `TimeProvider`, "shopping cart",
or N-ary projections. This is the first dedicated analysis of this DCB example.

### Are the two gaps new findings?

**Yes — both are genuinely new observations.**

**Gap 1 (time injection)** does not appear anywhere in prior documentation. It is a new
observation about the testability of time-dependent fold functions in `DecisionProjection<TState>`.

**Gap 2 (N-ary projections)** is a **refinement of an existing ✅ in `dcb-compliance-analysis.md`**.
That document records:

> `ComposeProjections()` helper — ✅ Implemented — Multi-projection overloads of `BuildDecisionModelAsync`

The compliance check only asked *"do overloads exist?"* and found the 1-, 2-, and 3-projection
variants — so it marked the item as done. It did not ask *"do the overloads cover all DCB usage
patterns?"* and therefore never reached the runtime-variable-N case that Feature 3 of this example
requires. The new analysis is the first to surface this limitation.

**What this means for the compliance doc:** With the N-ary overload now implemented,
`ComposeProjections()` should be updated to `✅ Fully — 1/2/3-projection typed overloads exist
plus an N-ary homogeneous overload for runtime-variable lists`. The documentation debt item
is resolved.

---

## 8. Summary

The "Dynamic Course Book Price" DCB example is a **perfect fit** for Opossum. The two minor
ergonomic gaps identified in the original analysis have been promoted to **in-scope library
enhancements** and are implemented as part of this task:

- **`TimeProvider` constructor overload in `DecisionProjection<TState>`** — standardises
  time-injection across the library; eliminates the ad-hoc `DateTimeOffset now` parameter
  pattern; enables unit-testing of time-dependent fold functions without external packages.
- **N-ary `BuildDecisionModelAsync` overload** — makes the runtime-variable-N DCB pattern
  first-class; eliminates the compound projection workaround for Feature 3; maps 1:1 to
  the DCB JS reference implementation.

With both enhancements in place, all three features are implemented idiomatically and without
workarounds. The library is now fully aligned with the DCB specification for this example.
