# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Performance
- **Parallel event-type index loading** (`IndexManager`): `GetPositionsByEventTypesAsync` and
  `GetPositionsByTagsAsync` now load all per-type/per-tag index files concurrently via
  `Task.WhenAll` instead of sequentially. Multi-type and multi-tag queries scale with
  `max(T_file)` rather than `sum(T_file)`.
- **Reduced read-retry overhead** (`PositionIndexFile`): `ReadPositionsAsync` now uses
  `maxRetries = 3` with a `1 ms` initial back-off (was 5 retries / 10 ms). Reads are
  non-destructive so a shorter back-off is sufficient, reducing worst-case retry latency by ~10×.
- **Eliminated redundant `File.Exists` syscall** (`EventTypeIndex`, `TagIndex`):
  `GetPositionsAsync` no longer calls `File.Exists` before delegating to
  `PositionIndexFile.ReadPositionsAsync`, which already handles the missing-file case — saving
  one kernel call per index file read.
- **K-way sorted merge** (`IndexManager`): multi-type and multi-tag position merges now use a
  k-way sorted merge that exploits pre-sorted index arrays, replacing the `HashSet<long>` +
  `Array.Sort` approach (O(N log N) → O(N × K)).

### Added
- **Cross-process append safety (ADR-005)** — `AppendAsync` is now safe when multiple
  application instances share the same store directory over a network drive or UNC path.
  A dedicated `.store.lock` file in the context directory is opened with `FileShare.None`
  for the entire duration of every append. Windows SMB enforces this server-side across all
  machines, eliminating the read-check-write race that could silently overwrite events when
  two PCs submitted a form within the same ~10 ms window. The existing `SemaphoreSlim` is
  retained as a fast within-process gate; the file lock is only contested across processes.
  Lock acquisition on an uncontested local drive adds < 1 ms overhead per append.
- **`CrossProcessLockManager`** — new internal class that acquires and releases the
  `.store.lock` file with exponential backoff (10 ms → 500 ms cap) on sharing violations.
  Throws `TimeoutException` with a diagnostic message when the configured timeout elapses.
  Throws `OperationCanceledException` immediately when the caller's token is cancelled.
- **`OpossumOptions.CrossProcessLockTimeout`** — new configuration property (default: 5 s).
  Increase this value when appends are consistently queued behind large batch operations on
  a slow network share. Validated at startup: must be > `TimeSpan.Zero`.
- **`TimeoutException` documented on `IEventStore.AppendAsync`** — the XML comment now
  describes when and why `TimeoutException` can surface, and references the configuration
  option to adjust.
- **`CrossProcessLockManagerTests`** (8 unit tests) — cover: successful acquisition, lock
  file is created even when the context directory does not yet exist, second acquisition while
  held throws `TimeoutException`, disposal releases the lock for re-acquisition, pre-cancelled
  token throws immediately, mid-wait cancellation throws `OperationCanceledException`, and
  exponential backoff stays within the configured timeout + max-backoff bounds.
- **`CrossProcessAppendSafetyTests`** (5 integration tests) — cover: two store instances on
  the same directory producing contiguous positions across 100 concurrent appends, no event
  payload overwritten, exactly one winner under competing `AppendCondition`, `TimeoutException`
  thrown when the lock is externally held for longer than `CrossProcessLockTimeout`, and
  100 sequential single-instance appends completing within 5 s (performance sanity guard).
- **`IEventStoreAdmin` interface with `DeleteStoreAsync`** — new public administrative interface
  that exposes destructive store-lifecycle operations. `DeleteStoreAsync` permanently removes
  all data owned by the store: events, indices, projections, checkpoints, and the ledger.
  Write-protected files (`WriteProtectEventFiles`, `WriteProtectProjectionFiles`) are handled
  transparently — the read-only attribute is stripped before deletion so no
  `UnauthorizedAccessException` is thrown. After the call, the store directory no longer
  exists; subsequent `AppendAsync`/`ReadAsync` calls recreate the required directory structure
  automatically. Registered in DI alongside `IEventStore` (same singleton instance).
