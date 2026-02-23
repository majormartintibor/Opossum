# ADR-003: Decision Not to Implement `IAsyncEnumerable<SequencedEvent>` on `IEventStore.ReadAsync`

**Date:** 2026-03-01
**Status:** ❌ Closed — Will Not Implement (may be re-evaluated if deployment profile changes)

---

## Context

Item 2 of the 0.3.0-preview.1 roadmap proposed changing `IEventStore.ReadAsync` from
returning `Task<SequencedEvent[]>` to `IAsyncEnumerable<SequencedEvent>`, motivated by:

- **Memory pressure** on large reads — 134,000 events × ~7 KB/event ≈ 938 MB heap during a full rebuild
- **Latency to first event** — projection rebuilds cannot start processing until all events are deserialised
- **DCB specification language** — the spec SHOULD clause recommends "some form of iterable or reactive stream"

The proposal seemed straightforward on paper. When evaluated against the actual codebase and
deployment profile, the trade-offs did not support the change.

---

## What the Roadmap Got Wrong

The roadmap's implementation note assumed a sequential accumulator was the bottleneck:

> `EventFileManager.ReadEventsAsync` reads each event file in a loop and collects results
> into a `List<SequencedEvent>` before converting to an array.

**This was already fixed.** The actual production implementation uses `Parallel.ForEachAsync`
for batches of 10 or more events:

```csharp
// EventFileManager.ReadEventsAsync — current implementation
await Parallel.ForEachAsync(
    Enumerable.Range(0, positions.Length),
    new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 },
    async (i, ct) =>
    {
        parallelEvents[i] = await ReadEventAsync(eventsPath, positions[i]).ConfigureAwait(false);
    }).ConfigureAwait(false);
```

The comment in that code is explicit: *"2x CPU count for I/O-bound work to keep SSD saturated —
benchmark results show 2x is optimal."* This parallel I/O strategy is the primary driver of
the real-world improvement from **3 hours to 18 minutes** when rebuilding 52,000 projections
from 134,000 events.

---

## The Core Trade-off

A naive `IAsyncEnumerable` implementation replaces `Parallel.ForEachAsync` with sequential reads:

```csharp
// Naive streaming — loses all parallel I/O benefit
foreach (var position in positions)
    yield return await ReadEventAsync(eventsPath, position).ConfigureAwait(false);
```

For the known real-world workload (134,000 events, 18-minute rebuild), this regression would
likely take rebuild time from 18 minutes back toward the original 3 hours.

### Can the parallelism be preserved in a streaming model?

Yes, via a `Channel<T>`-based prefetcher that reads ahead in parallel while the caller
processes the current event. But this approach has significant problems:

1. **Ordering.** `Parallel.ForEachAsync` completes tasks in arbitrary order. Projection
   rebuilds require strictly ascending position order. A parallel prefetcher produces
   out-of-order events that must be buffered and reordered before yielding — adding a
   sorting/reordering stage that grows with parallelism degree and event payload size.

2. **Complexity.** A correct, backpressure-aware, ordered parallel prefetcher is
   non-trivial to implement and test. It requires bounded channels, reorder buffers,
   and careful cancellation handling. This complexity is disproportionate to the benefit
   for Opossum's target deployment scale.

3. **It's still a breaking API change.** Every caller — `DecisionModelExtensions`,
   `ProjectionManager`, `ProjectionDaemon`, all tests — would need to be migrated.

---

## How Other .NET Event Sourcing Frameworks Approach This

| Framework | Read return type | Why |
|-----------|-----------------|-----|
| **EventStoreDB client** | `IAsyncEnumerable<ResolvedEvent>` | Data arrives over gRPC — inherently a network stream; no array to pre-build |
| **Marten** (PostgreSQL) | `IReadOnlyList<IEvent>` | PostgreSQL result sets are materialised; streaming exists on the query side, not event reads |
| **NEventStore** | `IEnumerable<ICommit>` | Synchronous, materialised; backed by SQL/MongoDB |
| **SqlStreamStore** | Page-based: array per page + cursor | Explicitly paginated; callers request the next page |

The pattern is clear: frameworks that use `IAsyncEnumerable` do so because their data
source is an inherently incremental network stream (gRPC, database cursor over a socket).
For Opossum, data is on the **local file system** and the parallel I/O model is the right
fit for that transport.

---

## Opossum's Deployment Profile Does Not Require Streaming

The README is explicit about target use cases:

> Perfect for scenarios where simplicity, offline operation, and local data sovereignty
> matter more than cloud scalability.
>
> ✅ Single server/small deployment (< 100k events/day)
> ✅ Offline-first requirements
> ✅ POS systems, field service, SMB ERP

In this profile:

- **Full projection rebuilds are rare** — they happen at startup after a crash, during
  schema migrations, or during development. They are not on the hot path.
- **Incremental updates are the hot path** — 10–11 µs/event, already excellent.
- **938 MB during a rare cold rebuild is acceptable** on any modern POS machine or
  SMB server (typically 8–32 GB RAM).
- **There is no backpressure producer** — data is already on the local disk, not
  arriving over a network where flow control matters.

---

## What to Do If Memory Does Become a Concern

If a future deployment profile genuinely requires bounded memory during projection rebuilds
(e.g. very large stores on constrained hardware), the correct fix is **chunked reading
inside `ProjectionManager.RebuildAsync`**, not a change to `IEventStore`:

```csharp
// ProjectionManager.RebuildAsync — chunked approach (not yet implemented)
long checkpoint = 0;
do {
    var chunk = await _eventStore.ReadAsync(Query.All(), null, fromPosition: checkpoint);
    if (chunk.Length == 0) break;
    foreach (var evt in chunk)
        await registration.ApplyAsync(evt, ct);
    checkpoint = chunk[^1].Position;
} while (chunk.Length == _projectionOptions.RebuildChunkSize);
```

This gives bounded memory with:
- Zero public API change on `IEventStore`
- Full preservation of `Parallel.ForEachAsync` within each chunk
- A configurable `RebuildChunkSize` on `ProjectionOptions`

This approach should be revisited as a backlog item if real-world memory pressure is
reported by users.

---

## Decision

**`IEventStore.ReadAsync` keeps the `Task<SequencedEvent[]>` return type.**

Reasons:

1. The parallel I/O optimisation that drives the 3h → 18min rebuild improvement is
   incompatible with a naive sequential streaming model.
2. A correct parallel-streaming implementation (ordered `Channel<T>` prefetcher) is
   disproportionately complex for Opossum's deployment scale.
3. All other .NET event sourcing frameworks that use `IAsyncEnumerable` do so because
   their transport is an inherently streaming network source — not applicable here.
4. Opossum's target users (POS, SMB, offline-first) do not encounter the memory
   scenarios that motivated the original proposal.
5. If memory pressure does appear, chunked reads inside `ProjectionManager` solve it
   without any public API change.

**This decision is closed and is not on any future roadmap.** It may be re-opened only
if a concrete real-world memory constraint is reported against a deployment that cannot
be addressed by chunked rebuilds.
