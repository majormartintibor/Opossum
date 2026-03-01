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

## 4. Chosen Domain Feature: Course Announcement + Retract

**Course Announcement** was selected as the showcase feature. An instructor posts an
announcement to a course. The browser submits, the network drops before the response
arrives, and the instructor's client retries. Without idempotency the same announcement
appears twice in the course feed.

The domain is a perfect fit for the DCB spec's stated goal: *"constraints that are not
directly related to the domain."* A course can legitimately have many announcements — there
is no domain rule that says "you can only post one announcement." The idempotency token is
therefore the **sole** mechanism preventing the duplicate; it is not supplementing a domain
constraint that would already solve the problem.

The feature supports a natural extension: a **retract flow** that demonstrates the DCB spec
note *"allow a token to be reused once the order was placed."* After retraction the original
token is freed — the instructor can re-post the same announcement (corrected, for example)
using the same idempotency token their client already holds.

---

## 5. Summary

| Question | Answer |
|---|---|
| Are all required primitives present in Opossum? | **Yes** |
| Does tag-based scoping work for idempotency keys? | **Yes** |
| Is a single-projection `BuildDecisionModelAsync` available? | **Yes** |
| Is the `(_, _) => true` projection pattern already used in the sample? | **Yes** (`AlreadyEnrolled`) |
| Does `AppendCondition` scope correctly to the narrow query? | **Yes** |
| Is retry-on-conflict available? | **Yes** (`ExecuteDecisionAsync`) |
| Are there any library-level changes needed? | **No** |
| Can the extended "token reuse after retract" variant be built? | **Yes** — stateful projection with two event types |
| Does the domain have a natural uniqueness constraint on announcements? | **No** — making the token the genuine sole guard |
| Best domain fit for the sample? | **Course Announcement + Retract** |

**Conclusion:** The "Prevent Record Duplication" example can be showcased in the Opossum sample
application in its current state without any modifications to the core library. The pattern
maps cleanly onto existing Opossum primitives and is structurally almost identical to the
`AlreadyEnrolled` projection that is already present in the sample.

---

## 6. Why Course Announcements Are the Right Domain Fit

The DCB spec states: *"This example demonstrates how DCB allows to enforce constraints that
are not directly related to the domain."*

For a domain event like `CourseFeePaymentProcessedEvent`, a `CourseFeePaid` projection
scoped to `(courseId, studentId)` would naturally exist alongside any idempotency guard —
payment is inherently a domain-constrained operation. The idempotency token would then be
supplementing a domain constraint that already solves the safety problem, which obscures the
pattern.

Course announcements have no such natural uniqueness rule. A course can have ten
announcements from Monday alone. The domain language has no concept of "already announced."
The *only* reason the same announcement should not appear twice is that it was an accidental
retry — which is precisely the infrastructure concern the idempotency token is designed to
address.

This makes the token the **honest** sole guard, not a supplement. The showcase is then about
what the spec actually claims: enforcing an infrastructure constraint on top of an event store,
using the same DCB primitives, without any domain-level backing.

---

## 7. Implementation Plan

### 7.1 New Events

| Event | Fields | Tags |
|---|---|---|
| `CourseAnnouncementPostedEvent` | `AnnouncementId`, `CourseId`, `Title`, `Body`, `IdempotencyToken` | `courseId:{id}`, `idempotency:{token}` |
| `CourseAnnouncementRetractedEvent` | `AnnouncementId`, `CourseId`, `IdempotencyToken` | `courseId:{id}`, `idempotency:{token}` |

`AnnouncementId` is server-assigned (a new `Guid` created in the command handler).
`IdempotencyToken` is client-generated and stored in both events. The retracted event carries
the **original** token so the `IdempotencyTokenWasUsed` projection can observe both events
and derive the current state — this is what enables token reuse after retraction.

### 7.2 API Design

**Post announcement**

```
POST /courses/{courseId}/announcements
Body: { "title": "...", "body": "...", "idempotencyToken": "..." }
→ 201 Created  { "announcementId": "..." }
→ 400 Bad Request  "Course does not exist"   — prerequisite not met
→ 400 Bad Request  "Re-submission detected"  — token already used (caught at decision model)
```

**Retract announcement**

```
POST /courses/{courseId}/announcements/{idempotencyToken}/retract
(no body)
→ 200 OK
→ 400 Bad Request  "Announcement not found"
→ 400 Bad Request  "Announcement has already been retracted"
```

The idempotency token doubles as the stable announcement reference in the retract URL. The
client already holds the token from the original post response — no separate lookup is needed.

### 7.3 Feature 1 — Post Course Announcement

#### Decision Model: 2 Composed Projections

One business prerequisite and the idempotency guard. The domain has no "already posted"
uniqueness constraint, so the token is the sole mechanism preventing duplicates (see § 6).

---

**Projection 1 — `CourseExists(courseId)` → `bool`**

A prerequisite: the course must exist before an announcement can be posted.

```
Query   : CourseCreatedEvent — tag courseId:{id}
Apply   : (_, _) => true
Initial : false
Guard   : if (!courseExists) → "Course does not exist"
```

---

**Projection 2 — `IdempotencyTokenWasUsed(token)` → `bool`**

The sole guard against duplicate announcements. This is the "Prevent Record Duplication"
pattern from the DCB spec.