- **`DELETE /admin/store?confirm=true` endpoint in sample app** — `AdminEndpoints.MapStoreAdminEndpoints`
  maps a delete endpoint for the whole event store. The `confirm=true` query parameter is
  required to prevent accidental erasure (omitting it or passing `confirm=false` returns HTTP
  400). A successful deletion returns HTTP 204 No Content. The endpoint is idempotent: calling
  it on an already-absent store also returns 204.
- **`EventStoreAdminTests`** — unit test class covering `DeleteStoreAsync`: basic deletion,
  graceful no-op when the store directory is absent, transparent bypass of write-protected
  event files, transparent bypass of write-protected projection files, store recreation after
  deletion, and `InvalidOperationException` when no store is configured.
- **`StoreAdminEndpointTests`** — integration test class for the `DELETE /admin/store` endpoint:
  missing `confirm`, `confirm=false`, and `confirm=true` (HTTP status codes), store-directory
  deletion verified on disk, store-recreation after deletion verified via a subsequent append,
  and idempotent double-deletion.
- **`OpossumOptions.WriteProtectProjectionFiles`** — new option (default: `true`) that marks
  projection files read-only at the OS level immediately after they are written to disk.
  Human operators can open and read the JSON files in any text editor, but cannot accidentally
  modify or delete them. Opossum transparently removes the read-only attribute before
  overwriting or deleting a projection file internally, then re-applies protection afterward.
  This mirrors the existing `WriteProtectEventFiles` behavior and satisfies the same
  "human-readable but immutable" requirement for the derived projection store.
- **`TestDirectoryHelper.ForceDelete`** — shared test utility in both `Opossum.UnitTests` and
  `Opossum.IntegrationTests` that strips all read-only attributes from files recursively before
  deleting a temp directory. Used in all test `Dispose()` methods that clean up temp stores
  created with write-protection enabled.

### Changed
- **`WriteProtectEventFiles` default changed from `true` to `false`** — write protection is
  now opt-in. Development environments can delete store files freely without clearing
  read-only attributes. Enable explicitly in production: `options.WriteProtectEventFiles = true`.
- **`WriteProtectProjectionFiles` default changed from `true` to `false`** — same rationale.
  Enable in production: `options.WriteProtectProjectionFiles = true`.
- **Event files are now pretty-printed JSON** — `JsonEventSerializer` switched from minified
  single-line JSON to indented multi-line JSON (`WriteIndented = true`). Event files are now
  immediately readable when opened in any text editor without any reformatting step.
- **Projection files are now pretty-printed JSON** — `FileSystemProjectionStore` likewise
  switched to `WriteIndented = true`. Both event and projection JSON files use the same
  human-friendly indented format.

### Fixed
- **Duplicate `EventStoreAdminTests` class** — the unit test file accidentally contained two
  identical class definitions, causing `CS0101`/`CS0111` compilation errors. The redundant
  second definition was removed; the first (which uses the cleaner `CreateEvent` helper) is
  the canonical version.
- **`StoreAdminEndpointTests.GetStoreName()` used stale `Opossum:Contexts` config key** —
  after the `AddContext` → `UseStore` rename in 0.3.0-preview.1, the helper still read the
  old `Opossum:Contexts` array and called `.Get<string[]>()` (an extension method unavailable
  without `Microsoft.Extensions.Configuration.Binder`), causing a `CS1061` build error.
  Updated to `config["Opossum:StoreName"]` which uses the plain `IConfiguration` indexer.
- **`IntegrationTestFixture` used stale `Opossum:Contexts` config key** — same root cause as
  above; updated to `context.Configuration["Opossum:StoreName"]`.
- **Test cleanup failures with write-protected files** — `Dispose()` methods in multiple test
  classes were calling `Directory.Delete(path, recursive: true)` directly, which throws
  `UnauthorizedAccessException` on Windows when the directory contains read-only files.
  Updated all affected `Dispose()` methods and inline `finally` blocks to use
  `TestDirectoryHelper.ForceDelete` so tests pass reliably when `WriteProtectEventFiles` or
  `WriteProtectProjectionFiles` is enabled (the default).
- **`EventFileManagerTests.CreateTestEvent` missing method declaration** — the method body was
  present in the file but the signature had been accidentally removed. Restored the signature.

## [0.3.0-preview.1] - 2026-02-23

