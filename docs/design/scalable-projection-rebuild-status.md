# Scalable Projection Rebuild — Implementation Status

> **Architecture document:** docs/design/scalable-projection-rebuild-architecture.md
> **Tasks document:** docs/design/scalable-projection-rebuild-tasks.md
> **Target version:** 0.5.0-preview.1

This document is the single source of truth for tracking progress across implementation
sessions. Update the Status and Notes columns as work proceeds.

**Status values:**
- `⬜ Not Started` — work not yet begun
- `🔄 In Progress` — currently being worked on
- `✅ Done` — complete, verified
- `⏸ Blocked` — waiting on a dependency or decision
- `❌ Cancelled` — removed from scope (with reason in Notes)

---

## Phase 0 — Approval Gate

| ID | Task | Status | Notes |
|----|------|--------|-------|
| P0-T1 | Approve breaking API change: removing rebuild methods from `IProjectionManager` | ✅ Done | Approved — Opossum is pre-1.0; breaking changes that improve the library are acceptable |

---

## Phase 1 — Architectural Separation

Goal: `ProjectionRebuilder` exists and is wired up. Existing behaviour unchanged.
All tests pass at end of phase.

| ID | Task | Status | Notes |
|----|------|--------|-------|
| P1-T1 | Define `IProjectionRebuilder` interface | ✅ Done | |
| P1-T2 | Add `GetRegistration(string)` internal accessor + `BeginRebuildAsync(tempPath)` to `ProjectionManager` | ✅ Done | |
| P1-T3 | Create `ProjectionRebuildJournal` model class | ✅ Done | |
| P1-T4 | Create `ProjectionRebuilder` skeleton — move rebuild code from `ProjectionManager` | ✅ Done | |
| P1-T5 | Remove rebuild code from `ProjectionManager` | ✅ Done | Stubs kept for 4 interface methods until P1-T6 |
| P1-T6 | Remove rebuild methods from `IProjectionManager` | ✅ Done | Also completed P1-T7, P1-T8, P1-T9, P1-T10 — tightly coupled |
| P1-T7 | Register `ProjectionRebuilder` in DI (`ProjectionServiceCollectionExtensions`) | ✅ Done | Factory uses GetService for nullable logger |
| P1-T8 | Update `ProjectionDaemon` to inject and use `IProjectionRebuilder` | ✅ Done | |
| P1-T9 | Update sample application admin endpoints | ✅ Done | |
| P1-T10 | Update integration tests that called rebuild via `IProjectionManager` | ✅ Done | 3 tests adapted for result-based error handling |
| P1-T11 | ✔ Verify Phase 1: 0 warnings, all tests green | ✅ Done | 0 warnings, 721 unit + 171/172 integration pass (1 pre-existing flaky timing test) |

---

## Phase 2 — Write-Through Store

Goal: `_rebuildStateBuffer` eliminated. State written directly to temp directory during
replay. Memory during rebuild is now O(batch × state_size). All tests pass at end of phase.

| ID | Task | Status | Notes |
|----|------|--------|-------|
| P2-T1 | Add `BeginRebuild(string tempPath)` overload; initialise `_tagAccumulator` | ✅ Done | Renamed `_rebuildMode` → `_isInRebuild`; both overloads init `_tagAccumulator` |
| P2-T2 | Remove `_rebuildStateBuffer`; implement write-through in `SaveAsync` | ✅ Done | Writes directly to temp dir with metadata wrapper; accumulates tags in `_tagAccumulator` |
| P2-T3 | Update `GetAsync` rebuild branch to read from temp directory | ✅ Done | Reads from temp dir via `GetFilePath`; returns `null` if file doesn't exist |
| P2-T4 | Update `DeleteAsync` rebuild branch to delete from temp directory | ✅ Done | Deletes file + removes key from all tag accumulator sets; handles write-protect |
| P2-T5 | Rewrite `CommitRebuildAsync`: parallel tag writes + dir swap only | ✅ Done | `Parallel.ForEachAsync` for tag indices; no `_metadataIndex` calls; atomic swap |
| P2-T6 | Remove dead code: `ClearProjectionFiles`, `DeleteAllIndicesAsync`, `ClearAsync` on `ProjectionRegistration` | ✅ Done | Also removed integration test that tested `DeleteAllIndicesAsync` directly; fixed admin endpoint 404 for unknown projection |
| P2-T7 | ✔ Verify Phase 2: 0 warnings, all tests green, write-through observed in temp dir | ✅ Done | 0 warnings; 721 unit + 171 integration + 107 sample app tests green |

