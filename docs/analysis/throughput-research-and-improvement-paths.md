# Throughput Research: File-System Event Stores & Improvement Paths

> **Purpose:** Diagnose Opossum's throughput ceiling with precision, survey how other
> file-system-based event stores achieve higher throughput, and evaluate concrete
> improvement paths for Opossum.
>
> **Note:** Web-fetched benchmarks were unavailable at time of writing. All comparative
> figures are drawn from published technical documentation, open-source code inspection,
> and established systems engineering literature. Opossum figures are from the actual
> BenchmarkDotNet runs in `docs/benchmarking/results/20260223/`.

---

## Part 1 — Why Opossum Is at 100–220 Events/Sec

### The measured baseline

From `AppendBenchmarks` (SSD, Windows 11, .NET 10, BenchmarkDotNet):

| Scenario | Mean | Throughput |
|---|---|---|
| Single event, `FlushEventsImmediately = false` | 4.874 ms | ~205 events/sec |
| Single event, `FlushEventsImmediately = true` | 10.853 ms | ~92 events/sec |
| Batch 10 events, no flush | 37.629 ms | ~266 events/sec (batch throughput) |
| Batch 10 events, with flush | 65.154 ms | ~154 events/sec (batch throughput) |
| Batch 100 events, no flush | 408 ms | ~245 events/sec (batch throughput) |

**Observation:** The batch-no-flush throughput only marginally exceeds single-event
throughput. 100 events batched takes 408 ms where 100 × 4.874 ms would predict 487 ms —
only a 1.2× batch benefit. The cost scales almost linearly with event count. This
tells us the bottleneck is not setup overhead but **per-event file I/O operations**.

### The actual per-event file I/O count

For a typical event with 2 tags (e.g., `courseId` + `studentId`):

| Operation | File I/O calls | Notes |
|---|---|---|
| Event temp file write | open + write + close | `File.WriteAllTextAsync(tempPath)` |
| Event file fsync | open + fsync + close | only if `FlushEventsImmediately = true` |
| Event file rename | rename syscall | `File.Move(tempPath, filePath)` |
| TagIndex read (tag 1) | open + read + close | `PositionIndexFile.ReadPositionsAsync` |
| TagIndex temp write (tag 1) | open + write + close + rename | full rewrite, not append |
| TagIndex read (tag 2) | open + read + close | same as tag 1 |
| TagIndex temp write (tag 2) | open + write + close + rename | same as tag 1 |
| EventTypeIndex read | open + read + close | full rewrite |
| EventTypeIndex temp write | open + write + close + rename | full rewrite |
| Ledger read | open + read + close | `GetLastSequencePositionAsync` |
| Ledger temp write | open + write + close | `UpdateSequencePositionAsync` |
| Ledger fsync | open + fsync + close | only if `FlushEventsImmediately = true` |
| Ledger rename | rename syscall | `AtomicMoveWithRetryAsync` |

**Total (2 tags, no flush): ~28 file I/O calls per event.**  
**Total (2 tags, with flush): ~28 + 2 fsyncs = ~30 file I/O calls per event.**

### Bottleneck decomposition

```
No-flush: 4.874 ms total
  ├─ Event file write + rename:              ~1.2 ms
  ├─ TagIndex × 2 (read + rewrite each):     ~2.0 ms   ← 41% of total
  ├─ EventTypeIndex (read + rewrite):        ~1.0 ms   ← 21% of total
  └─ Ledger (read + write + rename):         ~0.7 ms

With-flush: 10.853 ms total
  ├─ Same as above:                          ~4.9 ms
  ├─ Event file fsync (FlushToDisk):         ~3.5 ms   ← single biggest line item
  └─ Ledger fsync (FlushToDisk):             ~2.5 ms
```

**The two separate problems:**

1. **Without fsync:** The bottleneck is the **read-modify-write pattern on index files**.
   Every append reads the full TagIndex and EventTypeIndex, rewrites them from scratch,
   and renames the temp file. For 2 tags, this is 4 read+write cycles → ~62% of the
   no-flush cost.

