# What is an Event Store?

## The Core Idea

Traditional applications store **current state** — a `Students` table holds one row per student, updated in place when something changes. You can see where things are now, but not how they got there.

An **event store** flips this: it stores **what happened** as an immutable, append-only sequence of facts. The current state is always derivable by replaying events from the beginning. Nothing is ever updated or deleted; the history is the source of truth.

```
Traditional DB:         Event Store:
┌─────────────────┐     ┌──────────────────────────────────────┐
│ students         │     │ position 1 │ StudentRegisteredEvent  │
│ id | name | ...  │     │ position 2 │ StudentEnrolledEvent    │
│ 42 | Alice | ... │     │ position 3 │ CourseCreatedEvent      │
└─────────────────┘     │ position 4 │ StudentEnrolledEvent    │
                         └──────────────────────────────────────┘
```

---

## How Opossum Stores Events

Opossum stores events as **individual JSON files** in a structured directory hierarchy — one file per event, named by their monotonically increasing sequence position:

```
EventStore\
  MyApp\
    .ledger                            ← tracks last committed position
    Events\
      0000000001.json                  ← {"eventType":"StudentRegisteredEvent", "data":{...}, "tags":[...]}
      0000000002.json
      0000000003.json
    Indices\
      EventType\
        StudentRegisteredEvent.idx     ← list of positions
        CourseCreatedEvent.idx
      Tags\
        studentId_abc123.idx           ← positions for tag "studentId:abc123"
    Projections\
      StudentView\
        abc123.json                    ← current state for key "abc123"
```

### Why files?

| Property | Benefit |
|---|---|
| **Plain JSON** | Human-readable, inspectable with any text editor |
| **No database server** | Zero infrastructure to install, configure, or maintain |
| **Offline-first** | Works without any network connectivity |
| **Backup = copy** | A simple `xcopy` or `rsync` is a complete backup |
| **Audit-friendly** | OS timestamps and optional read-only protection |

---

## The Three Core Operations

### 1. Append

Add one or more events atomically. An optional `AppendCondition` enforces optimistic concurrency (the DCB pattern).

The recommended way to create events is the **fluent builder** — the `DomainEventBuilder` implicitly converts to `NewEvent`:

```csharp
using Opossum.Extensions;

// Fluent builder — implicit conversion to NewEvent
NewEvent evt = new StudentRegisteredEvent(studentId, "Alice", "Smith", "alice@example.com")
    .ToDomainEvent()
    .WithTag("studentId", studentId.ToString())
    .WithTag("studentEmail", "alice@example.com")
    .WithTimestamp(DateTimeOffset.UtcNow);

// Single-event convenience extension
await eventStore.AppendAsync(evt, condition: null);

// Multi-event append via the core interface
await eventStore.AppendAsync([evt1, evt2], condition: null);
```

Each appended batch is assigned a globally unique, monotonically increasing **sequence position**.

### 2. Read

Query events by event type, tags, or both:

```csharp
using Opossum.Core;

// All events
var all = await eventStore.ReadAsync(Query.All(), readOptions: null);

// Events of a specific type
var typed = await eventStore.ReadAsync(
    Query.FromEventTypes(nameof(StudentRegisteredEvent)), readOptions: null);

// Events matching a tag
var tagged = await eventStore.ReadAsync(
    Query.FromItems(new QueryItem { Tags = [new Tag("studentId", id)] }),
    readOptions: null);

// Convenience extension — single ReadOption instead of array
var descending = await eventStore.ReadAsync(
    Query.All(), ReadOption.Descending);
```

### 3. ReadLast

Efficiently read only the most recent event matching a query — key for DCB consecutive-sequence patterns like invoice numbering:

```csharp
var last = await eventStore.ReadLastAsync(
    Query.FromEventTypes(nameof(InvoiceCreatedEvent)));
// last?.Position is the AfterSequencePosition for your AppendCondition
```

---

## Sequence Positions

Every committed event has a **position** — a 1-based, globally ordered integer within its store:

- Positions are **unique** — no two events share the same position.
- Positions are **monotonically increasing** — a newer event always has a higher position than any older event.
- Positions **may have gaps** — e.g., after a failed append attempt that was partially written.

The position is the key concept enabling DCB's optimistic concurrency: you read up to position N, build your decision model, then append with a guard that says "fail if any new events matching this query appeared after position N".

---

## Tags — Domain-Scoped Indexing

Tags are key/value pairs attached to events at append time. They are stored in a separate index that allows fast retrieval without scanning all events:

```csharp
// Tags are added via the fluent builder when creating events
NewEvent evt = new StudentEnrolledToCourseEvent(courseId, studentId)
    .ToDomainEvent()
    .WithTag("studentId", studentId.ToString())
    .WithTag("courseId", courseId.ToString())
    .WithTimestamp(DateTimeOffset.UtcNow);
```

A query using tags touches only the indexed positions for those tags — not the full event log.

> Tags are immutable once committed. Use `IEventStoreMaintenance.AddTagsAsync` to retroactively add tags to existing events.

---

## The Append Condition — DCB Concurrency Control

The `AppendCondition` is how Opossum implements DCB's consistency guarantee. It has two parts:

- **`FailIfEventsMatch`** — a query describing what events would invalidate your decision.
- **`AfterSequencePosition`** — the highest position you were aware of when you made your decision.

The event store checks atomically: "are there any events matching `FailIfEventsMatch` with position > `AfterSequencePosition`?" If yes, it throws `AppendConditionFailedException`. The caller retries the full read → decide → append cycle.

This replaces aggregate-level locking with a **query-scoped lock** that spans exactly the events relevant to one business decision — nothing more.

---

## Durability

By default, Opossum calls `FileStream.Flush(flushToDisk: true)` after every append. This guarantees that events survive a power failure — they are physically on disk before `AppendAsync` returns.

For testing or development, set `FlushEventsImmediately = false` for a 2–3× throughput increase. See [Durability Guarantees](../guides/durability.md) for details.

---

## Next Steps

→ [DCB Specification](dcb.md) — the full spec Opossum implements  
→ [Projections](projections.md) — building read models from events  
→ [API Reference](../../api/Opossum.yml) — `IEventStore`, `AppendCondition`, `Query`, and more