### Fixed
- **Sample app startup crash when `Contexts` config key was used after `UseStore` refactor** —
  `appsettings.json` and `appsettings.Development.json` still used the old `Contexts: [...]`
  array. The base `appsettings.json` had `Contexts: [""]`, so any launch profile that does
  not load `appsettings.Development.json` (e.g. the Docker profile, which sets no
  `ASPNETCORE_ENVIRONMENT`) would call `options.UseStore("")`, throw `ArgumentException`
  during DI registration, and crash before the web server could bind — making Scalar UI
  unreachable and preventing projection auto-rebuild. Fixed by replacing `Contexts: [...]`
  with `StoreName: "..."` in both appsettings files and updating `Program.cs` to read
  `Opossum:StoreName` and call `UseStore` once.

### Added
- **`IEventStoreMaintenance.AddTagsAsync`** — new public interface that exposes additive-only
  tag migration. Retroactively adds tags to all stored events of a given event type; any tag
  whose key already exists on an event is silently skipped, so existing data is never modified
  or deleted. The tag index is updated atomically per-event under the append lock. Returns a
  `TagMigrationResult(TagsAdded, EventsProcessed)` summary. Registered in DI alongside
  `IEventStore` (same singleton instance).
- `TagMigrationResult` record — carries the outcome of an `AddTagsAsync` call.
- `CancellationToken` parameter on `AppendAsync` — all async operations in the public API
  now accept a `CancellationToken`.

### Changed
- **Breaking: `OpossumOptions.AddContext(string)` renamed to `UseStore(string)`** —
  `AddContext` and the `Contexts` list are removed. Call `options.UseStore("MyApp")` instead.
  `UseStore` throws `InvalidOperationException` if called more than once per options instance,
  enforcing the single-store-per-instance contract. The corresponding internal field is now
  `StoreName` (string?) instead of `Contexts` (List\<string\>).
  Migration: replace every `options.AddContext("Name")` with `options.UseStore("Name")`.
- **Breaking: `IEventStoreMaintenance.AddTagsAsync`** — removed the unused `string? context`
  parameter (single-store design makes it meaningless).
- **Breaking: `IProjectionDefinition<TState>.Apply` now receives `SequencedEvent` instead of `IEvent`** —
  the full event envelope (tags, metadata, position) is available in every `Apply` call,
  removing the asymmetry with `KeySelector(SequencedEvent)`.
- **Breaking: `IProjectionWithRelatedEvents<TState>.Apply` and `GetRelatedEventsQuery`** — both
  methods updated to accept `SequencedEvent` for consistency with the base interface.
- **Breaking: `Tag` and `QueryItem` are now immutable `record` types** — construction syntax
  changes; existing positional or property-init call sites are unaffected.
- **Breaking: `Metadata`, `DomainEvent`, and `SequencedEvent` are now immutable `record` types** —
  all properties are `init`-only; use `with` expressions to derive modified copies.

### Fixed
- Metadata mutation side-effect in `AppendAsync` — the store no longer mutates the caller's
  `NewEvent` instances while assigning derived metadata fields (`Timestamp`).

### Internal
- Extracted duplicated file I/O plumbing from `TagIndex` and `EventTypeIndex` into a shared
  `PositionIndexFile` static utility — atomic writes, retry logic, and `IndexData` now live
  in one place, eliminating the risk of durability fixes being applied to one index but not
  the other. No public API changes; `ProjectionTagIndex` is unaffected.

### Removed
- `Contexts` property and `AddContext()` method from `OpossumOptions` — replaced by
  `StoreName` property and `UseStore()` method (see Changed above).

---

## [0.2.0-preview.2] - 2026-02-22

### Fixed

- **`TotalEventsProcessed` in projection checkpoints was always wrong** — `SaveCheckpointAsync`
  previously stored `oldCheckpoint + 1` on every update after the first, meaning the value
  drifted further from reality with each polling cycle. It is now always set to the current
  `lastProcessedPosition`, which equals the total event count in a 1-indexed sequential store.

- **`StorageInitializer` created `Events/` but the store used `events/`** — On case-sensitive
  file systems (Linux) this caused a spurious empty `Events/` directory to be created at startup
  while actual event files went into the separate `events/` directory created on first write.
  The initializer now pre-creates `events/` (lower-case) consistently with `EventFileManager`.

