# Analysis: "Unique Username" DCB Example in Opossum

**Source:** https://dcb.events/examples/unique-username/  
**Status:** ✅ Implemented — `Opossum.Samples.CourseManagement`  
**Scope:** Maps the DCB "Unique Username" example to its Opossum implementation and documents
the intentional use of the lower-level DCB API as a valid alternative pattern.

---

## 1. What the DCB Example Demonstrates

The "Unique Username" example tackles one of the hardest consistency problems in eventually
consistent systems: **enforcing a globally unique value across all users**.

### Business Rules

1. No two users may share the same username (or, in the domain adaptation, the same email address).
2. The uniqueness check and the registration append must be atomic — a race between two
   concurrent registrations with the same username must result in exactly one succeeding.

### Why This Is Hard Without DCB

Traditional solutions involve:
- A unique index in a relational database (couples to SQL).
- A Redis lock or similar distributed primitive (complex, requires extra infrastructure).
- Optimistic versioning on a "usernames" aggregate (forces all registrations to serialise
  through a single stream — a hot spot).

DCB solves this with a **query-scoped optimistic lock**: the uniqueness check and the guard
query are exactly the same query, so the consistency boundary covers only the specific
username/email being registered. Two concurrent registrations of _different_ usernames are
completely independent and never block each other.

---

## 2. Domain Adaptation in `Opossum.Samples.CourseManagement`

| DCB Example | Opossum Sample |
|---|---|
| Unique username | Unique student email address |
| `UsernameRegistered` event | `StudentRegisteredEvent` |
| `username:{value}` tag | `studentEmail:{email}` tag |

The username concept maps naturally to the student email — it is the globally unique
identifier used to identify a student across the system, and it is supplied by the user at
registration time.

### Events Used

| Event | Tags | Purpose |
|---|---|---|
| `StudentRegisteredEvent` | `studentId:{id}` + `studentEmail:{email}` | Records the student and binds the email to the student ID |

---

## 3. Implementation Approach: Raw DCB API

The `RegisterStudentCommandHandler` uses the **raw DCB API** — direct `ReadAsync` plus an
explicit `AppendCondition` — rather than the higher-level `BuildDecisionModelAsync`:

```csharp
// Step 1 — Read: check whether the email is already taken
var uniquenessQuery = Query.FromItems(new QueryItem
{
    Tags = [new Tag("studentEmail", command.Email)],
    EventTypes = []
});

var existing = await eventStore.ReadAsync(uniquenessQuery, ReadOption.None);
if (existing.Length != 0)
    return CommandResult.Fail("A user with this email already exists.");

// Step 2 — Append: guard against a concurrent registration of the same email.
//   FailIfEventsMatch = the same query used in the read.
//   AfterSequencePosition = null → "fail if ANY matching event exists".
//   This closes the TOCTOU race: if another registration with the same email
//   landed between our Read and this Append, the condition fires.
await eventStore.AppendAsync(
    newEvent,
    condition: new AppendCondition { FailIfEventsMatch = uniquenessQuery });
```

### Why the Raw API Is an Intentional Design Choice Here

The `BuildDecisionModelAsync` family provides a higher-level abstraction for projections that
accumulate state through multiple events. For a pure uniqueness check the state is binary:
either a matching event exists or it does not, and the initial read result is the full answer.
There is nothing to fold.

Using the raw API for this case is not only valid — it is arguably more explicit:

- The query variable is defined once and used in **both** the read and the guard, making the
  consistency boundary visible at the call site.
- There is no intermediate projection type, no `IDecisionProjection<TState>` wrapper, and no
  ceremony that would obscure the simplicity of the operation.

**Comparison of the two approaches for this specific pattern:**

| | Raw `ReadAsync` + `AppendCondition` | `BuildDecisionModelAsync<bool>` |
|---|---|---|
| Lines of code | ~5 (query + read + check + condition + append) | ~10 (factory, projection, model, guard, append) |
| Ceremony | Minimal | Moderate |
| `AppendCondition` visibility | Explicit at call site | Encapsulated in helper |
| Retry support | Manual (`try/catch`) | Via `ExecuteDecisionAsync` |
| Best for | Pure existence check | Stateful folds over multiple events |

Both patterns enforce the same DCB guarantee. The sample deliberately keeps the raw API
approach to demonstrate that **Opossum does not force you to use the higher-level abstractions
— the underlying primitives are always available and equally correct**.

> Note: A `BuildDecisionModelAsync`-based alternative would look like:
> ```csharp
> // Factory method for the projection:
> public static IDecisionProjection<bool> EmailTaken(string email) =>
>     new DecisionProjection<bool>(
>         initialState: false,
>         query: Query.FromItems(new QueryItem
>         {
>             Tags = [new Tag("studentEmail", email)],
>             EventTypes = []
>         }),
>         apply: (_, _) => true);
>
> // In the handler:
> var model = await eventStore.BuildDecisionModelAsync(
>     StudentProjections.EmailTaken(command.Email));
>
> if (model.State)
>     return CommandResult.Fail("A user with this email already exists.");
>
> await eventStore.AppendAsync(newEvent, model.AppendCondition);
> ```
> This is equally correct. The sample keeps the raw form because both styles coexist
> in a real codebase and having one example of each is instructive.

---

## 4. The DCB Race Condition Guarantee

The critical safety property is the `AfterSequencePosition = null` in the `AppendCondition`.
This means:

> "Fail the append if **any** event matching this query exists in the store,
> regardless of when it was appended."

There is no watermark to compare against. The guard is absolute: if another writer registered
the same email between our read and our append, the `AppendConditionFailedException` fires.
The caller receives `HTTP 409 Conflict` (mapped in `Program.cs`).

Because the query is scoped to `studentEmail:{specific-value}`, registrations for
**different** emails never contend. There is no global "users aggregate" hot spot.

---

## 5. API Endpoints

`POST /students` — register a student with a unique email address

---

## 6. Test Coverage

| Layer | File | What it covers |
|---|---|---|
| Integration | `StudentRegistrationIntegrationTests.cs` | Happy path, duplicate email, conflict detection |

---

## 7. Summary

| DCB Concept | Opossum Primitive | Notes |
|---|---|---|
| Uniqueness guard | Tag-scoped `AppendCondition.FailIfEventsMatch` | `AfterSequencePosition = null` = absolute guard |
| Zero state to fold | Raw `ReadAsync` — existence check only | No projection type required |
| Tag-scoped boundary | `studentEmail:{email}` tag | Different emails never contend |
| Alternative higher-level form | `BuildDecisionModelAsync<bool>` with `(_, _) => true` | Both are valid; sample keeps raw form deliberately |
