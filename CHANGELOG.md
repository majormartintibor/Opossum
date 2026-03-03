# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