### Added

- **`OpossumTelemetry.ActivitySourceName`** — public constant (`"Opossum"`) that consumers pass
  to `tracerProviderBuilder.AddSource(...)` to receive Opossum distributed traces in any
  OpenTelemetry-compatible pipeline. No Opossum package dependencies are required on the
  consumer side; the library emits traces purely via `System.Diagnostics.ActivitySource`.

- **Distributed tracing via `ActivitySource`** — three operations now emit activities:
  - `EventStore.Append` — tagged with `db.operation`, `opossum.event_count`, and
    `opossum.context`. On `AppendConditionFailedException` the activity is tagged with
    `opossum.append.conflict = true` (not treated as an error — conflict is expected in
    the DCB retry pattern). All other unexpected exceptions set `ActivityStatusCode.Error`.
  - `EventStore.Read` — tagged with `db.operation`, `opossum.context`, and
    `opossum.event_count` (populated after the read completes).
  - `Projection.Rebuild` — tagged with `opossum.projection` and `opossum.events_processed`.
  When no listener is attached the overhead is a single null-check per operation.

- **Structured error logging in `FileSystemEventStore`** — the event store now accepts an
  optional `ILogger<FileSystemEventStore>?` via its constructor (injected automatically by
  the DI container when `services.AddLogging()` is present; falls back to `NullLogger`
  otherwise). Unexpected I/O or serialisation exceptions in `AppendAsync` and `ReadAsync` are
  logged at `Error` level. `AppendConditionFailedException` / `ConcurrencyException` are
  intentionally **not** logged — they are part of normal DCB flow and handled by the caller.

- **Structured error logging in `Mediator`** — an optional `ILogger<Mediator>?` is now
  injected into the mediator (via the DI factory registered by `AddMediator()`). A missing
  handler is logged at `Error` level before the `InvalidOperationException` is thrown.

- **Sample app `ActivityListener` demo** — `Program.cs` registers a zero-dependency
  `ActivityListener` in development that prints every Opossum span with duration and tags to
  the console, with an inline comment showing the one-liner OpenTelemetry replacement.

### Performance

- **`[LoggerMessage]` source-generated logging in `ProjectionDaemon` and `ProjectionManager`**
  — all `_logger.LogXxx(...)` calls in both classes have been converted to
  `[LoggerMessage]`-attributed `partial` methods. The source generator produces static
  callbacks that skip boxing and string allocation entirely when the requested log level is
  disabled, which benefits the hot polling loop (`ProcessNewEventsAsync` runs on every tick).

- **O(1) event-type matching in `ProjectionManager.UpdateAsync`** — the internal
  `ProjectionRegistration<T>` now builds a `HashSet<string>` from `definition.EventTypes` at
  registration time. The hot polling loop's `Contains()` check drops from O(n) array scan to
  O(1) hash lookup. The public `IProjectionDefinition<TState>.EventTypes` API is unchanged.

- **`Path.GetInvalidFileNameChars()` cached as `static readonly`** — `TagIndex` and
  `EventTypeIndex` previously called `Path.GetInvalidFileNameChars()` on every index write,
  allocating a new `char[]` each time. Both now hold a single cached instance.

### Changed

- **`LogRebuildingProjection` downgraded from `Information` → `Debug`** — the per-projection
  "Rebuilding projection 'X'..." progress message is repeated N times per rebuild (once per
  registered projection). The completion message `"Projection 'X' rebuilt in Xms"` and the
  overall summary `"All N projections rebuilt in X"` remain at `Information` and are sufficient
  for production observability. Enable `Debug` to see individual projection rebuild progress.

- **Sample app log-level guidance** — `appsettings.json` now documents all Opossum log-level
  options and per-component overrides with inline comments. `appsettings.Development.json`
  sets `"Opossum": "Debug"` so polling and per-projection rebuild details are visible during
  local development without any extra configuration.

### Documentation

- **XML docs added to previously undocumented public types** — `IEvent`, `Tag`,
  `SequencedEvent`, `DomainEvent`, `QueryItem`, and `IEventStore.AppendAsync` now have full
  IntelliSense documentation including remarks, parameter descriptions, and exception docs.

