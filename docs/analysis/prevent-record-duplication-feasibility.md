# Feasibility Analysis: "Prevent Record Duplication" DCB Example in Opossum

**Source:** https://dcb.events/examples/prevent-record-duplication/  
**Date:** 2025  
**Scope:** Can the example be showcased in `Opossum.Samples.CourseManagement` using the current state of the Opossum library?

---

## 1. What the DCB Example Demonstrates

The example solves the **idempotency problem** in distributed HTTP APIs: a client may retry a
request due to network failures, and without safeguards the server would process the same
operation multiple times (e.g., placing an order twice).

### The Mechanism

1. The **client** generates a random UUID called an `idempotencyToken` and includes it in the command.
2. The **server** tags the resulting event with _both_ `order:{orderId}` and `idempotency:{token}`.
3. A **Decision Model projection** (`IdempotencyTokenWasUsedProjection`) queries the event store
   filtered by `idempotency:{token}` and folds any matching event into `true` (token was used).
4. Before appending, the handler checks the projection state:
   - `true` → reject with a "Re-submission" error.
   - `false` → proceed; append the event.
5. The `AppendCondition` is automatically scoped to the narrow `idempotency:{token}` tag query,
   so concurrent requests with **different** tokens never block each other.

### Test Cases in the Spec

| Scenario | Given | When | Then |
|---|---|---|---|
| Duplicate token | `OrderPlaced` with token `11111` already exists | Place order with token `11111` | Error: "Re-submission" |
| Fresh token | `OrderPlaced` with token `11111` already exists | Place order with token `22222` | `OrderPlaced` appended successfully |

---

## 2. Mapping to Opossum Primitives

### 2.1 Tags — ✅ Fully Supported

Opossum's `Tag(string Key, string Value)` record is exactly what the spec needs:

```csharp
.WithTag("orderId", command.OrderId.ToString())
.WithTag("idempotency", command.IdempotencyToken.ToString())
```

Tags are indexed at write time and queryable via `QueryItem.Tags`. There is no constraint on
what key/value pairs are used — an `idempotency` tag key is entirely valid today.

### 2.2 Tag-Scoped Query — ✅ Fully Supported

The spec requires a query that matches **only** events carrying a specific idempotency token:

```csharp
// DCB JS spec equivalent: tagFilter: [`idempotency:${idempotencyToken}`]
Query.FromItems(new QueryItem
{
    EventTypes = [nameof(OrderPlacedEvent)],
    Tags = [new Tag("idempotency", command.IdempotencyToken.ToString())]
})
```

`Query.FromItems` and `QueryItem.Tags` are available and produce exactly this AND-scoped filter.

### 2.3 Single-Projection Decision Model — ✅ Fully Supported

The idempotency check needs only **one projection** (`bool` state). Opossum has:

```csharp
var model = await eventStore.BuildDecisionModelAsync(
    IdempotencyProjection(command.IdempotencyToken));

if (model.State)
    return CommandResult.Fail("Re-submission");

await eventStore.AppendAsync(orderEvent, model.AppendCondition);
```

`BuildDecisionModelAsync<TState>(IDecisionProjection<TState>)` is the single-projection overload
and is available today.

### 2.4 The Projection Pattern — ✅ Already Used in the Sample

The `AlreadyEnrolled` projection in `CourseEnrollmentProjections.cs` is structurally identical:

```csharp
// Existing AlreadyEnrolled — same (_, _) => true pattern
public static IDecisionProjection<bool> AlreadyEnrolled(Guid courseId, Guid studentId) =>
    new DecisionProjection<bool>(
        initialState: false,
        query: Query.FromItems(new QueryItem
        {
            EventTypes = [nameof(StudentEnrolledToCourseEvent)],
            Tags = [new Tag("courseId", courseId.ToString()), new Tag("studentId", studentId.ToString())]
        }),
        apply: (_, _) => true);
```

The idempotency projection uses the same pattern, just scoped to the `idempotency` tag instead
of a pair of domain entity IDs.

### 2.5 AppendCondition Scoping — ✅ Correct Behaviour Out of the Box

`BuildDecisionModelAsync` automatically sets `AppendCondition.FailIfEventsMatch` to the
projection's own `Query` and `AfterSequencePosition` to the max position of loaded events.
Because the query is narrowly scoped to `idempotency:{token}`, concurrent requests with
**different** tokens produce completely independent `AppendCondition` instances and do not
block each other. This is precisely the DCB spec's intended behaviour.

### 2.6 Retry on Conflict — ✅ Fully Supported

`ExecuteDecisionAsync` handles the retry loop already used in `EnrollStudentToCourseCommand`:

```csharp
return await eventStore.ExecuteDecisionAsync(
    (store, ct) => TryPlaceOrderAsync(command, store));
```

---

## 3. "Composed Projections" — Is There a Gap?

The DCB website labels this example as using **composed projections**. In the JS reference
implementation `buildDecisionModel` accepts a named dictionary of projections. Opossum instead
provides tuple-returning overloads for 1, 2, and 3 projections:

