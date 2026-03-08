# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- **Write-through projection rebuild (Phase 2 — Scalable Projection Rebuild).**
  During a projection rebuild, each `SaveAsync` call now writes the projection state
  directly to the temporary directory on disk instead of accumulating all states in an
  in-memory buffer (`_rebuildStateBuffer`). This bounds peak memory during rebuild to
  `O(batch_size × state_size)` regardless of how many unique projection keys exist,
  eliminating the previous `O(unique_keys × state_size)` memory requirement that caused
  out-of-memory failures at scale (e.g. 10 GB heap for 1M keys × 10 KB state).

  `GetAsync` and `DeleteAsync` now read from / delete in the temp directory during rebuild.
  `CommitRebuildAsync` only writes tag index files (in parallel) and performs the atomic
  directory swap — no state buffer flush is needed.

  Trade-off: a key updated by N events is now written N times during rebuild (instead of
  once at commit). This increases total I/O but distributes it over time and eliminates
  memory pressure. Rebuild is a rare operation where durability and bounded memory outweigh
  I/O minimisation.

- **Tag accumulator replaces per-key tag index writes at commit.**
  During rebuild, tag-to-key mappings are accumulated in a lightweight in-memory dictionary
  (`_tagAccumulator`) and written in a single parallel pass at commit time. This replaces
  the previous approach of calling `AddProjectionAsync` per key per tag, which caused
  O(keys × tags) sequential file round-trips.

### Removed

- **`_rebuildStateBuffer` eliminated from `FileSystemProjectionStore`.**
  The in-memory `Dictionary<string, TState?>` that held all projection states during rebuild
  has been removed. This was the primary source of unbounded memory growth.

- **`DeleteAllIndicesAsync()` and `ClearProjectionFiles()` removed from `FileSystemProjectionStore`.**
  These methods were only used by the now-removed `ClearAsync` on `ProjectionRegistration`
  and are no longer needed with the write-through rebuild approach.

- **`ClearAsync` removed from `ProjectionRegistration` abstract class.**
  Dead code — was not called from any rebuild path after Phase 1 separation.

### Fixed

- **Admin endpoint returns 404 (not 500) for non-existent projection rebuild.**
  The `/admin/projections/{name}/rebuild` endpoint now correctly returns `NotFound` when
  the projection name is not registered, instead of returning `InternalServerError`.

### Added

- **`RebuildBatchSize` option on `ProjectionOptions`** (default: 5 000).
  Controls how many events are loaded from disk per round-trip during a projection rebuild.
  Lower values reduce peak heap usage at the cost of more index reads per rebuild; higher
  values reduce I/O round-trips at the cost of more memory.
  Typical guidance: 1 000–5 000 for memory-constrained environments, 10 000–50 000 for
  high-memory / NVMe setups.

- **`maxCount` parameter on `IEventStore.ReadAsync`** (optional, default `null` = no limit).
  Limits how many events are returned in a single call. Combined with the existing
  `fromPosition` parameter this enables page-by-page iteration over large result sets
  without loading all events into memory at once.

- **Per-batch progress logging during projection rebuild.**
  After each batch the projection manager logs an `Information`-level message showing the
  projection name, total events processed so far, current throughput (events/second), and
  elapsed wall-clock time:
  ```
  Rebuilding 'StudentDetails': 5000 events processed (2341 events/s, elapsed 00:00:02.135)
  Rebuilding 'StudentDetails': 10000 events processed (2498 events/s, elapsed 00:00:04.003)
  …
  ```
  This gives developers a live view of rebuild progress and makes it easy to detect stalls.

### Fixed

- **Post-rebuild daemon thrashing: `UnauthorizedAccessException` on Windows after sparse-projection rebuild.**
  After rebuilding a sparse projection (one whose last relevant event sits at a much lower
  global position than the store head), the checkpoint was saved at the last *relevant* event
  position rather than the store head. The daemon's next tick called
  `SaveCheckpointAsync` once per batch of irrelevant events until it caught up — e.g. 82
  rapid `File.Move` calls in succession for a Medium dataset where CourseBookCatalog's last
  event was at position 5 600 and the store head was at position 86 648.
  On Windows, `MoveFileEx` with `MOVEFILE_REPLACE_EXISTING` fails with
  `UnauthorizedAccessException` when the OS has not yet released the destination handle
  from the previous atomic rename.

  The fix: `RebuildProjectionCoreAsync` now reads the store head *before* the rebuild loop
  starts and sets the final checkpoint to `Math.Max(storeHead, lastRelevantEventPosition)`.
  After rebuild the daemon finds nothing to do for the sparse projection and only resumes
  when genuinely new events arrive.

