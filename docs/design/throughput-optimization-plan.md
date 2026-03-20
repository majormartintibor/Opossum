# Throughput Optimization: Detailed Implementation Plan

> **Status:** Design
> **Version:** 0.6.0
> **Dependencies:** None (can be implemented independently of GDPR work)
> **Full research:** [docs/analysis/throughput-research-and-improvement-paths.md](../analysis/throughput-research-and-improvement-paths.md)
> **Measured baseline:** [docs/analysis/production-feasibility-analysis.md](../analysis/production-feasibility-analysis.md)

---

## Overview

Three optimizations that together improve durable write throughput from ~55/sec to
~200/sec (3.6x improvement). All three preserve the file-per-event model, maintain
cross-process safety, and require no external dependencies.

---

## Option E: Implicit Ledger (Eliminate the Second fsync)

### Problem

Every durable append performs **2 fsyncs**: one for the event file (~3.5ms) and one
for the ledger file (~2.5ms). The ledger fsync is redundant because the event file's
position is already encoded in its filename (`0000000042.json`).

### Design

1. Remove the `.ledger` file entirely
2. Track position in memory (`long _nextPosition`) initialized on startup
3. On startup: scan the `Events/` directory for the highest-numbered `.json` file
4. On append: increment the in-memory counter inside the cross-process lock
5. On crash: the next startup scan recovers the correct position from the filesystem

### Crash safety analysis

| Crash point | Recovery |
|-------------|----------|
| After temp file written, before fsync | Temp file cleaned up on startup; position not advanced |
| After fsync, before atomic move | Temp file cleaned up; position not advanced |
| After atomic move (event committed) | Startup scan finds the file; position recovered |
| After event committed, before in-memory update | Impossible (same thread, no I/O between) |

### Performance impact

- Saves ~2.5ms per event (one fewer fsync)
- Current: ~18ms/event -> Expected: ~15.5ms/event
- Throughput: ~55/sec -> ~65/sec (standalone), ~90-100/sec (combined with other opts)

### Breaking changes

- The `.ledger` file disappears from the store directory
- Migration: transparent. On first startup, position is derived from directory scan.
  No event data is affected.

### Implementation tasks

1. Add `ScanForHighestPositionAsync(string eventsDir)` to `EventFileManager`
2. Modify `FileSystemEventStore` constructor to call scan on init (lazy, on first use)
3. Remove `LedgerManager.UpdateSequencePositionAsync` calls from append path
4. Keep `LedgerManager.GetLastSequencePositionAsync` as fallback (read `.ledger` if
   scan fails, for backward compatibility)
5. Add temp file cleanup on startup (delete `*.tmp` files in Events directory)
6. Update `StorageInitializer` to not create `.ledger` for new stores
7. Update `IEventStoreAdmin.DeleteStoreAsync` to handle missing `.ledger`
8. Unit tests: startup scan, crash recovery simulation, backward compatibility
9. Integration tests: append + restart + verify position continuity
10. Update `docs/implementation/durability-guarantees.md`

### Estimated effort: Low-Medium (2-3 sessions)

---

## Option A: Append-Only Index Files

### Problem

Every append reads the full index file (JSON array of positions), deserializes it,
appends one entry, re-serializes, writes a temp file, and renames. With 2 tags, this
is **4 read-modify-write cycles** per event, consuming ~62% of the no-flush cost.

### Design

Replace JSON array index files with append-only newline-delimited position lists:

**Current format** (`EventType/StudentRegisteredEvent.json`):
```json
[1, 5, 12, 47, 103]
```

**New format** (`EventType/StudentRegisteredEvent.idx`):
```
1
5
12
47
103
```

**Write** = `File.AppendAllTextAsync($"{position}\n")` --- one syscall, no read, no
temp file, no rename.

**Read** = `File.ReadAllLinesAsync(...)` --- parse longs, sort, deduplicate on load.

### Why this is safe

- The cross-process lock guarantees only one appender is active at a time
- Concurrent readers use `FileShare.Read` which is compatible with an append handle
- Duplicates cannot occur because append is serialized
- If a crash occurs mid-append, the next read will see either the complete line or a
  truncated line. Truncated lines are detected by `long.TryParse` and skipped.

### Index file changes

| Current | New | Change |
|---------|-----|--------|
| `Indices/EventType/{eventType}.json` | `Indices/EventType/{eventType}.idx` | Format change |
| `Indices/Tags/{tagKey}={tagValue}.json` | `Indices/Tags/{tagKey}={tagValue}.idx` | Format change |

### Performance impact

- Eliminates 2 reads + 2 temp-write-rename cycles per event (for 2 tags)
- From ~28 file I/O calls/event to ~18 file I/O calls/event
- Expected throughput (no-flush): ~300-350 events/sec (~1.5x improvement)
- Expected throughput (flush): ~105-110 events/sec (~1.2x improvement)

### Breaking changes