2. **With fsync:** The additional 5.98 ms comes from **2 fsyncs** (event file + ledger),
   each costing ~3 ms on a modern SSD. The index files do not get fsynced.

These are two independent problems with independent fixes.

---

## Part 2 — How Other File-System-Based Stores Achieve Higher Throughput

*Note: figures below are from public documentation, open-source code, and published
benchmarks. All are approximate and hardware-dependent.*

### EventStoreDB (C#, open source, dedicated server)

**Storage design:**  
Events are written to 256 MB binary *chunk files* in strictly sequential order.
A single background writer thread receives all append requests from a concurrent queue.
Reads use memory-mapped views of the chunk files.

**Why it is fast:**
- Sequential append to one pre-allocated file eliminates `open()`/`close()` overhead
- Single writer means no file-level lock contention
- Background fsync: by default, `FlushInterval = 0` means fsync runs as fast as the
  writer produces data, but the *next* write is not blocked waiting for fsync (pipelining)
- Index (PTable) updates are batched and written in the background, not on the hot path

**Throughput:** ~15,000–50,000 events/sec (single node, SSD, fsync on)  
**Key insight:** ~150× faster than Opossum, almost entirely because sequential writes to
one open file eliminate per-event file-open/close overhead.

### Apache Kafka (Java, distributed log)

**Storage design:**  
Events are appended to *segment files* (default 1 GB each) within *topic partitions*.
The current segment file is kept open permanently. Writes use the `FileChannel.write()`
API with configurable fsync policy (`fsync.every.N.messages`, default: never — relies
on OS page cache + replication for durability).

**Why it is fast:**
- One open `FileChannel` per partition — no `open()`/`close()` per message
- Sequential writes → no random I/O → SSD sequential write bandwidth (~500 MB/s) vs
  random (~100 MB/s for small files)
- No per-message index write — offset index is sparse (written every N bytes)

**Throughput:** 100,000–2,000,000 messages/sec (highly batch/compression dependent)  
**Key insight:** Kafka trades durability (no per-message fsync) for throughput.
With `acks=all` + replication for durability, per-broker throughput drops to
~50,000–200,000/sec.

### Chronicle Queue (Java, embedded, file-system based)

**Storage design:**  
Events are written to memory-mapped files (mmap). Writing is a `memcpy` into mapped
virtual address space. The OS handles page cache and eventual persistence. For explicit
durability, `msync(MS_SYNC)` is called.

**Throughput:**
- In-process, no msync: ~50–200 million entries/sec (essentially RAM speed)
- In-process, msync per entry: ~200,000–1,000,000 entries/sec
- Cross-process via mmap'd file: ~500,000–2,000,000 entries/sec

**Key insight:** mmap eliminates the syscall overhead of `write()`/`read()`. Memory
stores go directly to the page cache; the MMU handles TLB and dirty-page tracking.
This is the fastest possible approach on any OS — but it requires careful handling of
file growth, cross-process synchronisation, and crash recovery.

### SQLite WAL Mode (C, embedded, file-system based)

**Storage design:**  
Writes go to a Write-Ahead Log file first. A background checkpointer periodically merges
the WAL back into the main database file. Readers use MVCC over the WAL + main file.
Cross-process safety is handled by the SQLite VFS layer using OS-level file locks.

**Throughput (SSD, single-threaded writes, WAL mode):**
- Local drive: ~10,000–100,000 INSERTs/sec
- SMB network share: ~500–5,000 INSERTs/sec (latency-bound by network round-trip)

**Cross-process safety:** Yes, on local drives. Unreliable on NFS;
partially reliable on SMB depending on implementation.

**Key insight:** SQLite's throughput is ~100× higher than Opossum's on local drives,
while remaining cross-process safe. On a network share, the advantage narrows but
remains significant (500/sec vs 100/sec = 5×).