### Benchmarks

Benchmark run `20260222` compared against the `20260212` pre-release baseline:

| Suite | 20260212 | 20260222 | Δ |
|---|---:|---:|---|
| ParallelRebuild — sequential (4 projections) | 5.51 s | 370 ms | **~15× faster** |
| ParallelRebuild — memory (sequential) | 85.9 MB | 21.5 MB | **~4× less** |
| ParallelRebuild — parallel vs sequential | 2.0× faster | ≈1.0× parity | I/O bottleneck eliminated |
| ProjectionRebuild — 250 events | 18.7 ms | 17.0 ms | −9% |
| Read — EventType scan (10K events) | 226 ms | 211 ms | −7% |
| Query — low selectivity (many matches) | 134 ms | 111 ms | −17% |
| Append — single event (no flush) | 4.54 ms | 4.76 ms | ≈ noise |

The **parallel rebuild advantage has narrowed to near-parity**: the rebuild I/O optimisation
reduced disk operations from O(events) to O(unique keys), cutting the 4-projection sequential
rebuild from 5.5 s to 370 ms and eliminating the bottleneck that previously made parallelism
valuable. Rebuilding four projections sequentially is now faster than the old *parallel* run
at 2.7 s.

The O(1) `HashSet` event-type matching and cached `Path.GetInvalidFileNameChars()` changes
target the live **projection-polling hot path** (`ProcessNewEventsAsync`); their benefit is not
captured by these one-shot rebuild or read benchmark suites.

Benchmark run `20260223` confirms the 0.3.0-preview.1 release candidate — no regressions
detected. Improvements vs `20260222`:

| Suite | 20260222 | 20260223 | Δ |
|---|---:|---:|---|
| Query — low selectivity (many matches) | 111,330 μs | 99,867 μs | **−10.3%** |
| Query — multiple QueryItems (OR logic) | 10,735 μs | 9,790 μs | **−8.8%** |
| Query — high selectivity (few matches) | 588.5 μs | 534.7 μs | **−9.1%** |
| Complex projection (multi-event types) | 127.75 μs | 111.70 μs | **−12.6%** |
| Projection rebuild (500 events) | 34,079 μs | 32,245 μs | **−5.4%** |
| Batch append (100 events, no flush) | 425.6 ms | 408.1 ms | **−4.1%** |
| Descending order vs ascending ratio | 1.02× slower | **1.00× parity** | ✅ |

All other benchmarks are within run-to-run noise (±2-4%). Full comparison in
`docs/benchmarking/results/20260223/ANALYSIS.md`.

Raw results: `docs/benchmarking/results/20260222/`

## [0.2.0-preview.1] - 2026-02-21

### Performance

- **Projection rebuild I/O reduced from O(events) to O(unique keys)** — `FileSystemProjectionStore`
  now supports a rebuild mode activated by `ProjectionManager.RebuildAsync`. State changes are
  buffered in memory during the event-application loop and every unique projection key is flushed
  to disk exactly once at the end via a new internal `CommitRebuildAsync` method. Previously each
  event application triggered a full `SaveAsync` cycle: projection-state file write + metadata
  index deserialise → update → re-serialise → temp-file write → rename. For 1,000 events across
  4 projections this amounted to ~12,000 file-system operations per sequential rebuild; with the
  optimisation it reduces to ~12. `ProjectionMetadataIndex` gains a complementary `BatchSaveAsync`
  that updates the entire cache and persists the index file in a single atomic write instead of
  once per entry. No public API changes.

### Added

- **`IEventStore.BuildDecisionModelAsync<T>(projection)`** — reads all events matching the
  projection's query, folds them into state, and returns `DecisionModel<T>` with a pre-built
  `AppendCondition` (same query + max position). Single-projection overload.

- **`IEventStore.BuildDecisionModelAsync<T1,T2>(p1, p2)`** — two-projection overload.
  Issues one `ReadAsync` with the union of both queries; each projection folds only its own
  matching subset. Returns `(T1, T2, AppendCondition)`.