- Index file format changes from `.json` to `.idx`
- **Migration strategy:** On startup, if `.json` index files exist but no `.idx` files,
  automatically convert them (read JSON array, write newline-delimited file, delete old).
  One-time operation.

### Implementation tasks

1. Modify `PositionIndexFile.WritePositionsAsync` -> `AppendPositionAsync(string path, long position)`
2. Modify `PositionIndexFile.ReadPositionsAsync` to parse newline-delimited format with
   `long.TryParse` (skip truncated lines)
3. Add `PositionIndexFile.MigrateFromJsonAsync` for backward compatibility
4. Update `EventTypeIndex.AddPositionAsync` to call append instead of read-modify-write
5. Update `TagIndex.AddPositionAsync` similarly
6. Update `StorageInitializer` to create `.idx` files
7. Add migration check on startup (detect `.json` files, convert)
8. Update `IndexManager` query methods to read `.idx` format
9. Unit tests: append, read, truncated line handling, migration
10. Integration tests: append + query consistency, migration from old format
11. Update `docs/implementation/durability-guarantees.md` to describe new index format

### Estimated effort: Low (2 sessions)

---

## Option B: In-Memory Index Cache

### Problem

Even with append-only index files (Option A), every query still reads index files from
disk. At large store sizes (10K+ events), index files grow and read times increase
linearly.

### Design

Load all tag and event-type indices into memory at startup. Serve all read queries
from memory. On append, update both the in-memory cache and the on-disk file.

### Cache structure

```csharp
internal sealed class IndexCache
{
    // EventType -> sorted list of positions
    private readonly ConcurrentDictionary<string, SortedSet<long>> _eventTypeIndex = new();

    // "tagKey=tagValue" -> sorted list of positions
    private readonly ConcurrentDictionary<string, SortedSet<long>> _tagIndex = new();
}
```

### Cache invalidation (cross-process)

When the cross-process lock is acquired:
1. Compare each index file's `LastWriteTimeUtc` against the cache's timestamp
2. If another process wrote while this process was not holding the lock, reload
   the changed index files
3. This makes the cache valid for the full duration of the locked append

For read operations (no lock held):
- Reads from the in-memory cache are always consistent within a single process
- If another process appended events, the cache will be stale until the next append
  (which acquires the lock and refreshes)
- This is acceptable: read-after-write consistency is guaranteed within a process;
  cross-process reads are eventually consistent (next lock acquisition)

### Performance impact

- Eliminates all index file I/O from the read path
- Eliminates index file reads from the write path (only appends remain)
- Expected throughput (no-flush): ~400-500 events/sec (combined with A)
- Expected throughput (flush): ~125-130 events/sec (combined with A)

### Breaking changes

None. This is a purely internal optimization.

### Implementation tasks

1. Create `IndexCache` class with thread-safe collections
2. Add `LoadFromDiskAsync(string indicesDir)` to populate on startup
3. Modify `EventTypeIndex` to check cache first, fall back to disk
4. Modify `TagIndex` to check cache first, fall back to disk
5. Modify `IndexManager.AddEventToIndicesAsync` to update cache after disk write
6. Add cache refresh on cross-process lock acquisition (timestamp comparison)
7. Unit tests: cache hit, cache miss, cache invalidation, concurrent access
8. Integration tests: cross-process cache coherency
9. Benchmark: measure before/after for read path and write path

### Estimated effort: Medium (3-4 sessions)

### Dependency

Option A should be implemented first. Option B can work with the current JSON format
but is more effective when combined with A (fewer disk reads to refresh the cache).

---

## Combined Performance Expectation (Measured Basis)

All figures based on micro-benchmark measurements from the feasibility analysis.

| Configuration | Current (0.5.0) | After E only | After A+E | After A+B+E |
|---|---|---|---|---|
| No-flush (local) | ~185/sec | ~280/sec | ~350/sec | ~550-650/sec |
| Flush=true (local) | ~55/sec | ~90/sec | ~130/sec | **~180-220/sec** |
| Flush=true (SMB LAN) | ~40-50/sec | ~70/sec | ~100/sec | ~120-140/sec |

### Hard ceiling (file-per-event + 1 fsync)

With A+B+E fully implemented, the single remaining fsync (~3.5ms on this SSD) sets an
absolute ceiling of **~285 events/sec** even with zero other I/O overhead. The measured
Scenario D (file-per-event + in-memory indices + durable) achieved **218/sec**,
confirming this ceiling.

---

## Implementation Order

```
Option E (implicit ledger)  ──┐
                               ├──> Option A (append-only indices) ──> Option B (in-memory cache)
                               │
                               └──> Can run in parallel with E
```

**Recommended sequence:**
1. **Option E first** --- smallest change, biggest per-event savings on flush path
2. **Option A second** --- low effort, big savings on no-flush path, unblocks B
3. **Option B third** --- medium effort, completes the optimization story

Each option is independently shippable and testable.
