# Scalable Projection Rebuild — Implementation Status

> **Architecture document:** docs/design/scalable-projection-rebuild-architecture.md
> **Tasks document:** docs/design/scalable-projection-rebuild-tasks.md
> **Target version:** 0.6.0

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
| P0-T1 | Approve breaking API change: removing rebuild methods from `IProjectionManager` | ⬜ Not Started | Requires explicit approval before Phase 1 begins |

---

## Phase 1 — Architectural Separation

Goal: `ProjectionRebuilder` exists and is wired up. Existing behaviour unchanged.
All tests pass at end of phase.

| ID | Task | Status | Notes |
|----|------|--------|-------|
| P1-T1 | Define `IProjectionRebuilder` interface | ⬜ Not Started | |
| P1-T2 | Add `GetRegistration(string)` internal accessor to `ProjectionManager` | ⬜ Not Started | |
| P1-T3 | Create `ProjectionRebuildJournal` model class | ⬜ Not Started | |
| P1-T4 | Create `ProjectionRebuilder` skeleton — move rebuild code from `ProjectionManager` | ⬜ Not Started | |
| P1-T5 | Remove rebuild code from `ProjectionManager` | ⬜ Not Started | |
| P1-T6 | Remove rebuild methods from `IProjectionManager` | ⬜ Not Started | |
| P1-T7 | Register `ProjectionRebuilder` in DI (`ProjectionServiceCollectionExtensions`) | ⬜ Not Started | |
| P1-T8 | Update `ProjectionDaemon` to inject and use `IProjectionRebuilder` | ⬜ Not Started | |
| P1-T9 | Update sample application admin endpoints | ⬜ Not Started | |
| P1-T10 | Update integration tests that called rebuild via `IProjectionManager` | ⬜ Not Started | |
| P1-T11 | ✔ Verify Phase 1: 0 warnings, all tests green | ⬜ Not Started | |

---

## Phase 2 — Write-Through Store

Goal: `_rebuildStateBuffer` eliminated. State written directly to temp directory during
replay. Memory during rebuild is now O(batch × state_size). All tests pass at end of phase.

| ID | Task | Status | Notes |
|----|------|--------|-------|
| P2-T1 | Add `BeginRebuild(string tempPath)` overload; initialise `_tagAccumulator` | ⬜ Not Started | |
| P2-T2 | Remove `_rebuildStateBuffer`; implement write-through in `SaveAsync` | ⬜ Not Started | |
| P2-T3 | Update `GetAsync` rebuild branch to read from temp directory | ⬜ Not Started | |
| P2-T4 | Update `DeleteAsync` rebuild branch to delete from temp directory | ⬜ Not Started | |
| P2-T5 | Rewrite `CommitRebuildAsync`: parallel tag writes + dir swap only | ⬜ Not Started | |
| P2-T6 | Remove dead code: `ClearProjectionFiles`, `DeleteAllIndicesAsync`, `ClearAsync` on `ProjectionRegistration` | ⬜ Not Started | |
| P2-T7 | ✔ Verify Phase 2: 0 warnings, all tests green, write-through observed in temp dir | ⬜ Not Started | |

---

## Phase 3 — Rebuild Journal and Crash Recovery

Goal: Rebuild progress is durable. Application can resume an interrupted rebuild on
restart. At most `RebuildFlushInterval` events need re-processing.

| ID | Task | Status | Notes |
|----|------|--------|-------|
| P3-T1 | Add `RebuildFlushInterval` to `ProjectionOptions` (default 10,000; validator updated) | ⬜ Not Started | |
| P3-T2 | Implement journal file I/O in `ProjectionRebuilder` (create, flush, read, delete) | ⬜ Not Started | |
| P3-T3 | Integrate journal flushing into `RebuildCoreAsync` event loop | ⬜ Not Started | |
| P3-T4 | Implement `ResumeInterruptedRebuildsAsync`: scan journals, resume or discard | ⬜ Not Started | |
| P3-T5 | Implement `CleanOrphanedTempDirectoriesAsync` | ⬜ Not Started | |
| P3-T6 | Update `ProjectionDaemon`: call `ResumeInterruptedRebuildsAsync` before `RebuildAllAsync` | ⬜ Not Started | |
| P3-T7 | Write crash recovery integration tests (5 test cases; see tasks document) | ⬜ Not Started | |
| P3-T8 | ✔ Verify Phase 3: 0 warnings, all tests green (existing + new crash recovery tests) | ⬜ Not Started | |

---

## Phase 4 — Metadata Index Decoupling

Goal: No aggregated metadata index written during rebuild. Post-rebuild reads served
from per-file embedded metadata.

| ID | Task | Status | Notes |
|----|------|--------|-------|
| P4-T1 | Verify `CommitRebuildAsync` makes no calls to `_metadataIndex`; remove any remaining calls | ⬜ Not Started | Likely already done in P2-T5; confirm |
| P4-T2 | Verify lazy metadata index handles missing `Metadata/index.json` after rebuild | ⬜ Not Started | Guard in `LoadIndexAsync` if needed |
| P4-T3 | Integration test: post-rebuild reads work without aggregated index | ⬜ Not Started | |
| P4-T4 | ✔ Verify Phase 4: 0 warnings, all tests green | ⬜ Not Started | |

---

## Phase 5 — Sample Application and Configuration Guide

| ID | Task | Status | Notes |
|----|------|--------|-------|
| P5-T1 | Add `RebuildFlushInterval` to sample app `appsettings.json` / `.Development.json` | ⬜ Not Started | |
| P5-T2 | Update `docs/configuration-guide.md` with `RebuildFlushInterval` documentation | ⬜ Not Started | |

---

## Phase 6 — Final Verification and Documentation

| ID | Task | Status | Notes |
|----|------|--------|-------|
| P6-T1 | Update `CHANGELOG.md` under `[Unreleased]` | ⬜ Not Started | |
| P6-T2 | Final full test suite run across all projects — 0 warnings, all green | ⬜ Not Started | |

---

## Session Log

Use this section to record what was done in each work session. This makes it easy to
resume in a new conversation without losing context.

| Date | Session summary | Tasks completed | Outstanding issues |
|------|-----------------|-----------------|--------------------|
| — | — | — | — |

---

## Open Questions / Decisions Pending

| # | Question | Raised | Resolution |
|---|----------|--------|------------|
| 1 | Is removing rebuild methods from `IProjectionManager` approved? (P0-T1) | 2026-03 | Pending |
| 2 | Should `ResumeInterruptedRebuildsAsync` be exposed publicly on `IProjectionRebuilder` for host-side use, or kept internal to the daemon? | 2026-03 | Pending |
| 3 | Should the tag accumulator also be flushed periodically (every N keys) for extreme tag counts, or is full in-memory accumulation acceptable for now? | 2026-03 | Pending — defer to post-1M-key benchmark |

---

## Known Scope Exclusions

The following related issues were identified during the design but are deliberately out
of scope for this implementation. They should be tracked as separate future items.

| Issue | Reason excluded | Suggested target |
|-------|----------------|-----------------|
| `ProjectionMetadataIndex._cache` grows unbounded in normal operation (not rebuild) | Not part of the rebuild scaling problem; separate concern | 0.7.0 |
| Tag accumulator memory at extreme tag counts (10+ tags × 1M+ keys) | Acceptable for now; in-memory accumulation is ~360 MB for 10 tags × 1M keys | 0.7.0 |
| Parallel writes in `GetAllAsync` threshold (magic number 10) | Pre-existing issue tracked in 0.5.0 roadmap | 0.5.0 |

---

*End of status document.*