- **`IEventStore.BuildDecisionModelAsync<T1,T2,T3>(p1, p2, p3)`** — three-projection overload.
  Same union-read approach. Returns `(T1, T2, T3, AppendCondition)`.

- **`IEventStore.ExecuteDecisionAsync<TResult>(operation, maxRetries, cancellationToken)`** —
  wraps the full DCB read → decide → append cycle with automatic exponential-backoff retry.
  Callers pass their decision logic as a delegate; the library handles retrying on
  `AppendConditionFailedException` so consumers no longer need to write this boilerplate.
  After exhausting `maxRetries` attempts the last exception is re-thrown.

- **`IDecisionProjection<TState>`** — interface that defines a write-side, in-memory projection:
  initial state, the query that scopes its event set, and a pure fold function.

- **`DecisionProjection<TState>`** — delegate-based concrete implementation of
  `IDecisionProjection<TState>`. Supports a static factory-method pattern for clean,
  per-command projection definitions.

- **`DecisionModel<TState>`** — result record returned by the single-projection overload of
  `BuildDecisionModelAsync`. Contains the folded `State` and the `AppendCondition` that guards
  the decision.

- **`Query.Matches(SequencedEvent)`** — in-memory event-matching helper that mirrors the
  OR/AND semantics the event store applies on disk. Required for composed projection filtering.

- **`fromPosition` parameter on `IEventStore.ReadAsync`** — fulfils the DCB specification SHOULD
  requirement for reading events from a given starting sequence position. Pass the last processed
  position to receive only events with `Position > fromPosition`, eliminating the need to load
  the full event log and filter in memory. `null` (the default) preserves existing behaviour.
  For `Query.All()`, the position range is generated directly (no wasted allocation); for indexed
  queries, positions are filtered after the index lookup. A convenience extension overload
  `ReadAsync(query, fromPosition)` is also provided.

- **`NewEvent` type for the write side of `AppendAsync`** — dedicated write-side type that holds
  `DomainEvent` and `Metadata` but has no `Position` property. Aligns with the DCB specification
  distinction between `Event` (input to `append`, no position) and `SequencedEvent` (output of
  `read`, position assigned by the store).

- **Sample: `CourseEnrollmentProjections`** — three single-purpose projection factories replacing
  the monolithic `CourseEnrollmentAggregate`: `CourseCapacity(courseId)`,
  `StudentEnrollmentLimit(studentId)`, and `AlreadyEnrolled(courseId, studentId)`. Each owns its
  own query; no shared mutable state.

### Changed

- **Breaking: `BuildProjections` parameter `aggregateIdSelector` renamed to `keySelector`** —
  "Aggregate" has no meaning in DCB; the rename removes the last `aggregate` reference from the
  public API and replaces it with neutral vocabulary: the parameter is simply the function that
  extracts the grouping key (typically the value of a domain identity tag) from each event.
  Callers using positional arguments are unaffected; callers using the named argument must update
  to `keySelector:`.

- **Breaking: `IEventStore.AppendAsync` now takes `NewEvent[]` instead of `SequencedEvent[]`** —
  All callers (command handlers, extension methods, test helpers, benchmark helpers) updated
  accordingly. `DomainEventBuilder.Build()` and its implicit cast now produce `NewEvent`.
  `SequencedEvent` remains the exclusive return type of `ReadAsync`.

- **Breaking: `IMultiStreamProjectionDefinition<TState>` renamed to `IProjectionWithRelatedEvents<TState>`** —
  The previous name leaked stream-based event sourcing terminology that contradicts DCB semantics.
  In DCB, events are first-class citizens — they do not belong to streams. The new name accurately
  describes what the interface does: it allows a projection to fetch related events from the store
  via a secondary `Query` when building its state. All usages in sample projections and tests have
  been updated.

- **`ConcurrencyException` is now a subclass of `AppendConditionFailedException`** — the DCB
  specification defines exactly one failure mode for an append: the append condition was violated.
  Both types represented the same condition as independent siblings; `ConcurrencyException` is now
  a subclass so a single `catch (AppendConditionFailedException)` covers both. Catching
  `ConcurrencyException` specifically still works for diagnostic access to
  `ExpectedSequence` / `ActualSequence`.

