# Scalable Projection Rebuild Architecture

> **Status:** Design Complete — Approved for Implementation
> **Target version:** 0.5.0-preview.1
> **Tasks document:** docs/design/scalable-projection-rebuild-tasks.md
> **Status tracker:** docs/design/scalable-projection-rebuild-status.md
> **Created:** 2026-03

---

## Table of Contents

1. [Context and Problem Statement](#1-context-and-problem-statement)
2. [Design Goals](#2-design-goals)
3. [Core Design Decisions](#3-core-design-decisions)
4. [New Component Architecture](#4-new-component-architecture)
5. [Memory Model: Before vs. After](#5-memory-model-before-vs-after)
6. [Crash Recovery Design](#6-crash-recovery-design)
7. [File System Layout](#7-file-system-layout)
8. [Sequence Diagrams](#8-sequence-diagrams)
9. [Configuration Changes](#9-configuration-changes)
10. [API Changes (Breaking)](#10-api-changes-breaking)
11. [What Changes vs. What Stays the Same](#11-what-changes-vs-what-stays-the-same)

---

## 1. Context and Problem Statement

### 1.1 Current Architecture Summary

The current rebuild implementation (`FileSystemProjectionStore.BeginRebuild` /
`CommitRebuildAsync`) uses an **in-memory full-buffer approach**:

1. `BeginRebuild()` creates a temp directory and sets `_rebuildMode = true`.
2. During event replay every `SaveAsync(key, state)` call accumulates the latest state into
   `_rebuildStateBuffer: Dictionary<string, TState?>` — no disk I/O in this phase.
3. `CommitRebuildAsync()` iterates the buffer, serialises each state to the temp directory
   sequentially, builds tag index files, then atomically renames the temp directory over the
   production directory.

The rebuild orchestration (event loop, checkpoint management, parallel coordination) lives
entirely inside `ProjectionManager`, the same class that handles live event processing and
projection registration. `ProjectionDaemon` calls into `ProjectionManager` for both concerns.

### 1.2 Identified Scaling Failures

| Issue | Root Cause | Impact at Scale |
|-------|-----------|-----------------|
| Unbounded state buffer | `_rebuildStateBuffer` holds all unique keys in memory until commit | OOM with hundreds of thousands of unique projection keys |
| Aggregated metadata index as single JSON blob | `ProjectionMetadataIndex.PersistIndexAsync` serialises the entire `_cache` dictionary into one file | 100 MB – 1 GB+ single file at 1M entries; catastrophic startup load |
| Second full dictionary allocation at commit | `metadataEntries: Dictionary<string, ProjectionMetadata>` allocated inside `CommitRebuildAsync` | Peak memory doubles just before the commit |
| Third allocation via `_cache.ToDictionary()` | `PersistIndexAsync` calls `.ToDictionary()` on the entire cache before serialising | Third simultaneous copy of all metadata |
| Sequential commit writes | `foreach` loop in `CommitRebuildAsync` with no parallelism | Minutes of single-threaded I/O for hundreds of thousands of files |
| Sequential tag index writes at commit | `AddProjectionAsync` called per-key, each call reads then rewrites the same tag file | O(unique_keys × tags_per_key) sequential file round-trips |
| No crash recovery | Stateless rebuild loop with no durable progress marker | Any interruption (power loss, OOM, restart) requires a full rebuild from position 0 |
| Rebuild tightly coupled to live processing | All rebuild code lives inside `ProjectionManager` | Impossible to reason about or test either concern in isolation |

### 1.3 Concrete Worst-Case: 1 Million Projections of One Type

With 1M unique projection keys and a 10 KB average state size:

| Memory component | Current | New |
|-----------------|---------|-----|
| `_rebuildStateBuffer` | ~10 GB | 0 (eliminated) |
| `metadataEntries` at commit | ~200 MB | 0 (eliminated) |
| `_cache.ToDictionary()` in persist | ~200 MB | 0 (eliminated) |
| Tag accumulator (3 tags × 1M keys × ~40 bytes) | ~108 MB | ~108 MB |
| Event batch (500 × ~1 KB) | ~500 KB | ~500 KB |
| **Total peak** | **~10.5 GB** | **~109 MB** |

---

## 2. Design Goals

| Priority | Goal | Requirement |
|----------|------|-------------|
| 1 (Critical) | Bounded memory | Peak heap during rebuild proportional to `RebuildBatchSize × state_size`, not `unique_keys × state_size` |
| 1 (Critical) | Crash recovery | On restart after any failure, resume from last safe flush point — not from position 0 |
| 1 (Critical) | Millions of projections | Successfully rebuild 1M+ unique projection keys with no memory pressure |
| 2 (High) | Durability first | Acceptable to trade extra I/O for bounded memory and recoverability |
| 2 (High) | Clean separation | Rebuild logic fully separated from the live projection daemon logic |
| 2 (High) | Production readiness | Orphaned temp directories detected and cleaned up; journals are self-describing |
| 3 (Medium) | Preserve existing public API surface where possible | Minimise breaking changes to library consumers; breaking changes are acceptable when they improve the library |

---

## 3. Core Design Decisions

### Decision 1: Write-Through Mode (replacing the in-memory full buffer)

**What changes:**
During event replay, each `SaveAsync(key, state)` call immediately writes the projection
file to the temp directory instead of accumulating it in `_rebuildStateBuffer`.

**Rationale:**
The full-buffer approach exists to reduce I/O: by buffering all states, each unique key is
written to disk exactly once at commit (O(unique keys) writes) instead of once per event
that touches the key (O(events) writes). However, durability and memory safety outweigh
I/O minimisation for rebuild (a rare operation). Write-through:

- Bounds memory to O(batch_size × state_size) regardless of projection count.
- Makes partial work durable: the temp directory always reflects current replay progress.
- Enables crash recovery without requiring a buffered state snapshot.
- Keeps commit lightweight: only tag index files need writing at the end.

**Trade-off accepted:**
A key updated by 50 events is written 50 times to the temp directory instead of once.
For a full rebuild the total file-write count equals the number of distinct (key, event)
pairs processed, not just the number of unique keys. This is more I/O than the buffer
approach but the I/O is distributed over time rather than burst-loading the disk at the end.
Given that rebuild is rare and the primary concern is bounded memory, this trade-off is
explicitly accepted.

**I/O cost clarification:**
The actual per-event I/O is `2 × O(events applied)`, not `O(events applied)`. Each event
application first calls `GetAsync(key)` to load the current state from the temp directory
(1 file read) and then calls `SaveAsync(key, state)` to write the updated state back
(1 file write). For a key touched by 50 events: 50 reads + 50 writes = 100 file operations
during replay, compared to 0 during replay and 1 write at commit in the old buffer approach.
This cost is distributed evenly over the replay duration and does not create memory
pressure.

---

### Decision 2: Rebuild Journal for Crash Recovery

**What changes:**
A new lightweight JSON file `{projectionName}.rebuild.json` is written to `_checkpointPath`
when a rebuild starts. It records the temp directory path, the store head at rebuild start,
and the last safely flushed event position. The journal is updated atomically every
`RebuildFlushInterval` events and deleted on successful commit.

**Journal format:**
```json
{
  "projectionName": "StudentInfo",
  "tempPath": "D:\\Database\\Large\\Projections\\StudentInfo.tmp.a1b2c3d4e5f6",
  "storeHeadAtStart": 2000000,
  "resumeFromPosition": 1250000,
  "startedAt": "2026-01-15T10:30:00Z",
  "lastFlushedAt": "2026-01-15T11:45:30Z"
}
```

**Rationale:**
With write-through mode the temp directory always contains valid partial state up to the
last event processed. On crash, the journal tells the resuming process exactly where to
pick up. Only the events in the range `(resumeFromPosition, crashPosition]` need
re-processing. Any files already written for those positions are simply overwritten,
restoring correctness. Maximum re-work = `RebuildFlushInterval` events (default: 10,000)
— not the entire event log.

---

### Decision 3: Separate `ProjectionRebuilder` from `ProjectionManager`

**What changes:**
All rebuild orchestration is extracted from `ProjectionManager` into a new
`ProjectionRebuilder` class implementing `IProjectionRebuilder`.
`ProjectionManager` retains only: projection registration, live event processing
(`UpdateAsync`), and checkpoint management.

**Rationale:**
The user requirement is explicit: rebuild and live processing must be cleanly separated.
Additional benefits:
- Each class has a single reason to change.
- Integration tests can target rebuild or live processing independently.
- `ProjectionDaemon` injects `IProjectionRebuilder` for startup rebuilds and
  `IProjectionManager` for live event updates — both responsibilities are visible in the
  constructor.
- `ProjectionRebuilder` is not a `BackgroundService`; it is a regular singleton service
  invoked by `ProjectionDaemon` and admin endpoints.

**Internal access:**
`ProjectionRebuilder` needs access to the registered `ProjectionRegistration<TState>`
objects (to call `BeginRebuildAsync`, `ApplyAsync`, `CommitRebuildAsync`). Since both
classes are `internal sealed` in the same assembly, `ProjectionManager` exposes an
internal method `GetRegistrationNames()` and `GetRegistration(name)` (returning the
abstract `ProjectionRegistration` base). This avoids making the registration dictionary
public while keeping the two classes properly separated.

---

### Decision 4: Eliminate Aggregated Metadata Index from the Rebuild Critical Path

**What changes:**
`ProjectionMetadataIndex.BatchSaveAsync` is not called during rebuild.
`_metadataIndex.ClearAsync` is also not called during replay.
After the atomic directory swap, the production directory contains no `Metadata/index.json`.
The aggregated index is a read-side optimisation only, populated lazily on first query after
rebuild completes.

**Rationale:**
Every projection file already contains embedded metadata via the `ProjectionWithMetadata<TState>`
wrapper (`CreatedAt`, `LastUpdatedAt`, `Version`, `SizeInBytes`). The aggregated index
exists to serve bulk metadata queries without reading all N individual files. At 1M keys
this becomes a problem:

- A single `index.json` of 100 MB – 1 GB must be serialised in one shot.
- `_cache.ToDictionary()` creates a full in-memory copy before serialisation.
- Loading this file on application startup requires reading and deserialising the entire
  thing into a `ConcurrentDictionary<string, ProjectionMetadata>`.

Post-rebuild, the first metadata query that needs the aggregated index will rebuild it from
the individual projection files (lazy population). For production workloads that do not
query aggregated metadata, the index is never built. For workloads that do, the cost is
paid once after rebuild, not during rebuild where memory is already under pressure.

**Long-term note:**
The aggregated metadata index has a scaling problem even outside rebuild (the in-memory
`_cache` holds all entries). This is out of scope for this redesign but should be tracked
as a future issue: shard the index into per-bucket files keyed by hash prefix.

---

### Decision 5: In-Memory Tag Accumulator + Parallel Bulk Write at Commit

**What changes:**
During rebuild, instead of calling `_tagIndex.AddProjectionAsync` for each key (which
reads and rewrites the relevant tag file once per key), the store accumulates a
`Dictionary<string, HashSet<string>>` (tag index file name → set of projection keys) in
memory. At commit time all tag index files are written in parallel to the temp directory.

**Rationale:**
`AddProjectionAsync` per key performs a read-modify-write cycle on the same tag file
for every key that carries that tag. For 1M keys with 3 tags each this is 3M sequential
file operations. By accumulating in memory and writing once, this reduces to `N_tags`
parallel file writes at commit — orders of magnitude fewer I/O operations.

**Memory cost:**
`N_tags × unique_keys × avg_key_string_length` ≈ `3 × 1M × 40 bytes` = ~120 MB for the
extreme 1M-key scenario. This is acceptable for a rare rebuild operation.

**Tag accumulator persistence for crash recovery:**
The tag accumulator must be persisted alongside the rebuild journal to support correct
resume after a crash. On resume, events at positions ≤ `resumeFromPosition` are NOT
re-read, so their tags cannot be re-derived from the event replay loop. Without persisting
the accumulator, the tag index files written at commit would only contain keys from the
resumed portion — missing all keys processed before the crash.

The solution is to serialise the tag accumulator as a companion file
`{projectionName}.rebuild.tags.json` next to the rebuild journal. This file is written
atomically (temp + rename) every time the journal is flushed. On resume, it is loaded
before the replay loop starts, restoring the accumulator to its pre-crash state.

Memory cost of the companion file: same as the in-memory accumulator
(`N_tags × unique_keys × avg_key_string_length`). For the 1M-key × 3-tag scenario this
is ~120 MB on disk — acceptable for a rare rebuild operation.

**If tag memory becomes a concern (many tags per key):**
A future optimisation could flush the tag accumulator to disk periodically and merge at
commit using a sorted merge strategy. This is not implemented in this design.

---

## 4. New Component Architecture

### 4.1 Component Responsibilities

```
┌──────────────────────────────────────────────────────────────────┐
│ ProjectionDaemon  (BackgroundService)                            │
│                                                                  │
│ Startup:  1. calls IProjectionRebuilder.ResumeInterrupted...()  │
│           2. calls IProjectionRebuilder.RebuildAllAsync()       │
│ Polling:  3. calls IProjectionManager.UpdateAsync()             │
└──────┬───────────────────────────────────────────┬──────────────┘
       │ injects / uses                             │ injects / uses
       ▼                                            ▼
┌──────────────────────┐               ┌────────────────────────────┐
│ IProjectionManager   │               │ IProjectionRebuilder       │
│                      │               │                            │
│ RegisterProjection() │               │ ResumeInterrupted...()     │
│ UpdateAsync()        │               │ RebuildAsync(name)         │
│ GetCheckpointAsync() │               │ RebuildAllAsync(force)     │
│ SaveCheckpointAsync()│               │ RebuildAsync(names[])      │
│ GetRegisteredProj... │               │ GetRebuildStatusAsync()    │
└──────────────────────┘               └────────────────┬───────────┘
                                                        │ uses (internal)
                                                        ▼
                                       ┌────────────────────────────┐
                                       │ ProjectionRegistration     │
                                       │ (internal, via PM access)  │
                                       │                            │
                                       │ BeginRebuildAsync()        │
                                       │ BeginRebuildAsync(tempPath)│
                                       │ ApplyAsync()               │
                                       │ CommitRebuildAsync()       │
                                       └────────────────────────────┘
```

### 4.2 New Class: `ProjectionRebuilder`

```
ProjectionRebuilder  (internal sealed, implements IProjectionRebuilder)
│
├── Constructor
│     IEventStore, ProjectionManager, ProjectionOptions,
│     ILogger<ProjectionRebuilder>
│
├── Public (IProjectionRebuilder)
│   ├── ResumeInterruptedRebuildsAsync(ct)
│   │     Scans _checkpointPath for *.rebuild.json
│   │     For each journal: verify tempPath exists → resume or clean up
│   │
│   ├── RebuildAsync(string name, ct)   → ProjectionRebuildResult
│   ├── RebuildAllAsync(bool force, ct) → ProjectionRebuildResult
│   ├── RebuildAsync(string[] names, ct)→ ProjectionRebuildResult
│   └── GetRebuildStatusAsync()         → ProjectionRebuildStatus
│
└── Private
    ├── RebuildCoreAsync(name, fromPosition, tempPath, storeHead, ct)
    │     The single-projection event loop.
    │     Writes journal on start; flushes journal every RebuildFlushInterval.
    │     Calls BeginRebuildAsync / ApplyAsync / CommitRebuildAsync.
    │
    ├── CreateJournalAsync(name, tempPath, storeHead, ct)
    ├── FlushJournalAsync(name, position, tagAccumulator, ct)
    ├── DeleteJournalAsync(name, ct)
    ├── ReadJournalAsync(name, ct) → ProjectionRebuildJournal?
    ├── GetJournalFilePath(name)
    ├── FlushTagAccumulatorAsync(name, tagAccumulator, ct)
    ├── LoadTagAccumulatorAsync(name, ct) → Dictionary<string, HashSet<string>>?
    ├── CleanOrphanedTempDirectoriesAsync(ct)
    └── (rebuild status tracking: same lock-based approach as today)
```

### 4.3 New Model: `ProjectionRebuildJournal`

```csharp
internal sealed class ProjectionRebuildJournal
{
    public required string ProjectionName { get; init; }
    public required string TempPath { get; init; }
    public required long StoreHeadAtStart { get; init; }
    public required long ResumeFromPosition { get; set; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset LastFlushedAt { get; set; }
}
```

Location on disk: `{_checkpointPath}/{projectionName}.rebuild.json`

Written atomically (temp file + rename) to match the pattern used for all other durable
files in Opossum.

### 4.4 Modified: `FileSystemProjectionStore<TState>`

**Removed fields:**
- `_rebuildStateBuffer: Dictionary<string, TState?>` — eliminated entirely

**Unchanged fields:**
- `_rebuildTempPath: string?` — still used; now populated from rebuild start, not lazily
- `_rebuildMode: bool` — renamed to `_isInRebuild` for clarity

**New field:**
- `_tagAccumulator: Dictionary<string, HashSet<string>>?`
  Tag index file path → set of projection keys. Non-null only during rebuild.

**Changed behaviour — `SaveAsync` (rebuild path):**
```
Old: _rebuildStateBuffer[key] = state  (no disk I/O)
New: Serialise state → write directly to GetFilePath(key) (which resolves to _rebuildTempPath)
```

**Changed behaviour — `GetAsync` (rebuild path):**
```
Old: return _rebuildStateBuffer.TryGetValue(key, ...) ? buffered : null
New: read {_rebuildTempPath}/{key}.json from disk; return null if file not found
```
This is correct: the temp directory is the authoritative current state during rebuild.
Falling back to the production directory is explicitly wrong (stale data).

**Changed behaviour — `CommitRebuildAsync`:**
```
Old: iterate _rebuildStateBuffer → write each file sequentially → build tag index per key → dir swap
New: write tag index files in parallel from _tagAccumulator → dir swap
     (all state files already written during replay)
```

**`BeginRebuild` changes:**
- Still creates temp directory.
- Still sets `_rebuildTempPath` and `_isInRebuild = true`.
- Now also initialises `_tagAccumulator = new()`.
- **Does NOT** try to call `Directory.CreateDirectory` if tempPath already exists
  (resume scenario: directory was created in a previous run).

**New internal method: `BeginRebuild(string tempPath)`**
Accepts an explicit temp path (used for resume). The existing no-arg overload generates
a new GUID-based path (used for fresh rebuilds). Both set `_isInRebuild = true`.

**Note on `_projectionTags` vs `_tagAccumulator`:**
`_projectionTags` and `_tagAccumulator` serve different purposes and are never used
simultaneously:
- `_projectionTags` is used during **live updates** only. It tracks old tags per key so
  `UpdateProjectionTagsAsync` can compute delta updates (remove old tags, add new tags).
  It is cleared on `BeginRebuild` and not populated during rebuild.
- `_tagAccumulator` is used during **rebuild** only. It accumulates all tag→keys mappings
  for bulk write at commit. It is set to `null` outside of rebuild.

**`BeginRebuildAsync(string tempPath)` on abstract `ProjectionRegistration`:**
The abstract `ProjectionRegistration` base class gains a new abstract method
`BeginRebuildAsync(string tempPath)` alongside the existing parameterless overload.
`ProjectionRegistration<TState>` implements it by casting to `FileSystemProjectionStore<TState>`
and calling `BeginRebuild(tempPath)`. This is required for `ProjectionRebuilder` to pass
the journal's temp path through the abstraction layer during crash recovery resume.

### 4.5 `ProjectionManager` after extraction

Removed from `ProjectionManager`:
- `RebuildProjectionCoreAsync` (private)
- `RebuildAsync(string, ct)` (public)
- `RebuildAllAsync(bool, ct)` (public)
- `RebuildAsync(string[], ct)` (public)
- `GetRebuildStatusAsync()` (public)
- `MoveToInProgress`, `RemoveFromInProgress`, `UpdateRebuildStatus` (private)
- `_currentRebuildStatus`, `_rebuildLock` (fields)

Added to `ProjectionManager` (internal, for `ProjectionRebuilder`):
- `GetRegistration(string name)` → `ProjectionRegistration?`

Remains on `IProjectionManager` (unchanged public API):
- `RegisterProjection<TState>`
- `UpdateAsync`
- `GetCheckpointAsync`
- `SaveCheckpointAsync`
- `GetRegisteredProjections`

---

## 5. Memory Model: Before vs. After

### Peak Memory During a Full Rebuild (at scale)

| Memory component | Current | New |
|-----------------|---------|-----|
| Event batch | O(batch_size × event_size) | O(batch_size × event_size) — **unchanged** |
| Projection state buffer | **O(unique_keys × state_size)** | **O(0)** — eliminated |
| Metadata dict at commit | **O(unique_keys × metadata_size)** | **O(0)** — not built |
| Metadata cache in index | **O(unique_keys × metadata_size)** | **O(0)** — not loaded |
| Tag accumulator | O(tags × unique_keys × key_len) | O(tags × unique_keys × key_len) — **unchanged** |
| Serialised JSON string per write | O(state_size) transient | O(state_size) transient — **unchanged** |

### I/O Profile Comparison

| Metric | Current | New |
|--------|---------|-----|
| File reads during replay | 0 | O(events applied) — `GetAsync` reads current state from temp dir |
| File writes during replay | 0 | O(events applied) — `SaveAsync` writes updated state to temp dir |
| File writes at commit | O(unique_keys) sequential | 0 — state already on disk |
| Tag index writes at commit | O(unique_keys × tags) sequential round-trips | O(tags) parallel writes |
| Metadata index write | O(1) enormous single file | Not written during rebuild |
| Total write amplification | Lower write count, catastrophic memory | Higher write count, bounded memory |

The new approach writes more files total. However:
- I/O is distributed over the full rebuild duration rather than burst-loading at commit.
- The commit phase is now very fast (only tag files + directory rename).
- Write amplification for any single key is bounded by how many events touch it (not
  artificially capped by the buffer — the buffer was always overwriting state in-place).

---

## 6. Crash Recovery Design

### 6.1 Normal (Successful) Rebuild Flow

```
ProjectionRebuilder.RebuildCoreAsync(name, fromPosition=0, tempPath=NEW_GUID):

  Phase 1 — Setup
  ├── Create temp directory at tempPath
  ├── Write journal: { name, tempPath, storeHead, resumeFromPosition=0, startedAt=now }
  └── Call store.BeginRebuild(tempPath)

  Phase 2 — Event Replay (write-through)
  ├── Loop: ReadAsync(eventTypes, fromPosition, batchSize)
  │   ├── For each event in batch:
  │   │   └── registration.ApplyAsync(evt) → store.GetAsync + store.SaveAsync
  │   │                                      → read {tempPath}/{key}.json (or null)
  │   │                                      → write {tempPath}/{key}.json
  │   ├── After each batch: update fromPosition, totalEventsProcessed
  │   └── Every RebuildFlushInterval events:
  │       ├── FlushJournalAsync(name, currentPosition)   [atomic rename]
  │       └── FlushTagAccumulatorAsync(name, accumulator) [atomic rename]
  └── Loop terminates when ReadAsync returns empty

  Phase 3 — Commit
  ├── Write tag index files from _tagAccumulator to tempPath (parallel)
  ├── Atomic swap: DeleteDirectory(productionPath) → Directory.Move(tempPath, productionPath)
  ├── SaveCheckpointAsync(name, Math.Max(storeHead, lastEventPosition))
  └── DeleteJournalAsync(name)  [also deletes {name}.rebuild.tags.json]
```

### 6.2 Crash Recovery Flow

```
Application restart → ProjectionDaemon.ExecuteAsync:

  Step 1: ProjectionRebuilder.ResumeInterruptedRebuildsAsync()
  ├── Scan _checkpointPath for *.rebuild.json files
  │
  ├── For each journal found:
  │   ├── IF journal.TempPath directory does NOT exist:
  │   │   ├── Log warning: "Rebuild journal for '{name}' references missing temp dir — starting fresh"
  │   │   ├── DeleteJournalAsync(name)
  │   │   └── Add name to "needs fresh rebuild" list
  │   │
  │   └── IF journal.TempPath directory EXISTS:
  │       ├── Log info: "Resuming interrupted rebuild of '{name}' from position {resumeFromPosition}"
  │       ├── store.BeginRebuild(journal.TempPath)   [reuse existing temp dir]
  │       ├── LoadTagAccumulatorAsync(name) → restore _tagAccumulator from companion file
  │       └── RebuildCoreAsync(name, fromPosition=journal.ResumeFromPosition,
  │                             tempPath=journal.TempPath, storeHead=journal.StoreHeadAtStart)
  │           Note: Events in range (0, resumeFromPosition] are NOT re-read.
  │                 Files in tempPath for those events are already correct.
  │                 Tags for those events are already in the restored _tagAccumulator.
  │                 Events in range (resumeFromPosition, ...] are re-read and re-applied,
  │                 overwriting any partial files from the crashed run.
  │
  └── Clean orphaned temp dirs (match {projectionName}.tmp.* pattern, no matching journal)

  Step 2: Normal missing-checkpoint detection (unchanged)
  └── RebuildAllAsync(forceRebuild: false)
```

### 6.3 Recovery Guarantees

| Property | Guarantee |
|----------|-----------|
| No data loss | State for all events at positions ≤ `resumeFromPosition` is durable on disk in tempPath |
| Bounded re-work | At most `RebuildFlushInterval` events are re-processed (not the entire event log) |
| Correctness on resume | Re-applied events overwrite files written during the crashed segment — idempotent |
| No partial file corruption | Each `SaveAsync` call writes atomically via OS file write (single file, complete content); a partially-written file can only occur if the OS crashes mid-write, in which case the key will be correctly overwritten on resume |
| No stale production reads | Production directory is never touched during rebuild; readers see consistent (if stale) data until commit |
| Tag accumulator correctness on resume | Tag accumulator is persisted to `{name}.rebuild.tags.json` on every journal flush; loaded on resume before the replay loop starts. Tags for events at positions ≤ `resumeFromPosition` are restored from the persisted file, not re-derived from replay |

### 6.4 Directory Swap Atomicity

The commit step performs `DeleteDirectory(productionPath)` followed by
`Directory.Move(tempPath, productionPath)`. This is **not an atomic operation** — there is
a brief window between delete and move where no production directory exists:

- On **Windows**, `Directory.Move` cannot overwrite an existing directory, so the
  delete-first pattern is required.
- On **Linux**, `rename(2)` can atomically replace a directory, but .NET's
  `Directory.Move` does not expose this semantic.

If the process crashes between delete and move:
- The production directory is gone (deleted).
- The temp directory still exists (move didn't happen).
- The journal exists with `resumeFromPosition` at the final position.

On restart, `ResumeInterruptedRebuildsAsync` finds the journal and calls
`RebuildCoreAsync(from=finalPosition)`. Since all events have already been processed,
the replay loop reads zero new events and proceeds directly to commit — performing the
directory swap again. This is **self-healing**: the window of unavailability is limited
to the time between the crash and the next application startup.

During this window, any live query hitting `GetAllAsync`, `QueryAsync`, or
`QueryByTagAsync` will find the production directory missing and return empty results.
This is acceptable because rebuild is rare and the window is extremely narrow.

### 6.5 Orphaned Temp Directory Handling

A temp directory can exist without a journal if:
- The journal was deleted but the directory rename failed (extremely rare, would require OS-level failure).
- A previous version of the software left the directory.

During `ResumeInterruptedRebuildsAsync`, after processing all journals, scan the
`Projections/` directory for subdirectories matching `*.tmp.*`. Any such directory with no
corresponding `*.rebuild.json` journal is deleted (no data loss: the production directory
is intact).

---

## 7. File System Layout

```
{RootPath}/{StoreName}/
├── Projections/
│   │
│   ├── StudentInfo/                        ← production (UNTOUCHED during rebuild)
│   │   ├── {student-guid}.json             ← ProjectionWithMetadata<StudentInfoState>
│   │   ├── Indices/
│   │   │   ├── type_student.json
│   │   │   └── faculty_engineering.json
│   │   └── Metadata/
│   │       └── index.json                  ← LAZY; not written during rebuild
│   │
│   └── StudentInfo.tmp.a1b2c3d4e5f6/       ← temp (written-through during rebuild)
│       ├── {student-guid}.json             ← grows incrementally during replay
│       ├── Indices/
│       │   ├── type_student.json           ← written at commit from tag accumulator
│       │   └── faculty_engineering.json
│       └── (no Metadata/index.json)        ← not written; metadata embedded per-file
│
└── _checkpoints/
    ├── StudentInfo.checkpoint              ← live checkpoint (unchanged format)
    ├── StudentInfo.rebuild.json            ← rebuild journal (NEW, temporary)
    ├── StudentInfo.rebuild.tags.json       ← persisted tag accumulator (NEW, temporary)
    ├── CourseDetail.checkpoint
    ├── CourseDetail.rebuild.json           ← if CourseDetail rebuild also in progress
    └── CourseDetail.rebuild.tags.json      ← if CourseDetail rebuild also in progress
```

During an in-progress rebuild, an additional file exists:
- `StudentInfo.rebuild.tags.json` — persisted tag accumulator (companion to the journal).

After a successful rebuild:
- `StudentInfo/` contains the freshly rebuilt state.
- `StudentInfo.tmp.a1b2c3d4e5f6/` no longer exists (moved to `StudentInfo/`).
- `StudentInfo.rebuild.json` no longer exists (deleted).
- `StudentInfo.rebuild.tags.json` no longer exists (deleted).
- `StudentInfo.checkpoint` has been updated.

---

## 8. Sequence Diagrams

### 8.1 Fresh Rebuild (no prior interruption)

```
ProjectionDaemon
    │
    ├─► ProjectionRebuilder.RebuildAllAsync()
    │       │
    │       ├─► Parallel.ForEachAsync [up to MaxConcurrentRebuilds]
    │       │       │
    │       │       ├─► ProjectionRebuilder.RebuildCoreAsync(name, from=0)
    │       │       │       │
    │       │       │       ├─► Directory.CreateDirectory(tempPath)
    │       │       │       ├─► CreateJournalAsync(name, tempPath, storeHead)
    │       │       │       ├─► store.BeginRebuild(tempPath)
    │       │       │       │
    │       │       │       ├─► [Replay loop]
    │       │       │       │       ReadAsync(types, from, batchSize)
    │       │       │       │       ApplyAsync(evt) → GetAsync + SaveAsync
    │       │       │       │                         ↳ read  {tempPath}/{key}.json
    │       │       │       │                         ↳ write {tempPath}/{key}.json
    │       │       │       │       [every FlushInterval]
    │       │       │       │           FlushJournalAsync(name, position)
    │       │       │       │           FlushTagAccumulatorAsync(name, accumulator)
    │       │       │       │
    │       │       │       ├─► [Commit]
    │       │       │       │       write tag indices → tempPath/Indices/ [parallel]
    │       │       │       │       DeleteDirectory(productionPath)
    │       │       │       │       Directory.Move(tempPath, productionPath)
    │       │       │       │       SaveCheckpointAsync(name, finalPosition)
    │       │       │       │       DeleteJournalAsync(name)  [+ tags companion file]
    │       │       │       │
    │       │       │       └─► return eventsProcessed
    │       │       │
    │       │       └─► [continues for next projection in parallel]
    │       │
    │       └─► return ProjectionRebuildResult
    │
    └─► begin polling loop (ProjectionManager.UpdateAsync)
```

### 8.2 Crash Recovery on Restart

```
Application starts
    │
    ├─► ProjectionDaemon.ExecuteAsync
    │       │
    │       ├─► ProjectionRebuilder.ResumeInterruptedRebuildsAsync()
    │       │       │
    │       │       ├─► scan _checkpointPath for *.rebuild.json
    │       │       │       found: StudentInfo.rebuild.json
    │       │       │             { resumeFromPosition: 1_250_000, tempPath: "...tmp.abc" }
    │       │       │
    │       │       ├─► Directory.Exists(tempPath) → true
    │       │       │
    │       │       ├─► store.BeginRebuild(existingTempPath)
    │       │       │       _isInRebuild = true
    │       │       │       _rebuildTempPath = existing temp dir (NOT re-created)
    │       │       │       _tagAccumulator = LoadTagAccumulatorAsync(name)
    │       │       │                          ↳ restored from {name}.rebuild.tags.json
    │       │       │
    │       │       └─► RebuildCoreAsync(name, from=1_250_000, tempPath=existing)
    │       │               ↳ only events with position > 1_250_000 are re-read
    │       │               ↳ files for positions ≤ 1_250_000 already correct in tempPath
    │       │               ↳ tags for positions ≤ 1_250_000 already in _tagAccumulator
    │       │               ↳ files for positions > 1_250_000 are overwritten cleanly
    │       │               ↳ commit → directory swap → delete journal + tags file
    │       │
    │       ├─► RebuildAllAsync(forceRebuild: false)   [picks up any OTHER missing projections]
    │       │
    │       └─► begin polling loop
```

---

## 9. Configuration Changes

One new option is added to `ProjectionOptions`:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `RebuildFlushInterval` | `int` | `10_000` | Number of events processed between rebuild journal flushes. Controls the maximum re-work required on crash recovery. Lower = more durable, more journal writes. Higher = less journal overhead, more re-work on recovery. |

Valid range: 100 – 1,000,000.

Existing options unchanged: `RebuildBatchSize`, `MaxConcurrentRebuilds`, `PollingInterval`,
`BatchSize`, `AutoRebuild` (formerly `EnableAutoRebuild` — see
docs/releases/0.5.0-preview.1-autorebuildmode-tasks.md for the enum migration).

---

## 10. API Changes (Breaking)

### ⚠️ Breaking: Methods removed from `IProjectionManager`

The following methods are removed from the `IProjectionManager` public interface and moved
to the new `IProjectionRebuilder` interface:

| Method | New home |
|--------|---------|
| `RebuildAsync(string projectionName, CancellationToken)` | `IProjectionRebuilder.RebuildAsync` |
| `RebuildAllAsync(bool forceRebuild, CancellationToken)` | `IProjectionRebuilder.RebuildAllAsync` |
| `RebuildAsync(string[] projectionNames, CancellationToken)` | `IProjectionRebuilder.RebuildAsync` |
| `GetRebuildStatusAsync()` | `IProjectionRebuilder.GetRebuildStatusAsync` |

**⚠️ Signature change:** `IProjectionManager.RebuildAsync(string, CancellationToken)`
currently returns `Task`. The new `IProjectionRebuilder.RebuildAsync(string, CancellationToken)`
returns `Task<ProjectionRebuildResult>`. This is an improvement: callers now receive a
result object with timing and event-count details. Existing callers that discarded the
result continue to compile (fire-and-forget `Task<T>` is assignable to `Task`).

**Migration for library consumers:**
Code that currently injects `IProjectionManager` and calls rebuild methods must instead
inject `IProjectionRebuilder` and call the same methods there. `IProjectionRebuilder` is
registered by `AddProjections()`.

**Approved:** This breaking change is approved. Opossum is pre-1.0 and not yet used in
production. Improving the API is more important than backward compatibility.

### New: `IProjectionRebuilder`

```csharp
public interface IProjectionRebuilder
{
    /// Resumes any rebuild interrupted by a previous crash/restart.
    /// Called automatically by ProjectionDaemon on startup.
    Task ResumeInterruptedRebuildsAsync(CancellationToken cancellationToken = default);

    /// Rebuilds a single projection from scratch.
    Task<ProjectionRebuildResult> RebuildAsync(
        string projectionName,
        CancellationToken cancellationToken = default);

    /// Rebuilds all registered projections.
    /// If forceRebuild is false, only projections without a checkpoint file are rebuilt.
    Task<ProjectionRebuildResult> RebuildAllAsync(
        bool forceRebuild = false,
        CancellationToken cancellationToken = default);

    /// Rebuilds specific projections by name.
    Task<ProjectionRebuildResult> RebuildAsync(
        string[] projectionNames,
        CancellationToken cancellationToken = default);

    /// Returns current rebuild status (in-progress, queued, idle).
    Task<ProjectionRebuildStatus> GetRebuildStatusAsync();
}
```

### Impact on Sample Application

The admin API endpoints in `Opossum.Samples.CourseManagement` that call
`IProjectionManager.RebuildAsync` / `RebuildAllAsync` must be updated to inject
`IProjectionRebuilder`.

---

## 11. What Changes vs. What Stays the Same

### Changed Significantly

| Component | Nature of Change |
|-----------|-----------------|
| `FileSystemProjectionStore<TState>` | Write-through `SaveAsync`; tag accumulator; `GetAsync` reads from temp; `CommitRebuildAsync` becomes tag-write + dir-swap only |
| `ProjectionManager` | Loses all rebuild methods and rebuild status tracking; gains `internal GetRegistration()` accessor |
| `IProjectionManager` | Loses 4 rebuild-related methods |
| `ProjectionDaemon` | Injects `IProjectionRebuilder`; calls `ResumeInterruptedRebuildsAsync` before `RebuildAllAsync` on startup |
| `ProjectionOptions` | Gains `RebuildFlushInterval` |
| `ProjectionServiceCollectionExtensions` | Registers `IProjectionRebuilder` as singleton |
| `ProjectionMetadataIndex` | Not called during rebuild critical path |

### New Files

| File | Purpose |
|------|---------|
| `src/Opossum/Projections/IProjectionRebuilder.cs` | Public interface |
| `src/Opossum/Projections/ProjectionRebuilder.cs` | Full rebuild implementation with journal management |
| `src/Opossum/Projections/ProjectionRebuildJournal.cs` | Journal model and file I/O helpers |

### Read-Query Behaviour During Rebuild

`GetAllAsync`, `QueryAsync`, `QueryByTagAsync`, and `QueryByTagsAsync` do **not** check
`_isInRebuild`. They always read from `_projectionPath` (the production directory), which
is untouched during rebuild. This means:

- Live queries continue to serve stale-but-consistent data throughout the rebuild.
- After `CommitRebuildAsync` completes the atomic directory swap, subsequent queries
  immediately see the freshly rebuilt state.
- If the production directory is missing (the narrow atomicity gap described in §6.4),
  these methods return empty results until the next startup completes the swap.

### Unchanged

| Component | Reason |
|-----------|--------|
| `IProjectionDefinition<TState>` | Projection definition API is not affected |
| `IProjectionWithRelatedEvents<TState>` | Related events loading is independent of rebuild mode |
| `IProjectionStore<TState>` | Public store API is unchanged |
| `ProjectionCheckpoint` | Checkpoint format and file location unchanged |
| `ProjectionTagIndex` | Core logic unchanged; new internal bulk-write method added for commit path |
| `ProjectionDaemon` polling loop | Live processing unaffected by this change |
| `ProjectionRebuildResult` / `ProjectionRebuildDetail` | Result models unchanged |
| All existing integration tests (rebuild, parallel) | Must all pass after implementation |
| Sample application event/command/query handlers | Unaffected |

### Dead Code Removed

| Item | Current location | Status |
|------|-----------------|--------|
| `ProjectionRegistration.ClearAsync()` | `ProjectionManager.cs` inner class | Not called in the current rebuild flow; removed in this redesign |
| `FileSystemProjectionStore.ClearProjectionFiles()` | `FileSystemProjectionStore.cs` | Called only by `ClearAsync` above; removed |
| `FileSystemProjectionStore.DeleteAllIndicesAsync()` | `FileSystemProjectionStore.cs` | Called only by `ClearAsync` above; removed |

---

*End of architecture document.*
