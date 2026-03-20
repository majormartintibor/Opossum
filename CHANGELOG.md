# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Production feasibility analysis** (`docs/analysis/production-feasibility-analysis.md`): Deep analysis of .NET file system API limits with micro-benchmarks, competitive landscape assessment, and identification of the viable production niche (embedded event sourcing for desktop/tablet applications).
- **GDPR and encryption design document** (`docs/design/gdpr-and-encryption-design.md`): Complete design for crypto shredding (per-subject encryption keys with AES-256-GCM), soft delete (tombstone event replacement), and forgettable payload analysis. Covers key management, serialization integration, erasure API, and key rotation.
- **Throughput optimization plan** (`docs/design/throughput-optimization-plan.md`): Detailed implementation plan for Options A (append-only indices), B (in-memory cache), and E (implicit ledger) with measured performance expectations.
- **0.6.0 roadmap rewrite** (`docs/future-plans/0.6.0-roadmap.md`): Complete rewrite with three-phase plan (GDPR Foundation, Encryption at Rest, Throughput), detailed session planning guide, dependency graph, and ~22-27 session estimate.

### Changed
- **`use-cases.md` — complete rewrite for honesty and accuracy:**
  - Removed false "Production Validated" and "Proven Use Cases" claims — Opossum has never been deployed in production.
  - Removed fabricated compliance section (GDPR per-event deletion, SOX cryptographic verification, HIPAA, FDA digital signatures, PCI encryption) — Opossum has no built-in encryption, no digital signatures, no per-event deletion, and no access control beyond OS file permissions.
  - Removed IoT Edge Gateway use case — 100+ sensors exceed throughput limits (~55 durable events/sec); "low latency" claim was false at 18 ms/event; no sync mechanism exists.
  - Removed "Architectural Patterns" section describing sync mechanisms (hybrid cloud, offline-first with eventual sync) that do not exist in the library.
  - Removed "Migration Guide" section — speculative, not based on any actual migration.
  - Removed unverifiable storage size comparison (EventStoreDB, Marten) with no backing benchmark data.
  - Reframed Car Dealership scenario from "Production Validated" to "Recommended" based on architecture analysis (low volume fits well, but unproven).
  - Reframed Factory scenario from "Robot Communication System" to "Production Logging" — Opossum is too slow for real-time robot coordination (~18 ms/event), but fits as an audit log for discrete production events.
  - Reframed Desktop Software use case with honest limitations (no branching, directory-based storage not portable as a single file).
  - Added "Prototyping and Learning Event Sourcing" as a legitimate use case.
  - Added explicit "What Opossum does NOT provide" section in the executive summary.
  - Expanded "When NOT to Use Opossum" with encryption/compliance, IoT, and per-event deletion entries.
  - Changed document status from "Production Validated" to "Pre-production (sample application only)".
  - All performance numbers now reference actual 2026-03-11 BenchmarkDotNet results.
  - Docfx copy synced to `docs/docfx/articles/guides/use-cases.md`.
- **README.md — honesty review:**
  - Replaced "Perfect Use Cases" heading with "Recommended Use Cases".
  - Removed "Field service applications — sync when connected" (no sync mechanism exists).
  - Removed "Compliance-heavy industries" from recommended use cases (no built-in encryption or compliance features).
  - Added "Desktop tools with event-sourced state" and "Environments where databases are banned" as recommended use cases.
  - Added encryption/compliance and per-event deletion entries to "When NOT to Use Opossum" table.
  - Softened rule of thumb to include encryption caveat.
  - Changed "Built for real-world use cases in automotive retail" acknowledgment to honest description.
  - Changed "Data residency requirements (legal/compliance)" to "Data residency requirements (data stays on local disk)".

