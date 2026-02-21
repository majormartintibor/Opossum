# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **`fromPosition` parameter on `IEventStore.ReadAsync`** — Fulfils the DCB specification SHOULD
  requirement for reading events from a given starting sequence position. Pass the last processed
  position to receive only events with `Position > fromPosition`, eliminating the need to load
  the full event log and filter in memory. `null` (the default) preserves existing behaviour and
  returns all matching events. For `Query.All()`, the position range is generated directly
  (no wasted allocation); for indexed queries, positions are filtered after the index lookup.
  A convenience extension overload `ReadAsync(query, fromPosition)` is also provided.

- **`ProjectionDaemon` now passes `fromPosition` directly to `ReadAsync`** — The polling loop
  previously read all events and filtered them in memory with `Where(e => e.Position > checkpoint)`.
  It now calls `ReadAsync(Query.All(), null, minCheckpoint)`, so the store itself skips
  already-processed events at the index level.

- **`NewEvent` type for the write side of `IEventStore.AppendAsync`** — Introduced `NewEvent`
  (`src/Opossum/Core/NewEvent.cs`) as a dedicated write-side type that holds `DomainEvent` and
  `Metadata` but has **no `Position`** property. This aligns Opossum with the DCB specification,
  which distinguishes between `Event` (input to `append`, no position) and `SequencedEvent`
  (output of `read`, position assigned by the store). The old workaround `Position = 0,
  // Will be assigned by AppendAsync` is gone from the entire codebase.

### Changed

- **`DomainEvent.EventType` now auto-derives from `Event.GetType().Name` when not set** —
  The property is backed by a nullable field: if you never assign it, the getter returns the
  inner `IEvent`'s simple class name automatically. An explicit assignment still takes effect
  (e.g. `"StudentRegistered"` instead of `"StudentRegisteredEvent"`), so deliberate custom type
  names continue to work. This eliminates the pre-existing silent divergence where a manually
  constructed `DomainEvent` with a forgotten or mistyped `EventType` would be indexed under the
  wrong name while `$type`-based deserialization still succeeded. The existing `AppendAsync`
  validation (`IsNullOrWhiteSpace(EventType)`) now only fires for the genuine bug of an
  explicitly blank string.

- **Breaking: `IEventStore.AppendAsync` now takes `NewEvent[]` instead of `SequencedEvent[]`**
  All callers (command handlers, extension methods, test helpers, benchmark helpers) updated
  accordingly. `DomainEventBuilder.Build()` and its implicit cast now produce `NewEvent`.
  `SequencedEvent` remains the exclusive return type of `ReadAsync`.

- **Thread safety: `FileSystemProjectionStore.DeleteAllIndicesAsync`**
  was called without holding `_lock`, creating a data race with any concurrent `SaveAsync` or
  `DeleteAsync`. Now acquires `_lock` for the clear operation.

- **Durability: atomic writes for projection metadata files** — `ProjectionTagIndex`,
  `ProjectionMetadataIndex.PersistIndexAsync`, and `ProjectionManager.SaveCheckpointAsync` all
  used `File.WriteAllTextAsync` (in-place overwrite) which can leave a partial file on crash.
  All three now use the same temp-file + `File.Move(overwrite: true)` atomic-rename pattern
  already used by the event store, ledger, and event-type/tag indices.

- **Thread safety: `ProjectionTagIndex` read path no longer serialised** — After making writes
  atomic (temp+rename), a concurrent reader always sees either the old or the new complete file
  and never a partial one. The exclusive semaphore previously held for every
  `GetProjectionKeysByTagAsync` call has been removed from the read path; write operations still
  hold it to prevent lost-update races.

