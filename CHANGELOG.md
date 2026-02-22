# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