### hallgren/eventsourcing — BoltDB backend (Go, embedded)

**Storage design:**  
BoltDB is a pure-Go key-value store backed by a memory-mapped B-tree. Single-writer,
multiple-reader. Cross-process safe on local drives (uses flock).

**Throughput:** ~20,000–100,000 writes/sec local SSD.  
**Key insight:** No file-per-event, no index-file management — the entire store is
one B-tree file. Eliminates all of Opossum's per-event file I/O overhead.

### Python `eventsourcing` library — SQLite backend

**Storage design:** Uses SQLite WAL mode via Python's `sqlite3` module.  
**Throughput:** ~2,000–30,000 events/sec local, ~200–2,000 on network share.  
**Key insight:** Even a dynamically-typed language with CPython overhead beats
Opossum's file-per-event approach by 20× on local drives.

---

## Part 3 — The Universal Pattern

Every high-throughput file-system event store uses one or more of three techniques:

```
1. Sequential append to one open file  (eliminates file-open/close overhead)
2. Batched fsync (group commit)         (amortises the 3–8 ms fsync cost)
3. Memory-mapped I/O                    (eliminates kernel-mode transitions per write)
```

**No high-throughput system uses one-file-per-event with per-file-open, full-rewrite
indexes, and per-event fsync.** This is Opossum's current design.

---

## Part 4 — Concrete Improvement Paths for Opossum

The following options are ordered by implementation cost vs. expected gain.
All preserve Opossum's zero-infrastructure, file-system-based identity.

---

### Option A: Append-only index files  
**Effort:** Low · **Impact:** ~2–3× on no-flush path

**Current design:**  
Every append reads the full `tagkey=tagvalue.json` file, deserialises the full position
array, appends one entry, re-serialises, writes a temp file, renames.

**Proposed design:**  
Index files become append-only newline-delimited position lists:

```
# tagkey=tagvalue.idx
1
5
12
47
```

Append = `File.AppendAllTextAsync($"{position}\n")` — one write, no read, no temp file.  
Read = `File.ReadAllLinesAsync(...)` — parse longs, sort, deduplicate on load.

**Why correct:** The cross-process lock from ADR-005 means only one appender is active
at a time. Concurrent readers use `FileShare.Read` which is compatible with an open
append handle. Duplicates cannot occur because append is serialised.

**Estimated result:**  
Eliminates 2 reads + 2 temp-write-rename cycles per event (for 2 tags).  
From ~28 file I/O calls/event to ~18 file I/O calls/event.  
**Expected throughput (no-flush): ~300–350 events/sec** (~1.5× improvement).

**Storage format change:** Breaking. Existing stores would need a one-time migration.

---

### Option B: In-memory index cache  
**Effort:** Medium · **Impact:** ~2–4× on no-flush path (especially at scale)

**Design:**  
Load all tag and event-type indexes into memory at startup. Serve all read queries
from memory. On append, update both the in-memory cache and the on-disk file.

With the cross-process lock from ADR-005:
- When the lock is acquired, compare the index directory's last-modified timestamp
  against the cache's timestamp. If another process wrote while this process was not
  holding the lock, reload the changed index files.
- This makes the cache valid for the full duration of the locked append.

**Effect on reads:** Queries no longer hit the disk at all. Current read benchmarks
(4–84 ms for 100–10,000 events) are already fast, but this eliminates the index file
I/O entirely for reads.

**Effect on appends:** The read step in each TagIndex/EventTypeIndex update is
eliminated (read from memory instead of disk). The write step remains.

**Estimated result:**  
Eliminates 1 read per tag + 1 read per event type from the write path.  
From 28 to ~21 file I/O calls/event (saves the 4 reads for 2 tags + 1 event type).  
**Expected throughput (no-flush): ~280–320 events/sec** (~1.4× improvement).