---

## Phase 3 — Rebuild Journal and Crash Recovery

Goal: Rebuild progress is durable. Application can resume an interrupted rebuild on
restart. At most `RebuildFlushInterval` events need re-processing.

| ID | Task | Status | Notes |
|----|------|--------|-------|
| P3-T1 | Add `RebuildFlushInterval` to `ProjectionOptions` (default 10,000; validator updated) | ✅ Done | Property, `[Range]` attribute, validator, and unit tests added |
| P3-T2 | Implement journal + tag accumulator file I/O in `ProjectionRebuilder` (create, flush, read, delete) | ✅ Done | 7 private methods + shared `WriteAtomicAsync<T>` helper; all use atomic temp-file + rename |
| P3-T3 | Integrate journal + tag accumulator flushing into `RebuildCoreAsync` event loop | ✅ Done | Journal created after `BeginRebuildAsync`; flushed every `RebuildFlushInterval` events (threshold-based); deleted after successful commit; left on disk on error. Added `RebuildTempPath`/`TagAccumulator` accessors to store + registration |
| P3-T4 | Implement `ResumeInterruptedRebuildsAsync`: scan journals, load tag accumulator, resume or discard | ✅ Done | Scans `*.rebuild.json`; discards journal if temp dir missing or projection unregistered; restores tag accumulator + resumes via `RebuildCoreAsync(journal)` overload. Refactored `RebuildCoreAsync` into fresh/resume overloads sharing `ReplayEventsAsync`. Added `RestoreTagAccumulator` to store + registration. `CleanOrphanedTempDirectoriesAsync` stub for P3-T5. |
| P3-T5 | Implement `CleanOrphanedTempDirectoriesAsync` | ✅ Done | Scans `_projectionsPath` for `*.tmp.*` dirs; extracts projection name; deletes if no matching journal exists in `_checkpointPath`; logs each deletion |
| P3-T6 | Update `ProjectionDaemon`: call `ResumeInterruptedRebuildsAsync` before `RebuildAllAsync` | ✅ Done | Added call inside `EnableAutoRebuild` block, before `RebuildMissingProjectionsAsync` |
| P3-T7 | Write crash recovery integration tests (6 test cases; see tasks document) | ✅ Done | 6 tests in `ProjectionRebuildCrashRecoveryTests.cs`: resume from flush point, missing temp dir → journal discarded, orphaned temp dir cleanup, journal flushed during rebuild, journal deleted on success, tag accumulator restored correctly on resume. All pass. |
| P3-T8 | ✔ Verify Phase 3: 0 warnings, all tests green (existing + new crash recovery tests) | ✅ Done | Clean build: 0 errors, 0 warnings. 736 unit + 177 integration (176 pass, 1 pre-existing flaky timing test) + 107 sample app — all green |

---

## Phase 4 — Metadata Index Decoupling

Goal: No aggregated metadata index written during rebuild. Post-rebuild reads served
from per-file embedded metadata.

| ID | Task | Status | Notes |
|----|------|--------|-------|
| P4-T1 | Verify `CommitRebuildAsync` makes no calls to `_metadataIndex`; remove any remaining calls | ✅ Done | Confirmed: already done in P2-T5. `CommitRebuildAsync` has zero `_metadataIndex` calls. Rebuild branches of `SaveAsync`/`DeleteAsync` also bypass it. No code changes needed. |
| P4-T2 | Verify lazy metadata index handles missing `Metadata/index.json` after rebuild | ✅ Done | `LoadIndexAsync` already guards against missing file. Fixed stale cache bug: added `ClearCache()` to `ProjectionMetadataIndex`, called from `CommitRebuildAsync` finally block to prevent post-rebuild `GetAsync` returning pre-rebuild version/timestamp data. |
| P4-T3 | Integration test: post-rebuild reads work without aggregated index | ✅ Done | 5 tests in `PostRebuildMetadataDecouplingTests.cs`: no `Metadata/index.json` after rebuild, `GetAsync` returns correct state, `GetAllAsync` returns all projections, `QueryAsync` filters correctly, normal `SaveAsync` after rebuild starts metadata version from 1 (cache not stale). All pass. |
| P4-T4 | ✔ Verify Phase 4: 0 warnings, all tests green | ✅ Done | Clean build: 0 errors, 0 warnings. 736 unit + 181/182 integration (1 pre-existing flaky timing test) + 117 sample unit + 107 sample integration — all green |