- **Memory: eliminated redundant `OrderBy` on pre-sorted event arrays** — `ReadAsync` returns
  events in ascending position order (positions are sorted by `Array.Sort` inside
  `GetPositionsForQueryAsync`). Redundant `.OrderBy(e => e.Position)` calls in
  `DecisionModelExtensions.BuildDecisionModelAsync` (all overloads), `FoldEvents`,
  `ProjectionManager.RebuildAsync`, `ProjectionManager.UpdateAsync`, and
  `ProjectionDaemon.ProcessNewEventsAsync` have been removed.

- **Memory: `FileSystemProjectionStore.SaveAsync` — single serialisation pass** — The projection
  wrapper was previously serialised twice: once to measure the JSON length for `SizeInBytes`,
  then again with the measured value embedded. The second pass served no observable purpose
  because `SizeInBytes` in the file body is never read back (metadata is always accessed through
  the metadata index). A single serialisation now writes the file; the correct byte count is
  passed to `_metadataIndex.SaveAsync`.

- **Memory: `JsonEventSerializer.PolymorphicEventConverter.Write` — avoid intermediate string** —
  The write path previously serialised `IEvent` to a `string`, then parsed that string back into a
  `JsonDocument` to copy its properties. A `MemoryStream`-backed approach now serialises directly
  to bytes and parses via `Utf8JsonReader`, eliminating the intermediate string allocation on every
  event write.

- **Memory: `EventFileManager.GetEventFilePath` — eliminate dynamic format string** —
  `$"D{PositionPadding}"` allocated a new format-string object on every call because
  `PositionPadding` is read at runtime. Replaced with the compile-time literal `$"{position:D10}.json"`.

- **Memory: `ProjectionDaemon` — remove redundant `batch.ToArray()`** — `Enumerable.Chunk`
  already yields `T[]` segments; calling `.ToArray()` on each batch created a needless copy.

- **Code clarity: `Mediator._handlers`** — The backing field is typed as
  `IReadOnlyDictionary<Type, IMessageHandler>` to make the read-only contract explicit.


  that wraps the full DCB read → decide → append cycle with automatic exponential-backoff retry.
  Callers pass their decision logic as a delegate; the library handles retrying on
  `AppendConditionFailedException` and `ConcurrencyException` so consumers no longer need to write
  this boilerplate. After exhausting `maxRetries` attempts the last exception is re-thrown.

### Changed

- **`IMultiStreamProjectionDefinition<TState>` renamed to `IProjectionWithRelatedEvents<TState>`** —
  The previous name leaked stream-based event sourcing terminology (Marten, EventStoreDB) which
  contradicts DCB semantics. In DCB there are no streams; events live in a single flat log filtered
  by tags and event types. The new name accurately describes what the interface does: it allows a
  projection to fetch related events from the store (via a secondary `Query`) when building its
  state. All usages in sample projections and tests have been updated accordingly.

- **`ConcurrencyException` is now a subclass of `AppendConditionFailedException`** —
  The DCB specification defines exactly one failure mode for an append: *the append condition was
  violated*. Both types previously represented this same condition as independent siblings under
  `EventStoreException`, forcing every caller to write two catch blocks. `ConcurrencyException`
  (thrown by the file-system layer for internal ledger-level races) is now a subclass of
  `AppendConditionFailedException` so a single `catch (AppendConditionFailedException)` covers
  both. Catching `ConcurrencyException` specifically still works for diagnostic access to
  `ExpectedSequence` / `ActualSequence`.

- **`ExecuteDecisionAsync` retry loop** — collapsed from two separate catch clauses
  (`AppendConditionFailedException` and `ConcurrencyException`) down to one
  `catch (AppendConditionFailedException)` which covers both via the new hierarchy.

- **Sample app `EnrollStudentToCourseCommandHandler`** — unified from two catch blocks to a
  single `catch (AppendConditionFailedException)`.

- **Sample app global exception handler (`Program.cs`)** — unified from two separate
  `ConcurrencyException` / `AppendConditionFailedException` → HTTP 409 handlers to a single
  `AppendConditionFailedException` handler.

### 🐛 Fixed