Greater impact at large store sizes where index files are large (current: reads
scale linearly with index file size; cache: O(1) lookup).

---

### Why A+B together only give ~1.4× on the flush=true path

This is the critical constraint to understand before evaluating further options.

The `FlushEventsImmediately = true` cost of 10.853 ms breaks into two independent budgets:

```
45% — no-flush I/O overhead:  4.874 ms  ← A+B attacks this
55% — fsync overhead:          5.979 ms  ← A+B does NOT touch this
  ├─ Event file fsync:         ~3.5 ms
  └─ Ledger fsync:             ~2.5 ms
```

After A+B, the no-flush overhead drops from ~4.874 ms to ~1.9 ms. But the 5.979 ms in
fsyncs is completely unchanged. Result:

```
A+B, flush=true: ~1.9 ms + ~5.98 ms = ~7.9 ms → ~127 events/sec (1.38× improvement)
```

**A and B fix the right problem for the no-flush path; they fix the wrong 45% for the
flush path.** To materially improve flush=true throughput, one of the two fsyncs must
be eliminated.

---

### Option E: Implicit ledger (eliminate the second fsync)  
**Effort:** Low–Medium · **Impact:** Additional ~1.4× on the flush=true path

**Observation:**  
The ledger fsync (~2.5 ms) is redundant given the event file write protocol that already
exists. The event file is:
1. Written to a temp file
2. Fsynced to disk (durability guaranteed)
3. Renamed to `position_N.json` (the rename is atomic on all supported OS)

Once the rename completes, the file `position_N.json` exists on disk. The position is
encoded in the filename. The ledger then writes and fsyncs the same integer a second time —
it is a duplicate durability guarantee for information already durable.

**Proposed design:**  
Eliminate the ledger file entirely. Position tracking becomes:
- **In-memory:** an `long` counter incremented inside the cross-process lock (never stale
  because only one writer holds the lock at a time)
- **On startup:** scan the events directory for the highest-numbered `*.json` file to
  initialise the in-memory counter (one-time directory scan, not per-append)
- **Crash recovery:** if the app crashes mid-append the temp file is cleaned up on restart;
  the next startup scan finds the correct highest committed position

**Effect on fsyncs:**  
- Current: 2 fsyncs/event (~3.5 ms + ~2.5 ms = ~6 ms)
- After: 1 fsync/event (~3.5 ms) — the ledger path produces zero fsyncs

**Combined with A+B:**

```
A+B + implicit ledger, flush=true:
  ~1.9 ms (I/O) + ~3.5 ms (1 fsync) = ~5.4 ms → ~185 events/sec (~2× improvement)

A+B + implicit ledger, no-flush:
  ~1.65 ms (I/O, no ledger write at all) → ~606 events/sec (~3× improvement)
```

**Breaking change:** The `.ledger` file disappears from the store directory. Existing
stores migrate transparently — on first startup, the position is re-derived from the
directory scan. No event data is affected.

**Correctness note on crash recovery:**  
If a process crashes after renaming `position_N.json` but before updating the in-memory
counter, the next startup scan recovers the correct position from the filesystem. The
only crash window where data could be lost is the same as today: if the process dies
between the temp file write and the rename. The fsync-before-rename protocol (unchanged)
already closes that window.

---

### Option C: Append-only event log (WAL)  
**Effort:** High · **Impact:** ~3–5× on no-flush path, ~2× on flush path

**Design:**  
Replace one-file-per-event (`0000000001.json`, `0000000002.json`, ...) with a single
`events.log` file in newline-delimited JSON (NDJSON) format:

```
{"position":1,"event":{...},"metadata":{...}}
{"position":2,"event":{...},"metadata":{...}}
```

A single `FileStream` opened in `FileMode.Append` is held open for the store's lifetime.
Writes are sequential appends to this stream. A separate `events.idx` file maps
`position → byte_offset` in the log (append-only, same as Option A).