```
Query   : CourseAnnouncementPostedEvent + CourseAnnouncementRetractedEvent — tag idempotency:{token}
Apply   : CourseAnnouncementPostedEvent    → true    (token consumed)
          CourseAnnouncementRetractedEvent → false   (token freed — see § 7.4)
Initial : false
Guard   : if (tokenWasUsed) → "Re-submission detected: this request has already been processed"
```

The query is scoped exclusively to the `idempotency` tag. Two instructors posting two
different announcements simultaneously use independent tokens and independent
`AppendCondition` instances — they never block each other. Two concurrent retries of the
same post share the same token: only one append succeeds; the retry's decision model reads
`tokenWasUsed = true` and returns the re-submission error without any special handling.

#### Handler Logic

```
1. BuildDecisionModelAsync(CourseExists, IdempotencyTokenWasUsed)
2. if (!courseExists)   → Fail("Course does not exist")
3. if (tokenWasUsed)    → Fail("Re-submission detected")
4. var announcementId = Guid.NewGuid()
5. AppendAsync(CourseAnnouncementPostedEvent, appendCondition)
6. return Ok(announcementId)
```

Wrapped in `ExecuteDecisionAsync` for automatic retry on `AppendConditionFailedException`.

### 7.4 Feature 2 — Retract Course Announcement (Token Reuse Extension)

#### Domain Story

An instructor posts an announcement, then realises it contained an error and retracts it.
They correct the text and want to re-post. Their client still holds the original idempotency
token — after retraction, that token is freed and can be reused for the corrected post.

#### Decision Model: 1 Projection

**`RetractableAnnouncement(idempotencyToken)` → `RetractableAnnouncementState?`**

```
Query   : CourseAnnouncementPostedEvent + CourseAnnouncementRetractedEvent — tag idempotency:{token}
Apply   : CourseAnnouncementPostedEvent    → RetractableAnnouncementState(AnnouncementId, CourseId, IsRetracted: false)
          CourseAnnouncementRetractedEvent → state with { IsRetracted = true }
Initial : null
```

`null` means no announcement was found for this token. `IsRetracted = true` means the
announcement exists but has already been retracted.

#### Handler Logic

```
1. BuildDecisionModelAsync(RetractableAnnouncement(idempotencyToken))
2. if (state is null)        → Fail("Announcement not found")
3. if (state.IsRetracted)    → Fail("Announcement has already been retracted")
4. AppendAsync(CourseAnnouncementRetractedEvent with idempotency:{token} tag, appendCondition)
5. return Ok()
```

#### How Token Reuse Works

After retraction:

1. `CourseAnnouncementRetractedEvent` is stored with `idempotency:{originalToken}` tag.
2. On the next post attempt, `IdempotencyTokenWasUsed(originalToken)` loads both events in
   sequence order: posted first (→ `true`), retracted second (→ `false`).
3. Final state is `false` — the token is free.
4. `AppendCondition.AfterSequencePosition` is set to the retraction event's position,
   guarding the new post against any concurrent activity on this token since the retraction.

No logic changes are needed in the post handler. The token reuse is a *consequence of the
event fold*, not a special case carved out in the handler.

### 7.5 Folder and File Structure

```
Samples/Opossum.Samples.CourseManagement/
  Events/
    CourseAnnouncementPostedEvent.cs        (new)
    CourseAnnouncementRetractedEvent.cs     (new)
  CourseAnnouncement/
    Endpoint.cs                             (new) — POST /courses/{id}/announcements
    PostCourseAnnouncementCommand.cs        (new) — command + handler
    CourseAnnouncementProjections.cs        (new) — CourseExists + IdempotencyTokenWasUsed
  CourseAnnouncementRetraction/
    Endpoint.cs                             (new) — POST /courses/{id}/announcements/{token}/retract
    RetractCourseAnnouncementCommand.cs     (new) — command + handler
    CourseAnnouncementRetractionProjection.cs (new) — RetractableAnnouncement projection
```

### 7.6 Test Plan

#### Unit Tests — `CourseAnnouncementProjections`

Projection logic only — no event store, no file system.

| Test | Projection | Given | Expected state |
|---|---|---|---|
| No events | `IdempotencyTokenWasUsed` | `[]` | `false` |
| Matching posted event | `IdempotencyTokenWasUsed` | `[Posted(T)]` | `true` |
| Different token | `IdempotencyTokenWasUsed` | `[Posted(T)]` | `false` (queried with T2) |
| Token freed by retraction | `IdempotencyTokenWasUsed` | `[Posted(T), Retracted(T)]` | `false` |
| Token re-consumed after retraction | `IdempotencyTokenWasUsed` | `[Posted(T), Retracted(T), Posted(T)]` | `true` |

#### Unit Tests — `CourseAnnouncementRetractionProjection`

| Test | Given | Expected state |
|---|---|---|
| No events | `[]` | `null` |
| Announcement exists | `[Posted(T)]` | `{IsRetracted: false}` |
| Already retracted | `[Posted(T), Retracted(T)]` | `{IsRetracted: true}` |

#### Integration Tests — Post Announcement

| Scenario | Expected |
|---|---|
| First post with fresh token | 201 Created |
| Re-submission with same token | 400 "Re-submission detected" |
| Post to non-existent course | 400 "Course does not exist" |
| Same token after retraction | 201 Created — token was freed |
| New token after retraction | 201 Created — fresh token always works |

#### Integration Tests — Retract Announcement

| Scenario | Expected |
|---|---|
| Retract existing announcement | 200 OK |
| Retract non-existent token | 400 "Announcement not found" |
| Double retraction | 400 "Announcement has already been retracted" |