- **Rebuild loop never terminates / `CommitRebuildAsync` unreachable.**
  The batched rebuild used `while (true)` with an inner `if (batch.Length == 0) break`,
  making the termination guarantee non-obvious and `CommitRebuildAsync` appear unreachable
  to readers. Refactored to the standard "prime the pump" pattern:
  ```csharp
  var batch = await ReadAsync(...);
  while (batch.Length > 0)
  {
      // process batch
      batch = await ReadAsync(...);  // next page
  }
  await CommitRebuildAsync(...);     // always reached
  ```

  Previously `RebuildProjectionCoreAsync` loaded _all_ events matching a projection's
  event types into a single in-memory array before starting to process them. On a "Large"
  seed (≈ 2.7 M events) this caused out-of-memory failures for even a single projection
  type. Events are now read in batches of `RebuildBatchSize` using the `fromPosition` /
  `maxCount` pagination mechanism so that peak memory is bounded by
  `batchSize × avg-event-size` instead of `total-events × avg-event-size`.

- **Projection rebuild now keeps old files accessible during rebuild.**
  Previously, `ClearAsync` deleted all projection files at the _start_ of a rebuild,
  leaving the projection directory empty for the entire rebuild duration. With large
  datasets this could mean hours with no projection data on disk.
  
  The rebuild now uses a temporary directory: all new projection files are written there
  while the old files in the production directory remain untouched. At the very end of
  `CommitRebuildAsync` an atomic directory swap (delete old, move temp to production)
  makes the new data visible instantaneously. Each projection in a parallel rebuild
  therefore becomes available as soon as _its own_ rebuild completes, independently of
  the other projections.

---

## [0.4.0-preview.3] - 2026-03-04

### Removed

- **Sample — `CourseBookOrderHistory` projection removed** (`CourseBookOrderHistory/`).
  The projection used `evt.Position.ToString()` as its key selector, creating one
  projection file per purchase event (O(Events) cardinality). On a Large-seeded database
  this produced 700,000+ projection files and never completed rebuilding. The unfiltered
  query path additionally called `GetAllAsync()`, which would have loaded all of those
  files into memory at once. The concept is fully covered by the existing
  `StudentPurchasedBooksProjection` (O(Students)). The `CourseBookOrderSortField` enum
  in `Shared/SortOrder.cs` was also removed as it was only used by this feature.
  See `docs/lessons-learned/course-book-order-history-projection-mistake.md` for the
  full post-mortem and the **projection key cardinality rule** to apply when designing
  new persisted projections.

### Fixed

- **`UpdateAsync` correctness bug — duplicate event application and checkpoint regression.**
  `ProjectionManager.UpdateAsync` now reads each projection's checkpoint *inside* the
  per-projection lock and filters incoming events by **both event type and position**
  (`e.Position > checkpoint`). Previously, `ProcessNewEventsAsync` read events from the
  global `minCheckpoint` (the lowest across all projections) and `UpdateAsync` applied
  them to *every* projection without a position guard. Any projection whose checkpoint
  was above `minCheckpoint` (e.g. because a peer projection had just been rebuilt with
  a sparse checkpoint) would silently receive already-processed events, corrupting its
  state and causing checkpoint regression. The regression then dragged `minCheckpoint`
  lower on the next tick, feeding a cascade of re-applications. The checkpoint is now
  always advanced to `batchMax` (not `relevantEvents.Max()`) so sparse projections never
  stall the global frontier. The lock is acquired for both the apply path and the
  checkpoint-advance path to prevent races with concurrent rebuilds.

- `RebuildAsync(projectionName)` and `RebuildAllAsync` no longer leave a projection in
  the "never rebuilt" state when the event store is completely empty. Previously the
  checkpoint file was not written when `ReadLastAsync(Query.All())` returned `null`,
  so the projection's checkpoint remained 0 (no file) both before and after the call —
  making the rebuild invisible and causing the daemon's startup auto-rebuild to re-queue
  the projection on every restart. Now the checkpoint file is always written after a
  rebuild (position 0 for an empty store). `RebuildAllAsync(forceRebuild: false)` uses
  `File.Exists` on the checkpoint file instead of `checkpoint == 0` to distinguish
  "rebuilt but store was empty" (file present) from "truly never rebuilt" (file absent).