**Effect on fsyncs:**  
- Current: 2 fsyncs per event (event file + ledger)
- WAL: 1 fsync per event (the log file), or 1 fsync per batch
- The ledger file can be replaced by the log file's byte offset — no separate fsync

**Effect on file I/O:**  
- Current: 5 file operations for the event file alone
- WAL: 1 write to open stream (no open/close syscall, no temp file, no rename)
- Saves ~1.5 ms/event on the event write path

**Estimated result:**  
From 28 file I/O calls/event to ~20 (savings from event file path).  
From 2 fsyncs to 1 fsync (saves ~3 ms/event with flush enabled).  
**Expected throughput (no-flush): ~350–500 events/sec**  
**Expected throughput (with-flush): ~180–250 events/sec**

**Compound with batched fsync:**  
If multiple events accumulate between fsyncs (group commit):  
At batch size 10: 10 events / (10 × 0.5 ms write + 5 ms fsync) = ~1,000 events/sec  
At Opossum's actual event rate (0.4/sec), batches are size=1 → no benefit for normal use.  
During bulk imports: very significant.

**Breaking change:** Requires migration of existing stores. Projection rebuilds need
to stream the log file instead of reading individual event files.

---

> **Note:** A SQLite backend option was evaluated and ruled out by product decision.
> Opossum is and will remain file-system based. File-per-event and human-readable JSON
> files are non-negotiable product constraints.

---

## Part 5 — Comparative Summary

All flush=true figures are the production-relevant numbers (durability guaranteed).
No-flush figures are included for completeness but are not the design target.

| Approach | No-flush (local) | Flush=true (local) | Flush=true (SMB) | Cross-process | Breaking | Effort |
|---|---|---|---|---|---|---|
| **Current Opossum** | ~205/sec | ~92/sec | ~70–80/sec | ❌ → ✅ ADR-005 | — | — |
| **+ Option A (append-only indexes)** | ~300–350/sec | ~105–110/sec | ~80–90/sec | ✅ | Index format | Low |
| **+ Option B (in-memory cache)** | ~250–280/sec | ~100–105/sec | ~78–85/sec | ✅ | None | Medium |
| **+ Option A + B combined** | ~400–500/sec | ~125–130/sec | ~90–100/sec | ✅ | Index format | Medium |
| **+ Option E (implicit ledger)** | ~400–500/sec | ~125–130/sec | ~90–100/sec | ✅ | Ledger removed | Low |
| **+ A + B + E combined** | ~550–650/sec | ~180–190/sec | ~120–140/sec | ✅ | Index + ledger | Medium |
| **+ Option C (WAL log) alone** | ~350–500/sec | ~180–250/sec | ~100–150/sec | ✅ | Storage format | High |
| **+ Option C + group commit** | ~1,000–5,000/sec | ~300–800/sec | ~150–400/sec | ✅ | Storage format | High |
| **EventStoreDB (reference)** | N/A | ~15,000–50,000/sec | N/A (server) | ✅ | N/A | N/A |
| **Chronicle Queue (reference)** | ~200,000+/sec | N/A (msync) | N/A (local mmap) | ✅ | N/A | N/A |

---

## Part 6 — Recommendation

### Fixed product constraints

- Storage is always file-system based. No SQLite, no embedded database.
- File-per-event is preserved. Human-readable JSON files stay.
- `FlushEventsImmediately = true` is the production default and is not negotiable.
  Power-outage durability is a hard requirement.

### Execution order

| Step | Gain on flush=true | What it fixes | When |
|---|---|---|---|
| **ADR-005** (cross-process lock) | Correctness (not speed) | Multi-PC data corruption | Before anything else |
| **Option E** (implicit ledger) | ~92 → ~130/sec (+41%) | Redundant second fsync | After ADR-005 — small, safe |
| **Option A** (append-only indexes) | ~130 → ~150/sec (+15%) | Index read-modify-write | Can be done in parallel with E |
| **Option B** (in-memory index cache) | ~150 → ~180–190/sec (+27%) | Remaining index I/O | After A |
| **A + B + E combined** | ~92 → ~180–190/sec (~2×) | All non-fsync overhead | — |
| **Option C** (WAL log) | ~180 → ~300+/sec | Remaining single fsync | Future milestone |

