# Analysis: "Invoice Number" DCB Example in Opossum

**Source:** https://dcb.events/examples/invoice-number/  
**Status:** ✅ Implemented — `Opossum.Samples.CourseManagement`  
**Scope:** Maps the DCB "Invoice Number" example to its Opossum implementation and documents
the `ReadLastAsync` primitive that makes the pattern efficient.

---

## 1. What the DCB Example Demonstrates

The "Invoice Number" example demonstrates how to generate a **consecutive, gap-free,
monotonically increasing sequence** using event sourcing — a requirement that appears
deceptively simple but breaks under concurrent writes if handled naïvely.

### Business Rules

1. Every invoice must have a unique integer number.
2. Invoice numbers must form an **unbroken sequence** — no gaps (1, 2, 3 … not 1, 3, 7 …).
3. The sequence must be correct even under concurrent invoice creation.

### Why This Is Hard Without DCB

Naïve approaches fail under concurrency:

- **Read max + increment**: Two concurrent writers both read the same max (`42`), both
  compute `43`, both append — resulting in two invoices with number `43`.
- **Aggregate on a "last invoice" stream**: Works, but forces all invoice creation globally
  through a single stream — a serialisation bottleneck.
- **Database sequence / auto-increment**: Couples the solution to SQL infrastructure.

DCB solves this with an optimistic concurrency guard scoped to the **exact same query** used
to find the current maximum. If another invoice was created between the read and the append,
the guard fires and the operation retries with the updated maximum.

---

## 2. Domain Adaptation in `Opossum.Samples.CourseManagement`

The Opossum sample implements the pattern directly without domain name changes — invoices are
a natural concept in a course management system (billing students for course access).

| DCB Example | Opossum Sample |
|---|---|
| Invoice number sequence | Invoice number sequence |
| Generic invoice event | `InvoiceCreatedEvent(InvoiceNumber, CustomerId, Amount, IssuedAt)` |
| No tag filter (global) | No tag filter — `Query.FromEventTypes(nameof(InvoiceCreatedEvent))` |

### The Global Consistency Boundary

There is **no tag filter** on the invoice query. This is intentional: the sequence must be
gap-free _globally_, so the consistency boundary is "all invoice creation events ever". A
concurrent invoice created by any user, for any customer, at any time invalidates the
"last number" read.

This demonstrates that DCB consistency boundaries do not have to be entity-scoped — they
can span the entire event type when the business rule demands it.

---

## 3. The Key Opossum Primitive: `ReadLastAsync`

Standard `ReadAsync` would load all invoice events to find the current maximum. For a store
with many invoices this is wasteful when you only need the latest number.

Opossum provides `ReadLastAsync` specifically for this pattern:

```csharp
// Returns the single most-recent matching event, or null if none exists.
// O(1) file reads — reads only the last position from the index.
var last = await eventStore.ReadLastAsync(_invoiceQuery, cancellationToken);
```

This maps directly to the DCB spec's guidance to read only the last relevant event when
building a consecutive-sequence decision model.

---

## 4. Full Implementation

```csharp
// Query has NO tag filter — spans all InvoiceCreatedEvents globally.
private static readonly Query _invoiceQuery =
    Query.FromEventTypes(nameof(InvoiceCreatedEvent));

private static async Task<CommandResult<int>> TryCreateInvoiceAsync(
    CreateInvoiceCommand command,
    IEventStore eventStore,
    CancellationToken cancellationToken)
{
    // Step 1 — Read: find the most recently created invoice (O(1) file reads).
    // Returns null when the store contains no invoices yet.
    var last = await eventStore.ReadLastAsync(_invoiceQuery, cancellationToken);

    // Step 2 — Decide: next number is last + 1, or 1 for the very first invoice.
    var nextNumber = last is null
        ? 1
        : ((InvoiceCreatedEvent)last.Event.Event).InvoiceNumber + 1;

    // Step 3 — Append with a guard.
    //   AfterSequencePosition = last?.Position
    //     → if last is null: "fail if ANY invoice already exists" (bootstrap race)
    //     → if last is not null: "fail if any invoice appeared after position X"
    var condition = new AppendCondition
    {
        FailIfEventsMatch = _invoiceQuery,
        AfterSequencePosition = last?.Position
    };

    await eventStore.AppendAsync(
        new InvoiceCreatedEvent(nextNumber, command.CustomerId, command.Amount, DateTimeOffset.UtcNow)
            .ToDomainEvent()
            .WithTag("invoiceNumber", nextNumber.ToString())
            .WithTag("customerId", command.CustomerId.ToString())
            .WithTimestamp(DateTimeOffset.UtcNow),
        condition,
        cancellationToken);

    return CommandResult<int>.Ok(nextNumber);
}
```

The entire decision cycle is wrapped in `ExecuteDecisionAsync` for automatic retry with
exponential back-off on `AppendConditionFailedException`.

---

## 5. The Bootstrap Race

When `last` is `null` (no invoices exist yet), `AfterSequencePosition` is `null`. This means:

> "Fail the append if **any** `InvoiceCreatedEvent` exists in the store."

Two concurrent first-invoice requests both read `null`, both compute number `1`. The second
`AppendAsync` call finds that an `InvoiceCreatedEvent` now exists (appended by the first
caller) and throws `AppendConditionFailedException`. The retry reads number `1` and correctly
appends number `2`. The sequence is preserved.

---

## 6. Mapping DCB Concepts to Opossum Primitives

| DCB Concept | Opossum Primitive | Notes |
|---|---|---|
| Read last event in sequence | `IEventStore.ReadLastAsync` | O(1) — reads only the index tail |
| Global consistency boundary | `Query.FromEventTypes(...)` — no tag filter | All invoice events in scope |
| Consecutive number derivation | `lastNumber + 1` or `1` if null | Standard arithmetic in handler |
| Bootstrap race guard | `AfterSequencePosition = null` | "fail if any exists" |
| Conflict detection | `AppendConditionFailedException` | Retry the full cycle |
| Retry | `ExecuteDecisionAsync` | Exponential back-off |

---

## 7. Read-Side Projection

A companion `InvoiceProjection` (`IProjectionDefinition<InvoiceReadModel>`) materialises
the invoice list as a persistent read model keyed by invoice number. This is entirely
separate from the write-side sequence guarantee — the read model is rebuilt from events
and plays no role in enforcing the consecutive numbering invariant.

---

## 8. API Endpoints

`POST /invoices` — create an invoice with the next consecutive number  
`GET /invoices` — list all invoices (from the `InvoiceProjection` read model)

---

## 9. Test Coverage

| Layer | File | What it covers |
|---|---|---|
| Integration | `InvoiceIntegrationTests.cs` | Consecutive sequence, concurrent creation, first invoice |

---

## 10. Summary

| Requirement | How Opossum Satisfies It |
|---|---|
| Gap-free sequence | `ReadLastAsync` + increment — no gaps possible with the guard active |
| Concurrent-write safety | `AppendCondition.FailIfEventsMatch` + `AfterSequencePosition` |
| Bootstrap race | `AfterSequencePosition = null` = absolute guard on first invoice |
| Efficient read | `ReadLastAsync` — O(1), not a full scan |
| No infrastructure dependency | Pure file-system event store — no SQL sequence, no Redis |