```csharp
// 1 projection
var model = await eventStore.BuildDecisionModelAsync(projectionA);

// 2 projections (composed)
var (stateA, stateB, condition) = await eventStore.BuildDecisionModelAsync(projA, projB);

// 3 projections (composed)
var (stateA, stateB, stateC, condition) = await eventStore.BuildDecisionModelAsync(projA, projB, projC);
```

For the _base_ "prevent record duplication" example, only one projection is required, so the
tuple API is a perfect fit and no gap exists. For the _extended_ version mentioned in the spec
("also ensure uniqueness of the orderId"), the 2-projection overload would be used:

```csharp
var (idempotencyUsed, orderAlreadyExists, condition) = await eventStore.BuildDecisionModelAsync(
    IdempotencyProjection(command.IdempotencyToken),
    OrderExistsProjection(command.OrderId));
```

This is fully supported today. The only difference from the JS reference is aesthetic
(positional tuple vs. named dictionary) — not a functional limitation.

---

## 4. Domain Fit in `Opossum.Samples.CourseManagement`

The sample app is a course/student management system. Introducing a bare "order placement"
concept would be a foreign domain. Two natural integration paths exist:

### Option A — Add Idempotency to an Existing Command

Add an optional `IdempotencyToken` to `EnrollStudentToCourseCommand`. The enrollment handler
would gain a third projection alongside the existing two (`CourseCapacity`,
`StudentEnrollmentLimit`). This uses the 3-projection `BuildDecisionModelAsync` overload and
requires no new event type — the `idempotency` tag is just added to `StudentEnrolledToCourseEvent`.

**Pros:** No new domain concepts; shows composed projections in context.  
**Cons:** Complicates an already 3-projection handler; the existing `AlreadyEnrolled` projection
already partially serves the same purpose for this specific command.

### Option B — New Self-Contained Feature: Course Registration via Idempotent Token

Add a new `RegisterForCourse` feature (distinct from the current enrollment flow) that
demonstrates idempotent course sign-up. A student sends a client-generated `registrationToken`
when registering. The feature is self-contained and mirrors the DCB spec example almost
verbatim, just with course-domain names instead of order-domain names.

**Pros:** Clean 1:1 mapping to the spec; clearly labelled for documentation; no risk of
breaking existing features.  
**Cons:** Slight domain overlap with enrollment.

### Option C — New Thin Domain: Order / Payment (separate section of the sample)

Add a minimal `Orders` section to the sample app that is explicitly presented as
"DCB Pattern Showcase — Idempotency". Only two routes: `POST /orders` (place order with
idempotency token) and `GET /orders/{id}` (read model). Completely isolated from the
course domain.

**Pros:** Exact mirror of the spec example; no domain awkwardness.  
**Cons:** Introduces a second domain concept into what is presented as a single-domain sample.

---

## 5. What Would Need to Be Created

Regardless of which domain option is chosen, the implementation needs:

| Artefact | Notes |
|---|---|
| `OrderPlacedEvent` (or domain-equivalent) | Record implementing `IEvent`; holds `orderId` + `idempotencyToken` |
| `PlaceOrderCommand` / request DTO | Carries `OrderId (Guid)` + `IdempotencyToken (Guid)` |
| `IdempotencyTokenWasUsedProjection(Guid token)` | `DecisionProjection<bool>`, initial `false`, query on `idempotency` tag, apply `(_, _) => true` |
| Command handler | `BuildDecisionModelAsync` → check → `AppendAsync` with condition |
| Minimal endpoint | `POST /orders` returns 201 on first call, 409 (or 400) on re-submission |
| Unit tests | Two cases from the spec: duplicate token → error, fresh token → success |
| Integration tests | Full round-trip via the Opossum file system event store |

No changes to the Opossum core library are required.

---

## 6. Summary

| Question | Answer |
|---|---|
| Are all required primitives present in Opossum? | **Yes** |
| Does tag-based scoping work for idempotency keys? | **Yes** |
| Is a single-projection `BuildDecisionModelAsync` available? | **Yes** |
| Is the `(_, _) => true` projection pattern already used in the sample? | **Yes** (`AlreadyEnrolled`) |
| Does `AppendCondition` scope correctly to the narrow query? | **Yes** |
| Is retry-on-conflict available? | **Yes** (`ExecuteDecisionAsync`) |
| Are there any library-level changes needed? | **No** |
| Can the extended "also check orderId uniqueness" variant be built? | **Yes** (2-projection overload) |
| Best domain fit for the sample? | **Option B** (new self-contained idempotent registration feature) or **Option C** (thin `Orders` showcase section) |

**Conclusion:** The "Prevent Record Duplication" example can be showcased in the Opossum sample
application in its current state without any modifications to the core library. The pattern
maps cleanly onto existing Opossum primitives and is structurally almost identical to the
`AlreadyEnrolled` projection that is already present in the sample.