---

## [0.4.0-preview.2] - 2026-03-03

### Added

- `IEventStore.ReadLastAsync(Query, CancellationToken)` — returns the single
  highest-position matching event in O(1) file reads (one index lookup + one file read).
  Scales from 100 to 10,000 events in 799–1,105 μs; 192× faster than a full `ReadAsync`
  at 10K events. Designed for consecutive-sequence patterns (e.g. invoice numbering).
- N-ary `BuildDecisionModelAsync` overload — accepts
  `IReadOnlyList<IDecisionProjection<TState>>` for runtime-variable lists of homogeneous
  projections (e.g. shopping-cart price validation spanning an arbitrary number of items
  in a single event-store read with one `AppendCondition` covering all).
- `TimeProvider` constructor overload on `DecisionProjection<TState>` — enables fully
  deterministic unit testing of time-dependent projections without sleeping; inject
  `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`.
- **Sample — Opt-In Token** (`ExamRegistration/`): server-generated single-use exam
  registration tokens (issue / redeem / revoke). Event store as token registry — no
  separate token table needed. Single `ExamTokenStatus` enum projection replaces
  two-bool pattern and handles revocation as a third state.
- **Sample — Dynamic Product Price, all three features** (`CourseBookPurchase/`):
  Feature 1 — current price without grace period;
  Feature 2 — configurable grace period with `TimeProvider` for testability;
  Feature 3 — shopping-cart N-ary overload (single read + one `AppendCondition` spanning
  all items; a concurrent price change for any book invalidates the entire order).
- **Sample — Prevent Record Duplication** (`CourseAnnouncement/`): client-generated
  idempotency token; two-projection `BuildDecisionModelAsync`; token freed on retraction
  so the event fold handles reuse automatically — no changes to the post handler needed.
- **Sample — Invoice Number / Consecutive Sequence** (`InvoiceCreation/`): gap-free
  invoice numbering with `ReadLastAsync` + `AppendCondition`; bootstrap race closed via
  `AfterSequencePosition = null` on the very first invoice.
- **Sample — Event-Sourced Aggregate** (`CourseAggregate/`): DCB tag-scoped
  `AppendCondition` in repository replaces the traditional named-stream lock; same
  `CourseCreatedEvent` / `StudentEnrolledToCourseEvent` events shared with the Decision
  Model endpoints; side-by-side comparison table in README.
- **Sample — `CourseBuyersProjection`**: read model tracking every student who purchased
  each course book.
- **Sample — Paging**: `PaginatedResponse<T>` returned from list endpoints; `SortOrder`
  enum for ascending/descending control.
- **Data Seeder** (`Opossum.Samples.DataSeeder`): standalone CLI tool that populates the
  `CourseManagement` sample with realistic data across all scenarios; includes a
  progress bar and session-based seeding phases.

### Fixed

- **Zero-allocation hot path restored in incremental projection updates** — a dead
  `GetCheckpointAsync` call was executing a redundant file read on every incremental
  update, and no in-memory checkpoint cache existed. Both issues fixed: 55–65 % speedup
  confirmed; **0 B allocated per incremental update** in rerun1 (was 11–16 KB). See
  `docs/benchmarking/results/20260303/rerun1/` for verification.
- **Large no-flush batch regression resolved** — batch-50 append: −13 %, batch-100
  append: −12 % vs 0.4.0-preview.1. Open item from the 20260226 analysis fully closed.
- **Projection rebuild admin endpoint** — now correctly targets only the named
  projections supplied; previously rebuilt all projections regardless of input.
- Test flakiness in concurrent-append integration tests.

### Performance (benchmark baseline 2026-03-03, rerun1 verified)

- **Incremental projection update**: 3.68 μs / **0 B** (1 new event),
  4.52 μs / **0 B** (10 new events) — zero-allocation hot path verified in rerun1.
- **`ReadLastAsync`**: 799 μs (100 events) → 1,105 μs (10,000 events) — near-O(1)
  confirmed; 192× faster than full `ReadAsync` at 10K.
- Single-event flush append: 16.4 ms → **~61 events/sec** (−5 % vs preview.1).
- Batch-10 flush append: 124 ms / 10 = 12.4 ms/event → **~81 events/sec** (−3 %).
- High-selectivity tag query: 553 μs → **~501 μs** (−9 %).
- Full benchmark report: `docs/benchmarking/results/20260303/`