- **`DomainEvent.EventType` now auto-derives from `Event.GetType().Name` when not set** — the
  property is backed by a nullable field; if never assigned, the getter returns the inner
  `IEvent`'s simple class name automatically. An explicit assignment still takes effect. The
  existing `AppendAsync` validation (`IsNullOrWhiteSpace(EventType)`) now only fires for an
  explicitly blank string.

- **`ProjectionDaemon` now passes `fromPosition` directly to `ReadAsync`** — the polling loop
  previously read all events and filtered in memory with `Where(e => e.Position > checkpoint)`.
  It now calls `ReadAsync(Query.All(), null, minCheckpoint)` so the store skips already-processed
  events at the index level.

- **Thread safety: `FileSystemProjectionStore.DeleteAllIndicesAsync`** — was called without
  holding `_lock`, creating a data race with concurrent `SaveAsync` or `DeleteAsync` calls. Now
  acquires `_lock` for the duration of the clear operation.

- **Thread safety: `ProjectionTagIndex` read path no longer serialised** — after making writes
  atomic (temp+rename), concurrent readers always see either the old or the new complete file.
  The exclusive semaphore previously held for every `GetProjectionKeysByTagAsync` call has been
  removed from the read path; write operations still hold it to prevent lost-update races.

- **Durability: atomic writes for projection metadata files** — `ProjectionTagIndex`,
  `ProjectionMetadataIndex.PersistIndexAsync`, and `ProjectionManager.SaveCheckpointAsync` now
  use the same temp-file + `File.Move(overwrite: true)` atomic-rename pattern used by the event
  store, ledger, and index files. Previously they used `File.WriteAllTextAsync` in-place which
  can leave a partial file on crash.

- **Memory: eliminated redundant `OrderBy` on pre-sorted event arrays** — `ReadAsync` already
  returns events in ascending position order. Redundant `.OrderBy(e => e.Position)` calls in
  `BuildDecisionModelAsync` (all overloads), `FoldEvents`, `ProjectionManager.RebuildAsync`,
  `ProjectionManager.UpdateAsync`, and `ProjectionDaemon.ProcessNewEventsAsync` removed.

- **Memory: `FileSystemProjectionStore.SaveAsync` — single serialisation pass** — the projection
  wrapper was previously serialised twice: once to measure JSON length for `SizeInBytes`, then
  again with the value embedded. A single pass now writes the file; the correct byte count is
  passed to `_metadataIndex.SaveAsync`.

- **Memory: `JsonEventSerializer.PolymorphicEventConverter.Write` — eliminate intermediate string** —
  the write path serialised `IEvent` to a `string`, then parsed it back into a `JsonDocument`.
  A `MemoryStream`-backed approach now serialises directly to bytes via `Utf8JsonReader`,
  eliminating the intermediate string allocation on every event write.

- **Memory: `EventFileManager.GetEventFilePath` — eliminate dynamic format string** —
  `$"D{PositionPadding}"` allocated a new format-string object on every call because
  `PositionPadding` is a runtime value. Replaced with the compile-time literal `$"{position:D10}.json"`.

- **Memory: `ProjectionDaemon` — remove redundant `batch.ToArray()`** — `Enumerable.Chunk`
  already yields `T[]` segments; the extra `.ToArray()` call on each batch created a needless copy.

- **Code clarity: `Mediator._handlers`** — backing field typed as
  `IReadOnlyDictionary<Type, IMessageHandler>` to make the read-only contract explicit.

- **`ExecuteDecisionAsync` retry loop** — collapsed from two separate catch clauses
  (`AppendConditionFailedException` and `ConcurrencyException`) to one
  `catch (AppendConditionFailedException)` which covers both via the new exception hierarchy.

- **Sample: `EnrollStudentToCourseCommandHandler`** — refactored to use `BuildDecisionModelAsync`
  with all three projection factories in a single call. Manual query construction, event reading,
  and condition assembly removed. Unified from two catch blocks to a single
  `catch (AppendConditionFailedException)`.

- **Sample: `Program.cs` global exception handler** — unified from two separate
  `ConcurrencyException` / `AppendConditionFailedException` → HTTP 409 handlers to a single
  `AppendConditionFailedException` handler.

### Fixed