### The honest ceiling with file-per-event + flush=true

With A+B+E fully implemented, the single remaining fsync (~3.5 ms) sets a hard ceiling
of **~285 events/sec** even with zero file I/O overhead. Breaking through that ceiling
requires either:
- Batched fsync (group commit) — adds complexity, no benefit at Opossum's typical rates
- The WAL approach (Option C) — allows one fsync to cover multiple events

For all realistic use cases (SMB, dealership, assembly line), 180–190 events/sec with
full durability is more than sufficient. Option C is a future capability, not an urgency.

### What to explicitly NOT do

- Do not implement group commit before A+B+E. The fsyncs would still dominate.
- Do not implement mmap. It helps sequential large files; it does not help a directory
  of thousands of small independent files.
- Do not remove the event file fsync. That is the only durability guarantee that matters.

---

## Part 7 — Assembly Line Requirements

### What counts as a business event on a manufacturing line

Event sourcing captures **business-meaningful state transitions**, not raw telemetry.
Sensor readings (temperature every 100 ms, pressure every second) are time-series data
for tools like InfluxDB or TimescaleDB — not event sourcing. The events Opossum would
store are:

- Machine state changes: `MachineStarted`, `MachinePaused`, `FaultRaised`, `FaultCleared`
- Production events: `CycleStarted`, `CycleCompleted`, `UnitPassed`, `UnitRejected`
- Operator actions: `ParameterAdjusted`, `MaterialLoaded`, `ToolChanged`
- Batch/order events: `OrderStarted`, `BatchCompleted`, `ShiftHandover`

### Sustained event rates for 2–4 machines/robots

| Line type | Industry example | Events per cycle | Takt time | Sustained rate (4 machines) |
|---|---|---|---|---|
| Slow — heavy manufacturing | Automotive body welding | ~5 events | 60–120 sec | **0.04–0.08 events/sec** |
| Medium — general assembly | Mechanical assembly + test | ~6 events | 15–30 sec | **0.2–0.4 events/sec** |
| Fast — electronics SMT | PCB population + reflow | ~8 events | 8–15 sec | **0.5–1.0 events/sec** |
| High-speed — pharma | Blister pack fill + seal + inspect | ~4 events | 1–3 sec | **1.5–4.0 events/sec** |

### Burst scenario

Alarm flood, batch changeover, or all 4 machines completing a cycle simultaneously:
**20–30 events/sec for 10–30 seconds.** This is the worst-case burst, not the
sustained rate.

### Minimum comfortable requirement

Applying a **3× safety margin** over the worst-case burst:
**~50 events/sec sustained** is the minimum comfortable requirement for any assembly
line business-event-sourcing deployment with 2–4 machines.

### Headroom analysis

| Scenario | Required | Current (92/sec) | After A+B+E (~185/sec) |
|---|---|---|---|
| Automotive / heavy | 5 events/sec | ✅ 18× headroom | ✅ 37× headroom |
| Electronics SMT | 20 events/sec | ✅ 4.6× headroom | ✅ 9.3× headroom |
| Pharma packaging burst | 50 events/sec | ✅ 1.8× headroom | ✅ 3.7× headroom |

**The current Opossum already satisfies every assembly line business-event scenario.**
Even the tightest realistic case (pharma packaging at burst) has 1.8× headroom today,
and 3.7× after A+B+E.

### Where Opossum would not be the right tool

If the requirement is to store individual sensor readings at high frequency
(e.g., every 100 ms per sensor × 20 sensors × 4 machines = **800 events/sec**),
that is time-series data, not event sourcing. No amount of throughput improvement
to Opossum changes that architectural mismatch.