- **`ProjectionTagIndex.GetProjectionKeysByTagsAsync`** — Multi-tag AND queries were broken when
  the smallest index set was not the first element in the list. `keySets.Skip(1)` was applied to
  the *original* unsorted list, so the smallest set was intersected with itself instead of the
  other tag sets, causing `QueryByTagsAsync` with e.g. `tierFilter=Professional, isMaxedOut=false`
  to return results matching only one of the two conditions. Fixed by sorting into a new list first
  and iterating over `sortedSets.Skip(1)`.

- **`FileSystemProjectionStore.SaveAsync` — stale tag index entries after application restart** —
  `_projectionTags` is an in-memory dictionary that is empty on every process start. When a
  projection was updated after a restart, `SaveAsync` could not find old tags in the dictionary,
  treated the projection as brand-new, and only *added* the new tag entries without *removing*
  the stale ones. For example, upgrading a student's enrollment tier from Basic → Standard →
  Professional across restarts left the student's key in all three index files simultaneously.
  Fixed by reading the existing on-disk state (before overwriting the file) and reconstructing
  old tags via `IProjectionTagProvider` whenever the in-memory cache has no entry for the key.

### ✨ Added

#### Decision Model Projections (DCB write-side pattern)

- **`Query.Matches(SequencedEvent)`** — in-memory event-matching helper that mirrors the
  OR/AND semantics the event store applies on disk. Required for composed projection filtering.

- **`IDecisionProjection<TState>`** — interface that defines a write-side, in-memory projection:
  initial state, the query that scopes its event set, and a pure fold function.

- **`DecisionProjection<TState>`** — delegate-based concrete implementation of
  `IDecisionProjection<TState>`. Supports a static factory-method pattern for clean,
  per-command projection definitions.

- **`DecisionModel<TState>`** — result record returned by `BuildDecisionModelAsync`.
  Contains the folded `State` and the `AppendCondition` that guards the decision.

- **`IEventStore.BuildDecisionModelAsync<T>(projection)`** — reads all events matching the
  projection's query, folds them into state, and returns `DecisionModel<T>` with a
  pre-built `AppendCondition` (same query + max position). Single-projection overload.

- **`IEventStore.BuildDecisionModelAsync<T1,T2>(p1, p2)`** — two-projection overload.
  Issues one `ReadAsync` with the union of both queries; each projection folds only its own
  matching subset. Returns `(T1, T2, AppendCondition)`.

- **`IEventStore.BuildDecisionModelAsync<T1,T2,T3>(p1, p2, p3)`** — three-projection
  overload. Same union-read approach. Returns `(T1, T2, T3, AppendCondition)`.

#### Sample Application — Course Management

- **`CourseEnrollmentProjections`** — three single-purpose projection factories replacing
  the monolithic `CourseEnrollmentAggregate`:
  - `CourseCapacity(courseId)` — tracks seats available on a course
  - `StudentEnrollmentLimit(studentId)` — tracks tier and current course count for a student
  - `AlreadyEnrolled(courseId, studentId)` — detects duplicate enrollment for a specific pair
  - Each projection owns its own query; no shared mutable state.

- **`EnrollStudentToCourseCommandHandler` refactored** — now uses
  `BuildDecisionModelAsync` with all three projections in a single call. The
  `AppendCondition` is returned automatically and spans all three queries. The manual
  query construction, event reading, aggregate building, and condition assembly have been
  removed.

### 🗑️ Removed

- **`CourseEnrollmentAggregate`** — replaced by `CourseEnrollmentProjections` factory methods.
- **`EnrollStudentToCourseCommandExtensions` / `Queries.cs`** — manual query construction
  replaced by queries embedded in the projection factories.

### 📝 Documentation

- **README** — added `### Decision Model Projections` subsection to Core Concepts with API
  overview, code example, and link to the sample application.
- **`docs/implementation/decision-model-projections-plan.md`** — Step 6 marked as Done.

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