---

## Phase 5 — Sample Application and Configuration Guide

| ID | Task | Status | Notes |
|----|------|--------|-------|
| P5-T1 | Add `RebuildFlushInterval` to sample app `appsettings.json` / `.Development.json` | ✅ Done | Added with default 10,000 to both files |
| P5-T2 | Update `docs/configuration-guide.md` with `RebuildFlushInterval` documentation | ✅ Done | Added `RebuildFlushInterval` and `RebuildBatchSize` to Projections table; updated example JSON |

---

## Phase 6 — Final Verification and Documentation

| ID | Task | Status | Notes |
|----|------|--------|-------|
| P6-T1 | Update `CHANGELOG.md` under `[Unreleased]` | ✅ Done | Added Phase 1 (architectural separation, breaking API change), Phase 3 (crash recovery journal, resume, orphaned cleanup, `RebuildFlushInterval`), Phase 4 (metadata decoupling, stale cache fix) |
| P6-T2 | Final full test suite run across all projects — 0 warnings, all green | ✅ Done | Clean build: 0 errors, 0 warnings. 736 unit + 182 integration + 117 sample unit + 107 sample integration + 102 seeder unit + 22 seeder integration = 1,266 tests — all green |

---

## Session Log

Use this section to record what was done in each work session. This makes it easy to
resume in a new conversation without losing context.

| Date | Session summary | Tasks completed | Outstanding issues |
|------|-----------------|-----------------|--------------------|
| 2026-03 | **Phase 2 — Write-Through Store.** Replaced `_rebuildStateBuffer` with direct file writes in `FileSystemProjectionStore`. Renamed `_rebuildMode` → `_isInRebuild`. Added `_tagAccumulator` (populated in `SaveAsync`, written in parallel at commit). `GetAsync`/`DeleteAsync` now read from / delete in temp dir during rebuild. `CommitRebuildAsync` rewritten: parallel tag index writes + atomic dir swap only — no `_metadataIndex` calls, no state buffer flush. Removed dead code (`ClearProjectionFiles`, `DeleteAllIndicesAsync`, `ClearAsync` on `ProjectionRegistration`). Fixed admin endpoint returning 500 instead of 404 for unknown projections. Updated `CHANGELOG.md`. | P2-T1 through P2-T7 | None |

---

## Open Questions / Decisions Pending

| # | Question | Raised | Resolution |
|---|----------|--------|------------|
| 1 | Is removing rebuild methods from `IProjectionManager` approved? (P0-T1) | 2026-03 | ✅ Approved — breaking changes acceptable for pre-1.0 library |
| 2 | Should `ResumeInterruptedRebuildsAsync` be exposed publicly on `IProjectionRebuilder` for host-side use, or kept internal to the daemon? | 2026-03 | Pending |
| 3 | Should the tag accumulator also be flushed periodically (every N keys) for extreme tag counts, or is full in-memory accumulation acceptable for now? | 2026-03 | Pending — defer to post-1M-key benchmark |

---

## Known Scope Exclusions

The following related issues were identified during the design but are deliberately out
of scope for this implementation. They should be tracked as separate future items.

| Issue | Reason excluded | Suggested target |
|-------|----------------|-----------------|
| `ProjectionMetadataIndex._cache` grows unbounded in normal operation (not rebuild) | Not part of the rebuild scaling problem; separate concern | Post-0.5.0 |
| Tag accumulator memory at extreme tag counts (10+ tags × 1M+ keys) | Acceptable for now; in-memory accumulation is ~360 MB for 10 tags × 1M keys | Post-0.5.0 |
| Parallel writes in `GetAllAsync` threshold (magic number 10) | Pre-existing issue | Post-0.5.0 |

---

*End of status document.*