### Added
- DocFX documentation website scaffolded under `docs/docfx/` with full article hierarchy (Getting Started, Concepts, Guides, Architecture Decisions) and auto-generated API reference from XML doc comments.
- GitHub Actions workflow (`.github/workflows/docs.yml`) that builds and deploys the site to GitHub Pages on every push to `master`.
- Documentation site live at `https://majormartintibor.github.io/Opossum/`.
- Docs badge added to `README.md`.
- `PackageProjectUrl` in `Opossum.csproj` updated to point to the documentation site.

### Fixed
- **`mediator.md` — complete rewrite:** removed fabricated `IMessageHandler<TMessage, TResponse>` generic interface (does not exist in the library). Documentation now correctly describes the convention-based handler discovery (class name ending with `Handler` or `[MessageHandler]` attribute), method-parameter DI injection, `CommandResult` return type, and the `ExecuteDecisionAsync` / `BuildDecisionModelAsync` patterns used in the sample app.
- **`mediator.md`:** replaced MVC controller dispatch example with minimal API endpoint pattern matching the sample app.
- **`mediator.md`:** added correct query handler example using `IProjectionStore<T>` via method parameters.
- **`quick-start.md`:** fixed `StudentRegisteredEvent` constructor from `(Guid, string, string)` to actual `(Guid, string, string, string)` with `FirstName`/`LastName`/`Email` parameters.
- **`quick-start.md`:** fixed `StudentView` projection record to use `FirstName`/`LastName` matching the actual event type.
- **`quick-start.md`:** updated Step 6 (DCB example) to use `CommandResult` return type and `AppendCondition` pattern matching the actual `RegisterStudentCommandHandler`.
- **`quick-start.md`:** fixed `AddProjections` registration to show chained call pattern.
- **`event-store.md`:** replaced raw `NewEvent { Event = new DomainEvent { ... } }` append example with the fluent builder pattern (`ToDomainEvent().WithTag().WithTimestamp()`) that's actually used in the codebase.
- **`event-store.md`:** added convenience extension method examples for `ReadAsync` and single-event `AppendAsync`.
- **`installation.md`:** corrected `IEventStoreAdmin` description from "Administrative operations (tag migration, etc.)" to "Destructive admin operations (`DeleteStoreAsync`)" — tag migration is on `IEventStoreMaintenance`.
- **`installation.md`:** updated DI registration example to show chained `.AddOpossum().AddMediator().AddProjections()` pattern.
- **`configuration.md`:** replaced `Contexts` array with `StoreName` string in appsettings.json example and options table — the multi-context model was removed in favour of single-store per instance.
- **`configuration-validation.md`:** replaced all `Contexts` references with `StoreName` in examples, error messages, and validation rules table.
- **`use-cases.md`:** fixed all projection `Apply` method signatures from `Apply(TState?, IEvent)` to the correct `Apply(TState?, SequencedEvent)` with `evt.Event.Event switch` pattern matching.
- **`use-cases.md`:** replaced `new Tag { Key = "...", Value = "..." }` object initializer syntax with `new Tag("key", "value")` positional record constructor throughout.
- **`use-cases.md`:** fixed `QueryByTagsAsync(new[] { ... })` calls to use collection expression syntax.
- **`use-cases.md`:** fixed `ReadAsync` calls to use correct parameter names and `Query.FromTags` syntax.
- **`use-cases.md`:** added missing `ProjectionName`, `EventTypes`, and `KeySelector` members to projection examples.
- **Source docs synced:** applied identical fixes to `docs/configuration-guide.md`, `docs/configuration-validation.md`, and `docs/guides/use-cases.md` (source files for the docfx copies).
- `installation.md`: corrected target framework claim from `.NET 8` to `.NET 10`.
- `installation.md`: split "What gets registered" table by extension method (`AddOpossum`, `AddProjections`, `AddMediator`) and corrected `IProjectionStore` to its actual generic form `IProjectionStore<TState>`.
- `quick-start.md`: fixed two append calls where `DomainEventBuilder` was incorrectly wrapped in `new NewEvent { Event = ... }` — the builder carries an implicit conversion to `NewEvent` and must be used directly.
- `quick-start.md`: fixed `IProjectionStore` usage to the correct generic form `IProjectionStore<StudentView>` with the correct `GetAsync(key)` signature.
- `mediator.md`: fixed `options.ScanAssembly()` (method does not exist on `MediatorOptions`) to `options.Assemblies.Add()`.
- `mediator.md`: same `DomainEventBuilder` append fix applied to the handler code example.
- **README ↔ docfx contradiction fixes (cross-documentation review):**
  - **README Section 7:** replaced non-generic `IProjectionStore` with `GetAsync<T>("name", key)` with the actual generic `IProjectionStore<StudentDetails>` using `GetAsync(key)`.
  - **README Core Concepts Projection:** added missing `ProjectionName`, `EventTypes`, and `KeySelector` members required by `IProjectionDefinition<T>`.
  - **README API Reference `IEventStore`:** added missing `maxCount` parameter to `ReadAsync` signature; removed incorrect `= null` default from `AppendAsync` condition parameter.
  - **README disk layout:** changed `Context name` comment to `Store name`; added `Projections\` directory (capital P) matching actual code.
  - **README:** replaced `new[]` with collection expression and `!= null` with `is not null` for style consistency.
  - **`quick-start.md` disk layout:** corrected `events/` → `Events/`, `index/event-types/` → `Indices/EventType/`, `tags/` → `Indices/Tags/`, `ledger.json` → `.ledger`, `position_1.json` → `0000000001.json`, `projections/` → `Projections/` — all matching actual `StorageInitializer` path constants.
  - **`event-store.md` disk layout:** applied same directory naming corrections.
  - **`mediator.md` enrollment handler:** added missing `studentLimit is null` and `studentLimit.IsAtLimit` checks to match actual `EnrollStudentToCourseCommandHandler` source and README.
- `mediator.md`: fixed `GetStudentHandler` to use `IProjectionStore<StudentView>` with the correct `GetAsync(key)` signature.
- `mediator.md`: corrected the claim that handlers are registered as transient DI services — they are discovered by reflection at startup and stored in the mediator's internal handler map.

---

## [0.5.0-preview.1] - 2026-03-11

### Fixed

- **`LogReadError` nullable `StoreName` guard.**
  a nullable `string?` to a non-nullable `string` parameter. Applied the same
  `?? string.Empty` guard already used by `LogAppendError`.

- **Corrupt ledger no longer silently resets sequence positions to zero.**
  `LedgerManager.GetLastSequencePositionAsync` previously caught `JsonException` and
  returned `0`, causing the next `AppendAsync` to allocate positions starting at 1 and
  silently overwrite every committed event file. The fix replaces the silent `return 0`
  with an explicit `InvalidOperationException` that includes the ledger file path and
  actionable recovery guidance. This transforms silent catastrophic data loss into a loud,
  actionable failure.

- **Crash-recovery position collision — orphaned event files are no longer silently overwritten.**
  A process crash between writing event files (step 7) and updating the ledger (step 9)
  previously left orphaned files on disk. On the next `AppendAsync`, the stale ledger
  allocated the same positions and `WriteEventAsync` overwrote the orphans — even with
  `WriteProtectEventFiles = true`. The fix makes `WriteEventAsync` idempotent (skip if
  destination exists) and adds `LedgerManager.ReconcileLedgerAsync` which, on the first
  append after startup, scans the events directory and advances the ledger to match the
  actual highest on-disk position. See
  [limitation doc](docs/limitations/crash-recovery-position-collision.md).

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

- **Rebuild orchestration extracted from `ProjectionManager` into dedicated `ProjectionRebuilder` (Phase 1 — Architectural Separation).**
  All rebuild logic (event replay loop, checkpoint management, parallel coordination) now
  lives in a new `ProjectionRebuilder` class behind the `IProjectionRebuilder` interface.
  `ProjectionManager` is now solely responsible for live event processing and projection
  registration. `ProjectionDaemon` injects `IProjectionRebuilder` for rebuild operations.
  This separation makes each concern independently testable and easier to reason about.

- **No aggregated metadata index written during rebuild (Phase 4 — Metadata Index Decoupling).**
  `CommitRebuildAsync` no longer calls `_metadataIndex` at all. Post-rebuild reads are
  served from per-file embedded metadata, eliminating the O(unique_keys) JSON blob that
  was written to `Metadata/index.json` at commit time. The lazy metadata index handles
  missing `index.json` gracefully on first read after rebuild.

- **`ProjectionOptions.EnableAutoRebuild` (bool) replaced by `AutoRebuild` (`AutoRebuildMode` enum) — ⚠️ breaking change.**
  The boolean only supported two states (on/off). The new enum adds a third mode:
  - `None` — daemon starts without triggering any rebuild (was `false`)
  - `MissingCheckpointsOnly` — only projections with absent checkpoint files are rebuilt
    on startup (was `true`; this remains the default)
  - `ForceFullRebuild` — all projections are rebuilt from scratch on every startup
    (new; useful for development iteration and post-migration scenarios)

  Crash recovery (`ResumeInterruptedRebuildsAsync`) now runs unconditionally on daemon
  startup, regardless of the `AutoRebuild` mode. Previously it was gated behind
  `EnableAutoRebuild = true`, which meant manually-triggered rebuilds interrupted by a
  crash were not resumed when auto-rebuild was disabled.

  **Migration:** replace `"EnableAutoRebuild": true/false` in `appsettings.json` with
  `"AutoRebuild": "MissingCheckpointsOnly"` / `"AutoRebuild": "None"`.
  In code: `options.EnableAutoRebuild = true` → `options.AutoRebuild = AutoRebuildMode.MissingCheckpointsOnly`.

### Removed

- **`_rebuildStateBuffer` eliminated from `FileSystemProjectionStore`.**
  The in-memory `Dictionary<string, TState?>` that held all projection states during rebuild
  has been removed. This was the primary source of unbounded memory growth.

- **`DeleteAllIndicesAsync()` and `ClearProjectionFiles()` removed from `FileSystemProjectionStore`.**
  These methods were only used by the now-removed `ClearAsync` on `ProjectionRegistration`
  and are no longer needed with the write-through rebuild approach.

- **`ClearAsync` removed from `ProjectionRegistration` abstract class.**
  Dead code — was not called from any rebuild path after Phase 1 separation.

- **Rebuild methods removed from `IProjectionManager` — ⚠️ breaking change (Phase 1).**
  `RebuildProjectionAsync`, `RebuildAllAsync`, `RebuildMissingProjectionsAsync`, and
  `ForceRebuildAllAsync` are no longer part of the `IProjectionManager` contract. Callers
  must now inject `IProjectionRebuilder` instead. This is acceptable because Opossum is
  pre-1.0; the change produces a cleaner API surface and enables independent evolution of
  the rebuild subsystem.

### Fixed

- **Admin endpoint returns 404 (not 500) for non-existent projection rebuild.**
  The `/admin/projections/{name}/rebuild` endpoint now correctly returns `NotFound` when
  the projection name is not registered, instead of returning `InternalServerError`.

### Added

- **`IProjectionRebuilder` interface and `ProjectionRebuilder` implementation (Phase 1 — Architectural Separation).**
  Dedicated rebuild orchestrator registered in DI via `AddProjectionRebuilder()`. Exposes
  `RebuildProjectionAsync`, `RebuildAllAsync`, `RebuildMissingProjectionsAsync`, and
  `ForceRebuildAllAsync`. Injected by `ProjectionDaemon` and available to application code
  (e.g. admin endpoints) for on-demand rebuilds.

- **`AutoRebuildMode` enum** (`None`, `MissingCheckpointsOnly`, `ForceFullRebuild`).
  Replaces the boolean `EnableAutoRebuild` property on `ProjectionOptions` with a
  three-valued enum that unlocks force-rebuild-on-every-startup for development workflows.

- **`RebuildFlushInterval` option on `ProjectionOptions`** (default: 10 000; range: 100–1 000 000).
  Controls how many events are processed between rebuild journal flushes. After every
  `RebuildFlushInterval` events the rebuilder persists a journal checkpoint and the current
  tag accumulator to disk. If the process crashes, at most this many events need
  re-processing. Lower values increase durability at the cost of more journal I/O.

- **Crash-recovery rebuild journal (Phase 3 — Rebuild Journal and Crash Recovery).**
  During rebuild, a `*.rebuild.json` journal file is persisted to the checkpoint directory
  every `RebuildFlushInterval` events. The journal records the projection name, temp
  directory path, last-processed event position, and total events processed. On crash, the
  journal allows the rebuild to resume from the last flush point instead of starting over.
  The journal is deleted on successful rebuild completion.

- **`ResumeInterruptedRebuildsAsync` — automatic resume of interrupted rebuilds on startup (Phase 3).**
  On daemon startup, the rebuilder scans for `*.rebuild.json` journal files. If the matching
  temp directory still exists and the projection is registered, the rebuild resumes from the
  journal's last-flushed position. If the temp directory is missing or the projection is
  unregistered, the journal is discarded. This runs before `RebuildMissingProjectionsAsync`.

- **Orphaned temp directory cleanup on startup (Phase 3).**
  `CleanOrphanedTempDirectoriesAsync` scans the projections path for `*.tmp.*` directories
  with no matching journal file and deletes them, preventing disk space leaks from
  interrupted rebuilds where the journal was cleaned up but the temp directory was not.

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

- **Stale metadata cache after rebuild (Phase 4 — Metadata Index Decoupling).**
  Added `ClearCache()` to `ProjectionMetadataIndex`, called from `CommitRebuildAsync`'s
  `finally` block. Without this, `GetAsync` could return pre-rebuild version/timestamp
  data from the in-memory cache after the production directory had been swapped to the
  freshly rebuilt files. The cache is now invalidated so the next read loads metadata
  from the new per-file data on disk.

### Performance (benchmark baseline 2026-03-11)

- **Parallel projection rebuild: ~6–10× slower vs 0.4.0 baseline — expected, by design.**
  The write-through projection rebuild (Phase 2) writes each `SaveAsync` call directly
  to disk during rebuild instead of buffering in memory. For a large dataset, total disk
  I/O is O(N events × updates_per_key) instead of O(unique_keys) at commit time. At the
  parallel benchmark's scale (four projections, ~3.5× larger dataset than the 0.4.0
  baseline), sequential rebuild takes ~3.7 s vs ~381 ms; Concurrency=4 takes ~2.0 s.
  This is the correct trade-off: the alternative was unbounded memory growth (10 GB+ for
  1 M unique keys × 10 KB state) and unrecoverable OOM failures on large datasets.
  The Concurrency=4 benefit is stronger than before (~47 % faster than sequential, up
  from ~7 %), because write-through I/O parallelises better than in-memory commit
  serialisation.
- **Incremental projection update: ~4.6 μs / 0 B** (1 new event), ~4.8 μs / 0 B
  (10 new events) — **2× faster and zero-allocation** vs the 0.4.0-preview.2 baseline
  (10.4 μs / 11.8 KB). The in-memory checkpoint cache fix is confirmed in this first
  full benchmark run since the fix was applied.
- **All core paths stable:** append, read, query, ReadLast, and descending-order
  benchmarks are all within ±9 % of the 0.4.0-preview.2 baseline (within expected
  InvocationCount=1 noise). No regressions introduced in core I/O paths.
- Full benchmark report: `docs/benchmarking/results/20260311/`

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
