# Lesson Learned: CourseBookOrderHistory — Projection Key Anti-Pattern

## Date: 2025 (discovered during Large dataset testing)
## Status: Projection Removed
## Decision: Complete Removal

---

## 📋 Executive Summary

**Feature:** `CourseBookOrderHistoryProjection` — a persisted read model intended to expose a
global log of all course-book purchase orders.

**Expected behaviour:** One projection entry per order, queryable by student ID with pagination.

**Actual behaviour:** One projection **file** per purchase _event_, producing an unbounded number
of files proportional to total event count — not entity count.

**Decision:** **Complete removal.** The concept was not required by the DCB specification, not
required to demonstrate any DCB pattern, and was already covered by the better-designed
`StudentPurchasedBooksProjection`.

---

## 🎯 The Mistake

### Root Cause — Key Selector Tied to Event Position

```csharp
// ❌ WRONG — creates one projection file per event
public string KeySelector(SequencedEvent evt) => evt.Position.ToString();
```

Using `evt.Position` as the projection key means **cardinality = total number of matching
events**, not entities. There is no upper bound: every `CourseBookPurchasedEvent` and every
`CourseBooksOrderedEvent` gets its own file on disk.

### Scale Impact (with default `SingleBookPurchasesPerBook = 20`)

| Preset | Purchase events | Multi-book orders | Projection files created |
|--------|----------------|-------------------|--------------------------|
| Small  | 25 × 20 = 500  | 8                 | ~508                     |
| Medium | 4,000 × 20 = 80,000 | 600          | ~80,600                  |
| Large  | 40,000 × 20 = 800,000 | 7,000      | ~807,000                 |
| Prod   | 200,000 × 20 = 4,000,000 | 35,000  | ~4,035,000               |

On a freshly-seeded Large database the rebuild was stopped manually after **700,000+
projection files** had been created — and the rebuild was still running.

### Secondary Flaw — Unbounded `GetAllAsync` When No Filter Is Applied

```csharp
// ❌ WRONG — loads every projection file into memory
filtered = await projectionStore.GetAllAsync();
```

The unfiltered query path loaded the entire projection set into memory. On Large this would
have attempted to deserialise ~807,000 JSON files in a single call.

---

## 🔑 The Rule — Projection Key Cardinality Must Be Bounded by Entity Count

A persisted projection's `KeySelector` must produce a key that belongs to a **finite,
entity-scoped domain**. Acceptable keys:

| Key domain | Example | Upper bound |
|------------|---------|-------------|
| Student ID | `studentId` | Number of students |
| Course ID  | `courseId`  | Number of courses  |
| Invoice number | `invoiceNumber` | Number of invoices |
| Composite entity key | `$"{courseId}-{studentId}"` | Enrolment count |

**Never acceptable keys:**

| Key domain | Why it is wrong |
|------------|-----------------|
| Event position | O(Events) — unbounded, grows forever |
| `Guid.NewGuid()` | Each call creates a new unique key — infinite cardinality |
| Timestamp (raw) | Not unique; still O(Events) in the worst case |

---

## ✅ The Correct Design — Per-Student Aggregation

The `StudentPurchasedBooksProjection` that already existed in the sample demonstrates the
correct pattern:

```csharp
// ✅ CORRECT — one projection file per student
public string KeySelector(SequencedEvent evt)
{
    var tag = evt.Event.Tags.FirstOrDefault(t => t.Key == "studentId")
        ?? throw new InvalidOperationException(…);
    return tag.Value;
}
```

- Key domain: student ID → O(Students)
- State accumulates all of a student's purchases in a single document
- Tag index allows efficient per-student lookup without `GetAllAsync`

If a "global order log" read model is genuinely needed in the future, the correct
implementation is either:

1. **Key by student ID** — one file per student, state = `List<OrderEntry>`. O(Students).
2. **On-demand event-store query** — skip the persisted projection entirely; query
   `CourseBookPurchasedEvent` / `CourseBooksOrderedEvent` directly from the event store
   with appropriate tags and pagination.

---

## 📝 What Was Removed

| Artifact | Reason |
|----------|--------|
| `CourseBookOrderHistory/CourseBookOrderHistoryProjection.cs` | Broken key selector (position-keyed) |
| `CourseBookOrderHistory/CourseBookOrderHistoryTagProvider.cs` | Only served the removed projection |
| `CourseBookOrderHistory/GetCourseBookOrderHistory.cs` | Query handler + endpoint for removed projection |
| `Shared/SortOrder.cs` → `CourseBookOrderSortField` enum | Only used by the removed query |
| `Program.cs` → `MapGetCourseBookOrderHistoryEndpoint()` | Endpoint registration for removed feature |

---

## 🔍 How It Was Caught

Running the sample application against the **Large** seeded database (40,000 books ×
20 purchases/book = 800,000 purchase events) revealed projection rebuilding that never
completed. Stopping the process and inspecting the projection directory showed 700,000+
individual JSON files under `CourseBookOrderHistory/`.

The same problem is visible at smaller scale: a **Small** database (25 books × 20
purchases) produces ~508 projection files — far exceeding the number of students (40) or
courses (25) in that dataset, which is the first signal that cardinality is wrong.

---

## 💡 Checklist Before Adding a New Persisted Projection

- [ ] What is the key selector? Is its domain bounded by an entity count?
- [ ] What is the worst-case number of projection files (largest preset)?
- [ ] Does any query path call `GetAllAsync()`? If so, is the total file count acceptable?
- [ ] Is this projection functionally distinct from existing projections?
- [ ] Is this projection required to demonstrate a specific DCB pattern?