- **`ProjectionTagIndex.GetProjectionKeysByTagsAsync` — AND query correctness** — multi-tag AND
  queries were broken when the smallest index set was not the first element in the list.
  `keySets.Skip(1)` was applied to the unsorted list, so the smallest set was intersected with
  itself instead of the other tag sets. Fixed by sorting into a new list first and iterating over
  `sortedSets.Skip(1)`.

- **`FileSystemProjectionStore.SaveAsync` — stale tag index entries after application restart** —
  `_projectionTags` is an in-memory dictionary that is empty on every process start. When a
  projection was updated after a restart, `SaveAsync` treated it as brand-new and only added new
  tag entries without removing stale ones. Fixed by reading the existing on-disk state and
  reconstructing old tags via `IProjectionTagProvider` whenever the in-memory cache has no entry
  for the key.

### Removed

- **`CourseEnrollmentAggregate`** — replaced by `CourseEnrollmentProjections` factory methods.
- **`EnrollStudentToCourseCommandExtensions` / `Queries.cs`** — manual query construction
  replaced by queries embedded in the projection factories.

## [0.1.0-preview.1] - 2025-02-11

### 🎉 First Preview Release

This is the first preview release of Opossum - a file system-based event store implementing the DCB (Dynamic Consistency Boundaries) specification.

### ✨ Features

#### Core Event Store
- **File-based storage** - Events stored as JSON files in structured directories
- **DCB implementation** - Full support for Dynamic Consistency Boundaries specification
- **Optimistic concurrency** - Append conditions for race-free operations
- **Tag-based indexing** - Fast event queries without full scans
- **Event Type indexing** - Efficient filtering by event type
- **Ledger system** - Monotonic sequence positions
- **Durability guarantees** - Configurable flush-to-disk behavior

#### Projection System
- **Materialized views** - Rebuild read models from events
- **Tag-based projection queries** - Fast projection lookups
- **Multi-stream projections** - Query related events across streams
- **Automatic rebuilding** - Rebuild projections from scratch
- **Assembly scanning** - Auto-discover projection definitions

#### Developer Experience
- **.NET 10 support** - Built for latest .NET
- **Dependency injection** - First-class DI support
- **ConfigureAwait(false)** - Library-safe async/await
- **Mediator pattern** - Built-in command/query handling
- **Fluent API** - Intuitive event building with extensions
- **Sample application** - Complete course management example

#### Configuration
- **Flexible configuration** - appsettings.json binding support
- **Platform-aware paths** - Handles Windows/Linux path differences
- **Validation** - Built-in options validation

### 📝 Documentation
- Comprehensive README with quick start guide
- API reference documentation
- Sample application demonstrating real-world usage
- CONTRIBUTING guide for contributors
- Use case documentation (automotive retail, POS systems, etc.)
- Performance characteristics and scalability limits

### ⚠️ Known Limitations (MVP)
- **Single context only** - Multi-context support planned for future release (see `docs/limitations/mvp-single-context.md`)
- **No cache warming** - Feature planned but not in preview
- **Single-server deployments** - Not designed for distributed systems
- **File count limits** - Performance degrades beyond ~10M events

### 🎯 Target Use Cases
- On-premises applications
- Offline-first applications
- Small business ERP/POS systems
- Development & testing environments
- Compliance-heavy industries requiring audit trails
- Budget-conscious deployments avoiding cloud costs

### 📦 Package Information
- **Package ID:** Opossum
- **Target Framework:** .NET 10.0
- **License:** MIT
- **Repository:** https://github.com/majormartintibor/Opossum

### 🚀 Getting Started

```bash
dotnet add package Opossum --version 0.1.0-preview.1
```

See [README.md](README.md) for complete quick start guide.

### 🙏 Acknowledgments
- Inspired by the [DCB Specification](https://dcb.events/)
- Built for real-world use cases in automotive retail and SMB applications

---

## [Unreleased]

### Planned Features
- Multi-context support
- Cache warming for projections
- Snapshot support for aggregates
- Event schema versioning
- Retention policies
- Archiving and compression
- Cross-platform performance optimizations

[0.1.0-preview.1]: https://github.com/majormartintibor/Opossum/releases/tag/v0.1.0-preview.1
