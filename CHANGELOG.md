# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **DataSeeder Session 9 — Final Polish**
  Completed the DataSeeder redesign with cleanup and documentation tasks:
  — Deleted the legacy monolithic `DataSeeder.cs` class (all functionality has been fully
  replaced by the nine dedicated generator classes, `SeedPlan` orchestrator, `DirectEventWriter`,
  and `EventStoreWriter` introduced in Sessions 5–8).
  — Rewrote `Samples/Opossum.Samples.DataSeeder/README.md` from scratch to document the
  new three-layer architecture (generators → `SeedPlan` → writer), all four presets with
  event-count estimates, the interactive console menu flow, all CLI flags with examples,
  the complete 15-event-type catalogue with tags and timestamp windows, all
  `SeedingConfiguration` properties, and the on-disk layout guarantee.
  — Updated `docs/design/dataseeder-redesign.md` status from `Draft` to `Implemented`.

### Added
- **DataSeeder Session 8 — Console UX and Presets**
  Replaced the legacy monolithic `Program.cs` entry point with a fully redesigned interactive
  console experience that wires the complete generator + `SeedPlan` + writer pipeline:
  — `SeedingPresets` static class: four factory methods (`Small`, `Medium`, `Large`, `Prod`)
  returning pre-calibrated `SeedingConfiguration` instances covering ~620, ~104 000,
  ~1 030 000, and ~5 150 000 events respectively.
  — `SeedingConfiguration` extended with `PresetName` (display name set by presets),
  `UseEventStoreWriter` (opt-in fallback to `IEventStore.AppendAsync`), and
  `WriteParallelism` (max concurrent file-write threads; 0 = `ProcessorCount`).
  `EstimatedEventCount` replaced with an improved formula from the design document that
  accounts for all nine event-producing generators (students, tier upgrades, enrollments,
  capacity changes, announcements, exam tokens, books, book purchases, invoices).
  — Rewritten `Program.cs`: on startup displays the database path, then presents a
  four-option interactive preset menu (`[1] Small … [4] Prod`) followed by a reset prompt
  and a confirmation summary showing preset name, entity counts, estimated event count,
  reset flag, and writer description. After confirmation, resets the directory if requested,
  instantiates all nine generators in dependency order, selects the writer, and invokes
  `SeedPlan.RunAsync`. Prints elapsed time and total events written on completion.
  — CLI flag support bypasses the interactive menu for scripted / CI runs:
  `--size <small|medium|large|prod>`, `--reset`, `--no-confirm`, `--use-event-store`,
  `--parallelism <n>`, `--help`. Example: `dotnet run -- --size small --reset --no-confirm`.
  — `SeedPlan.RunAsync` now returns `Task<int>` (total events written) instead of `Task`,
  enabling the caller to display the exact written count.
  — 12 new integration tests in `SeederEndToEndTests` covering:
  end-to-end round-trip (Small preset → `DirectEventWriter` → `IEventStore.ReadAsync`),
  correct student and course event counts, ascending position order, presence of all nine
  expected event types, all four preset shape assertions, and
  `EstimatedEventCount` scaling and boundary behaviour.

- **DataSeeder Session 7 — New Feature Generators**
  Three new generator classes covering all DCB-pattern feature areas that previously had
  no seed data, completing the feature-coverage goal of the DataSeeder redesign:
  — `AnnouncementGenerator`: produces `CourseAnnouncementPostedEvent` records (3 per course
  by default) and `CourseAnnouncementRetractedEvent` records (~20% of announcements).
  Each announcement receives a unique `AnnouncementId` and a unique `IdempotencyToken`;
  retraction events reuse the same token as the corresponding posted event, matching the
  Idempotency / Prevent-Record-Duplication DCB pattern exactly.
  Tags emitted: `courseId`, `idempotency`.
  — `ExamTokenGenerator`: produces `ExamRegistrationTokenIssuedEvent` (2 exams × 5 tokens
  per course by default), `ExamRegistrationTokenRedeemedEvent` (~70% of tokens), and
  `ExamRegistrationTokenRevokedEvent` (~10% of tokens). Invariants enforced in pure code:
  each token has a unique `TokenId`; redeemed and revoked events are mutually exclusive per
  token; redemption student is drawn from enrolled students in the token's course (via
  `SeedContext.EnrolledPairs`); redeemed/revoked timestamps always follow the issued
  timestamp.
  Tags emitted: `examToken`, `examId`, `courseId` (issued); `examToken`, `examId`,
  `studentId` (redeemed); `examToken`, `examId` (revoked).
  — `CourseBookGenerator`: produces `CourseBookDefinedEvent`, `CourseBookPriceChangedEvent`,
  `CourseBookPurchasedEvent`, and `CourseBooksOrderedEvent` records. Books are assigned
  to courses (one per course up to `CourseBookCount`; extras distributed round-robin);
  price changes are applied to ~40% of books; `PricePaid` on every purchase and order item
  always matches the current in-memory price at generation time — no event-store reads.
  Multi-book orders prefer same-course books when possible; all four event types carry the
  correct `courseId` tag. Populates `SeedContext.Books` for downstream consumers.
  Tags emitted: `bookId`, `courseId` (defined + purchased + ordered); `bookId`
  (price changed); `studentId`, `bookId` per item, `courseId` (ordered).
  — `BookInfo` record added to `Core/` to represent a defined book's `BookId` and
  `CourseId` in `SeedContext`.
  — `SeedContext.Books` list added for downstream generator access.
  — `SeedingConfiguration` gains 10 new properties: `AnnouncementsPerCourse` (3),
  `AnnouncementRetractionPercentage` (20), `ExamsPerCourse` (2), `TokensPerExam` (5),
  `TokenRedemptionPercentage` (70), `TokenRevocationPercentage` (10), `CourseBookCount`
  (200), `PriceChangePercentage` (40), `SingleBookPurchasesPerBook` (20),
  `MultiBookOrders` (200).
  — 52 new unit tests across three test classes (`AnnouncementGeneratorTests`,
  `ExamTokenGeneratorTests`, `CourseBookGeneratorTests`) in the existing
  `Opossum.Samples.DataSeeder.UnitTests` project, all using fixed seed `Random(42)`:
  event counts, tag presence, tag/payload consistency, ordering invariants (issued before
  redeemed/revoked), mutual exclusion of lifecycle states, price consistency, and
  empty/boundary conditions.


  Six new generator classes porting all logic from the legacy `DataSeeder.cs` into the
  `ISeedGenerator` pattern. Each generator is a stateless pure function — no I/O, no
  event-store reads — enforcing all domain invariants via in-memory data structures:
  — `StudentGenerator`: produces `StudentRegisteredEvent` records; enforces unique e-mail
  addresses (counter suffix on collision) and tier distribution matching config percentages;
  populates `SeedContext.Students`.
  — `TierUpgradeGenerator`: produces `StudentSubscriptionUpdatedEvent` for
  `TierUpgradePercentage%` of non-Master students; updates `SeedContext.Students` so
  downstream generators see the upgraded tier limits.
  — `CourseGenerator`: produces `CourseCreatedEvent` records across Small / Medium / Large
  size categories; capacity drawn from per-category bounds; populates `SeedContext.Courses`.
  — `CapacityChangeGenerator`: produces `CourseStudentLimitModifiedEvent` for
  `CapacityChangePercentage%` of courses; enforces minimum capacity of 10; updates
  `SeedContext.Courses`.
  — `EnrollmentGenerator`: produces `StudentEnrolledToCourseEvent` records using a partial
  Fisher-Yates shuffle over the available-course pool; enforces no duplicate pairs, course
  capacity, and student tier limit entirely in O(MaxCourses) per student via swap-remove;
  populates `SeedContext.CourseEnrollmentCounts`, `StudentEnrollmentCounts`, and
  `EnrolledPairs`.
  — `InvoiceGenerator`: produces `InvoiceCreatedEvent` records with sequential invoice
  numbers from an in-memory counter (no store read required).
  — `GeneratorHelper`: shared static class with name arrays, `RandomTimestamp`, and
  `CreateSeedEvent` helpers.
  — `SeedingConfiguration` gains `TierUpgradePercentage` (default: 30) and
  `CapacityChangePercentage` (default: 20); `EstimatedEventCount` updated to use these.
  — New `Opossum.Samples.DataSeeder.UnitTests` project with 50 unit tests covering all six
  generators: event count, tag presence, key invariant properties, context-state updates,
  and timestamp windows — all deterministic via `Random(42)`.


- **DataSeeder core infrastructure (Session 5 of the DataSeeder Redesign plan)**
  — `SeedEvent` record (pre-position event wrapper), `SequencedSeedEvent` record
  (event with pre-assigned position), `SeedContext` class (shared mutable state for
  generators), `StudentInfo` and `CourseInfo` value records, `ISeedGenerator` interface,
  `IEventWriter` interface, and `SeedPlan` orchestrator (stable-sorts by timestamp, assigns
  1-based positions, delegates to writer).
  — `DirectEventWriter`: high-performance file-system writer that accumulates all index
  structures in memory across the full batch and flushes each index file exactly once,
  achieving O(1) per-event index I/O regardless of batch size. Event files are written in
  parallel (default: `Environment.ProcessorCount` threads). Uses the same JSON format as
  Opossum's internal serializer and the same temp-file + atomic-rename strategy as
  `EventFileManager`. Supports appending to existing databases by reading the current ledger
  position and offsetting all new positions accordingly.
  — `EventStoreWriter`: thin `IEventStore`-backed fallback writer for small datasets.
  — `SeedEventSerializer`: internal replication of Opossum's `JsonEventSerializer` (including
  `PolymorphicEventConverter`) so that `DirectEventWriter` produces byte-for-bit compatible
  event files without requiring access to Opossum internals.
  — New integration test project `Opossum.Samples.DataSeeder.IntegrationTests` with 8 tests
  covering event file creation, EventStore round-trip deserialization, EventType and Tag index
  creation, ledger correctness, multi-batch append/merge, and `SeedPlan` timestamp sorting.
  — `GlobalUsings.cs` added to `Opossum.Samples.DataSeeder`; duplicate external usings removed
  from `DataSeeder.cs` and `Program.cs` per the project's using-statement conventions.
- **`StudentPurchasedBooksProjection` and `GET /students/{studentId}/purchased-books` endpoint** —
  new persisted projection (`IProjectionDefinition<StudentPurchasedBooksState>`) keyed by
  `studentId` tag. Folds `CourseBookPurchasedEvent` and `CourseBooksOrderedEvent` into a
  per-student deduplicated book list. Each `PurchasedBookEntry` aggregates `TotalPaid`,
  `PurchaseCount`, `FirstPurchasedAt`, and `LastPurchasedAt` across all individual purchases and
  cart orders for the same `bookId`. Returns 404 when the student has no purchase history.
  Covered by unit tests (`StudentPurchasedBooksProjectionTests`) and integration tests
  (`StudentPurchasedBooksIntegrationTests`). _Session 3 of the DataSeeder Redesign plan._

- **`CourseId` property on `CourseBookDefinedEvent`**
  course book to its associated course. `DefineCourseBookRequest`, `DefineCourseBookCommand`, and
  the `POST /course-books` endpoint all accept the new `CourseId` field. The
  `DefineCourseBookCommandHandler` now emits a `courseId` tag alongside the existing `bookId` tag
  on every `CourseBookDefinedEvent`. This is a prerequisite for the `CourseBuyersProjection`
  (Session 4). All existing unit and integration tests updated.

- **`CourseBookPriceProjections.CourseIdForBook(bookId)` projection** — new decision projection
  that reads `CourseBookDefinedEvent` for a given `bookId` and returns the associated `CourseId`
  (or `null` when the book does not exist). Used by purchase command handlers to tag purchase events
  with the course the book belongs to.

- **`CourseBuyersProjection` and `GET /courses/{courseId}/book-buyers` endpoint** —
  new persisted projection (`IProjectionDefinition<CourseBuyersState>`) keyed by `courseId` tag.
  Folds `CourseCreatedEvent`, `CourseBookDefinedEvent`, `CourseBookPurchasedEvent`, and
  `CourseBooksOrderedEvent` into a per-course read model containing the course name, the assigned
  textbook ID, and all students who purchased that textbook. Returns 404 when no data exists for
  the requested course. Known limitation: a `CourseBooksOrderedEvent` carrying books from multiple
  courses only updates the buyer list for the first `courseId` tag on the event — documented in
  both the endpoint description and the design plan. Covered by integration tests
  (`CourseBuyersIntegrationTests`). _Session 4 of the DataSeeder Redesign plan._

- **`courseId` tag on `CourseBookPurchasedEvent`** — `PurchaseCourseBookCommandHandler` now uses
  the binary `BuildDecisionModelAsync` overload to read both `PriceWithGracePeriod` and
  `CourseIdForBook` in a single store read. The emitted `CourseBookPurchasedEvent` carries a
  `courseId` tag when the book has an associated course. No extra I/O round-trip.

- **`courseId` tag on `CourseBooksOrderedEvent`** — `OrderCourseBooksCommandHandler` makes a
  second N-ary `BuildDecisionModelAsync` call (one `CourseIdForBook` projection per cart item)
  after price validation. Unique `courseId` values from all items are added as tags on the
  `CourseBooksOrderedEvent`. Course-book associations are immutable so this secondary read
  does not need to participate in the `AppendCondition`.

  All new projections and tag behaviour are covered by unit tests (`CourseIdForBook_*`) and
  integration tests (`PurchaseBook_StoresCourseIdTagAsync`, `OrderBooks_StoresCourseIdTagAsync`).

### Documentation
- **README.md comprehensive update** — fully documents all 7 DCB examples (https://dcb.events/examples/)
  implemented in the sample app:
  - Added **DCB Examples Coverage** table mapping all examples to sample locations
  - Added **Consecutive Sequences — Invoice Numbers** section documenting the `ReadLastAsync`
    pattern for gap-free numbering
  - Added **Dynamic Product Price — Course Books** section documenting the `TimeProvider`
    constructor overload and N-ary `BuildDecisionModelAsync` overload (shopping cart)
  - Added **Opt-In Token — Server-Generated Single-Use Tokens** section documenting the enum
    lifecycle projection and event store as token registry
  - Added **OpenTelemetry** section documenting `OpossumTelemetry.ActivitySourceName` and
    traced operations (`EventStore.Append`, `EventStore.Read`, `EventStore.ReadLast`,
    `Projection.Rebuild`)
  - Updated **API Reference**: `IEventStore` now shows `ReadLastAsync`; Extension Methods
    show N-ary `BuildDecisionModelAsync` and metadata builder methods (`WithCorrelationId`,
    `WithCausationId`); Query Building shows `Query.FromEventTypes()` and `Query.FromTags()`
  - Added **`CommandResult<T>`** documentation in the API Reference
  - Clarified distinction between client-generated idempotency tokens and server-generated
    Opt-In tokens throughout

---

## [0.4.0-preview.4] - Unreleased

### Added

- **`TimeProvider` constructor overload in `DecisionProjection<TState>`** — a new constructor
  overload accepts `Func<TState, SequencedEvent, TimeProvider, TState>` plus an optional
  `TimeProvider?` parameter (defaults to `TimeProvider.System`). Enables time-dependent fold
  functions to be unit-tested without external packages by injecting a `FixedTimeProvider`.
  This is a purely additive, non-breaking change; all existing projections compile without
  modification.

- **N-ary `BuildDecisionModelAsync` overload** — new
  `BuildDecisionModelAsync<TState>(IReadOnlyList<IDecisionProjection<TState>>, CancellationToken)`
  extension method on `IEventStore`. Issues a single `ReadAsync` call for a runtime-variable
  list of homogeneous projections and returns `(IReadOnlyList<TState> States, AppendCondition)`.
  Enables the DCB runtime-variable-N pattern (e.g. shopping cart) as a first-class library
  feature. Throws `ArgumentException` on empty list. Fully covered by unit and integration tests.

- **Dynamic Course Book Price feature in `Opossum.Samples.CourseManagement`** — implements the
  DCB "Dynamic Product Price" example (https://dcb.events/examples/dynamic-product-price/)
  adapted to Course Books:
  - **F1 — Single book, fixed price**: `PurchaseCourseBookCommand` uses
    `PriceWithGracePeriod` decision projection; displayed price must match stored price.
  - **F2 — Grace period**: `CourseBookPriceProjections.PriceWithGracePeriod(bookId, timeProvider?)`
    uses the new `TimeProvider` constructor overload — within 30 minutes of a price change
    both the old and new price are accepted.
  - **F3 — Shopping cart**: `OrderCourseBooksCommand` uses the new N-ary
    `BuildDecisionModelAsync` overload — one projection per cart item, one event-store read,
    one `AppendCondition` spanning all books.
  - Four new events: `CourseBookDefinedEvent`, `CourseBookPriceChangedEvent`,
    `CourseBookPurchasedEvent`, `CourseBooksOrderedEvent`.
  - `CourseBookPriceState` record with `IsValidPrice(decimal)` business method.
  - `CourseBookPriceProjections`: `CurrentPrice`, `PriceWithGracePeriod`, `BookExists`.
  - Two read-side projections: `CourseBookCatalogProjection` and
    `CourseBookOrderHistoryProjection`.
  - Six API endpoints under the **"Course Books (Dynamic Price)"** tag:
    `POST /course-books`, `PATCH /course-books/{id}/price`,
    `POST /course-books/{id}/purchase`, `POST /course-books/order`,
    `GET /course-books`, `GET /course-books/orders`.
  - 18 unit tests (library + sample projections) and 14 integration tests covering all
    three features, admin endpoint projection counts, and concurrency guards.

- **`docs/analysis/dynamic-course-book-price-feasibility.md`** — full feasibility analysis
  and implementation plan for the DCB "Dynamic Product Price" example adapted to Course Books.
  Documents both library gaps (time injection, N-ary projections) and their resolution.

---

## [0.4.0-preview.3] - Unreleased

### Added
- **Exam Registration Token feature in `Opossum.Samples.CourseManagement`** — implements the
  DCB "Opt-In Token" pattern (https://dcb.events/examples/opt-in-token/).
  - Three new events: `ExamRegistrationTokenIssuedEvent`, `ExamRegistrationTokenRedeemedEvent`,
    `ExamRegistrationTokenRevokedEvent`.
  - `ExamTokenStatus` enum (`NotIssued | Issued | Revoked | Redeemed`) and `ExamTokenState`
    record that carry both the lifecycle status and the `ExamId` in a single projection fold.
  - `ExamRegistrationTokenProjections` with `TokenStatus(tokenId)` and `CourseExists(courseId)`
    factory methods — the token-scoped query replaces an entire "valid tokens" read model.
  - Three command handlers: `IssueExamRegistrationTokenCommandHandler` (unconditional append
    guarded by `CourseExists`), `RedeemExamRegistrationTokenCommandHandler` (wrapped in
    `ExecuteDecisionAsync` for retry), `RevokeExamRegistrationTokenCommandHandler`.
  - Three API endpoints: `POST /exams/{examId}/registration-tokens`,
    `POST /exams/registration-tokens/{tokenId}/redeem`,
    `DELETE /exams/registration-tokens/{tokenId}`.
  - 13 unit tests (pure in-memory projection folds) and 10 integration tests (all scenarios
    from the DCB spec: issue, redeem, redeem-unknown, redeem-already-used, revoke,
    redeem-revoked, revoke-already-redeemed).
- **Full DCB examples coverage analysis** — all 7 examples from https://dcb.events/examples/
  mapped against `Opossum` and `Opossum.Samples.CourseManagement`.
- **`docs/analysis/course-subscriptions-feasibility.md`** — documents the mapping of the
  DCB "Course Subscriptions" example to the existing `CourseEnrollment` feature, including
  the three-projection decision model, domain enrichment (subscription tiers), and the
  second implementation via the Event-Sourced Aggregate pattern.
- **`docs/analysis/unique-username-feasibility.md`** — documents the mapping of the DCB
  "Unique Username" example to student email uniqueness in `StudentRegistration`. Explains
  why the intentional use of the raw DCB API (direct `ReadAsync` + `AppendCondition`) is a
  valid alternative to `BuildDecisionModelAsync`, and preserves both styles in the sample.
- **`docs/analysis/invoice-number-feasibility.md`** — documents the mapping of the DCB
  "Invoice Number" example to `InvoiceCreation`, including the `ReadLastAsync` primitive,
  the global (tag-free) consistency boundary, and the bootstrap-race guard.
- **`docs/analysis/opt-in-token-feasibility.md`** — feasibility analysis and full
  implementation plan for the DCB "Opt-In Token" example. Domain adaptation: Course
  Enrollment Token (instructor issues a single-use invitation token; student redeems it to
  enroll). Demonstrates DCB as a read-model replacement — no persistent token projection
  needed for validation. Includes optional revocation extension, 7-step implementation
  order, and open questions for the implementation session.

---

## [0.4.0-preview.2] - Unreleased

### Added
- **`StudentAggregate` and `StudentAggregateRepository`** in `Opossum.Samples.CourseManagement`
  — a second Event-Sourced Aggregate alongside `CourseAggregate`, giving the sample two
  complete, single-responsibility aggregates each backed by their own repository.
- **`CourseEnrollmentService`** domain service — coordinates `CourseAggregate` and
  `StudentAggregate` to enforce all three enrollment invariants (course capacity, student
  tier limit, duplicate enrollment) in the Aggregate pattern approach. Two independent
  repository loads are safe because store positions are globally monotonically increasing;
  the compound `AppendCondition` uses `MAX(course.Version, student.Version)` as its
  watermark. `CourseAggregateRepository.SaveAsync` accepts an optional `AppendCondition`
  override so the domain service can supply the compound condition without either repository
  needing to know about the other aggregate.
- **`docs/analysis/aggregate-vs-dcb-comparison.md`** — full side-by-side analysis of the
  DCB Decision Model vs Event-Sourced Aggregate pattern for cross-entity invariant
  enforcement, including the two-independent-reads safety proof and an asset count table.
- **2 new integration tests** for the aggregate enrollment endpoint: tier-limit rejection
  and duplicate-enrollment rejection.
- **9 new unit tests** for `StudentAggregate` (reconstitution, tier folding, limit
  computation) and 2 new unit tests for `CourseAggregate.SubscribeStudent` (already-enrolled
  guard).

- **Course Announcement feature in `Opossum.Samples.CourseManagement`** — implements the DCB
  "Prevent Record Duplication" pattern from <https://dcb.events/examples/prevent-record-duplication/>.
  Two new endpoint groups under the **"Course Announcement (Idempotency Pattern)"** Scalar tag:
  - `POST /courses/{courseId}/announcements` — post an announcement with a client-generated
    idempotency token; re-submission with the same token is detected and rejected before any
    event is appended.
  - `POST /courses/{courseId}/announcements/{idempotencyToken}/retract` — retract an
    announcement; stores a `CourseAnnouncementRetractedEvent` carrying the original token,
    which frees the token for reuse (the `IdempotencyTokenWasUsed` projection folds
    `Posted → true`, then `Retracted → false`, so no handler changes are required).
  The feature demonstrates that idempotency can be enforced via a tag-scoped Decision Model
  projection without any domain-level uniqueness constraint — the token is the sole guard.
  Includes `CourseAnnouncementProjections` (`CourseExists` + `IdempotencyTokenWasUsed`) and
  `CourseAnnouncementRetractionProjection` (`RetractableAnnouncement`).
- **13 unit tests** in `Opossum.Samples.CourseManagement.UnitTests` covering all projection
  initial states, query tag scoping, apply logic, and the full `Post → Retract → Post` token
  reuse cycle — no I/O required.
- **9 integration tests** in `Opossum.Samples.CourseManagement.IntegrationTests` covering
  first post, re-submission detection, non-existent course, retraction, double retraction,
  and both same-token and new-token re-post after retraction.
- **README section "Idempotency Tokens — Prevent Record Duplication"** added under
  "Decision Model Projections" documenting the pattern with a self-contained code example.
- **Event-Sourced Aggregate example in `Opossum.Samples.CourseManagement`** — implements the
  DCB aggregate pattern from <https://dcb.events/examples/event-sourced-aggregate/#dcb-approach>
  using Opossum's existing API. Three new endpoints under the **"Aggregate (Event-Sourced)"**
  Scalar tag (`POST /courses/aggregate`, `PATCH /courses/aggregate/{id}/capacity`,
  `POST /courses/aggregate/{id}/subscriptions`) demonstrate `CourseAggregate` (pure C#,
  no Opossum machinery) and `CourseAggregateRepository` (tag-scoped `AppendCondition` as the
  DCB optimistic lock). Reuses the existing `CourseCreatedEvent`,
  `CourseStudentLimitModifiedEvent`, and `StudentEnrolledToCourseEvent` — both approaches
  share a single unified event log. The sample is a living side-by-side comparison of the
  DCB Decision Model pattern and the Event-Sourced Aggregate pattern; pick one for a real
  application.
- **22 unit tests for `CourseAggregate`** in `Opossum.Samples.CourseManagement.UnitTests`
  covering all factories, business methods, recorded-event accumulation/flushing, and version
  semantics — no I/O required.
- **9 integration tests for the aggregate endpoints** in
  `Opossum.Samples.CourseManagement.IntegrationTests`.

- **`ReadLastBenchmarks`** — new BenchmarkDotNet suite in `Opossum.BenchmarkTests` covering
  `ReadLastAsync` at three store scales (100 / 1K / 10K events) for event-type queries,
  tag queries, and `Query.All()`. Validates the single-file-read invariant and surfaces the
  `GetAllPositionsAsync` full-array allocation cost when using `Query.All()` with `ReadLastAsync`.
- **`IEventStore.ReadLastAsync(Query, CancellationToken)`**
  event with the highest sequence position matching the given query, or `null` when no
  events match. Only a single event file is read from storage regardless of total matches,
  making it significantly more efficient than `ReadAsync([Descending])[0]` for large event
  streams.
  Enables the **consecutive-sequence DCB pattern** (e.g. invoice numbering without gaps):
  read the last `InvoiceCreatedEvent` → derive the next number → append with
  `AfterSequencePosition = last?.Position` so any concurrent invoice append is detected
  and retried. `null` from `ReadLastAsync` maps directly to `AfterSequencePosition = null`,
  which rejects the append if *any* matching event already exists — covering the
  "first-ever invoice" bootstrap invariant for free.
- **`EventStore.ReadLast` OpenTelemetry activity** — `ReadLastAsync` emits a dedicated
  `EventStore.ReadLast` activity with `db.operation = "read_last"` and `opossum.event_count`
  tag, consistent with the existing `EventStore.Read` activity.
- **Invoice feature in `Opossum.Samples.CourseManagement`** — demonstrates the
  `ReadLastAsync` consecutive-sequence pattern end-to-end: `POST /invoices` creates invoices
  with a guaranteed gap-free number enforced by DCB; `GET /invoices` and
  `GET /invoices/{invoiceNumber}` expose the `InvoiceProjection` read model.

---

## [0.4.0-preview.1] - 2026-02-26

### Known Issues
- **Crash-recovery position collision (pre-existing since v0.1.0)** — a process crash
  between step 7 (event files written) and step 9 (ledger updated) leaves orphaned event
  files on disk at positions the ledger does not record. On the next append, those positions
  are reallocated and the orphaned files are silently overwritten. `WriteProtectEventFiles`
  does not guard against this because the write path strips the `ReadOnly` attribute before
  every overwrite. Full analysis in
  [`docs/limitations/crash-recovery-position-collision.md`](docs/limitations/crash-recovery-position-collision.md).
  Fix is tracked for 0.5.0.

### Fixed
- **Index files not flushed to disk when `FlushEventsImmediately = true`** — `PositionIndexFile.WritePositionsAsync`
  now calls `RandomAccess.FlushToDisk` on the temp file before the atomic rename when the flush flag is set,
  matching the durability guarantee already provided by event and ledger files. Previously, on a power failure
  after an append, event-type and tag index queries could miss the last batch of appended events until a
  manual reindex. `EventTypeIndex`, `TagIndex`, `IndexManager`, and `FileSystemEventStore` were updated to
  propagate the `FlushEventsImmediately` option through the full index write path.
- **`DeleteStoreAsync` race condition with `AppendAsync`** — `DeleteStoreAsync` now acquires
  `_appendLock` before touching the store directory. Previously a concurrent `AppendAsync`
  could encounter `IOException`/`DirectoryNotFoundException` mid-write because the deletion
  bypassed the in-process semaphore entirely. The double-checked pattern (fast-path existence
  check before the lock, re-check inside) ensures both concurrent deletes and the
  append-vs-delete race complete cleanly.

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
- **Flaky `DescendingPerformanceTests` integration test** — the test compared ascending vs. descending
  read times without a warm-up pass. The ascending measurement benefited from dirty write-cache pages
  left by `AppendAsync`, while the descending measurement could hit actual disk I/O, producing a
  non-deterministic ratio. Fixed by adding a warm-up read before both timed measurements so both start
  from an equally warm OS page-cache state. The ratio threshold was also widened from 2× to 4× to
  absorb scheduler jitter and GC pauses in a test environment while still catching the original
  ~12× regression reliably.

### Benchmarks

Benchmark run `20260226` compared against `20260225/rerun1` (same branch, post-ADR-005 baseline):

| Suite | 20260225/rerun1 | 20260226 | Δ | Notes |
|---|---:|---:|---|---|
| **Single event (with flush)** | 9.8 ms | 17.2 ms | **+75.6 %** | ✅ correctness fix — index files now flushed |
| **Batch 10 events (with flush)** | 55.2 ms | 127.9 ms | **+131.9 %** | ✅ correctness fix — true durability cost |
| Single event (no flush) | 4.611 ms | 4.584 ms | −0.6 % | noise |
| Batch 10 events (no flush) | 36.8 ms | 36.6 ms | −0.5 % | noise |
| Batch 20 events (no flush) | 78.2 ms | 73.2 ms | **−6.4 %** | improved |
| Batch 50 / 100 (no flush) | 194 / 406 ms | 224 / 449 ms | +10–16 % | ⚠️ under investigation; acceptable for release |
| **Multiple event types — 1K** | **66.6 ms** | **64.7 ms** | **−2.8 %** | ✅ +43 % regression from 20260225 fully resolved |
| Event type — 10K events | 208.3 ms | 200.9 ms | **−3.6 %** | improved |
| Tag query — 1K events | 10.9 ms | 10.5 ms | **−4.1 %** | improved |
| Query.All() — 10K events | 828.2 ms | 801.8 ms | **−3.2 %** | improved |
| Real-world: Payment events | 4,453 μs | 4,062 μs | **−8.8 %** | improved; prior +17 % regression resolved |
| Descending order vs ascending | 1.0003× | 0.9997× | ≈ 0 | ✅ parity |
| Parallel rebuild — sequential | 369.7 ms | 319.9 ms | **−13.5 %** | improved (async refactor) |
| Incremental projection update alloc | ~12 KB | **0 B** | 100 % | ✅ zero-allocation hot path |

**Flush regressions are expected and correct.** The `FlushEventsImmediately = true` path previously
omitted `FlushToDisk` calls on event-type and tag index temp files. Users who enabled this flag
were silently not receiving the full durability guarantee. The new numbers represent the true
on-disk cost: ~17 ms per single-event flush and ~128 ms per 10-event batch flush on SSD.

The +10–16 % regression on large no-flush batches (50, 100 events) has no corresponding
write-path change in this release. It is within the acceptable range for the target use case
(< 100 events/day average, SMB/on-premises) and is tracked as a follow-up investigation item
rather than a release blocker.

Full analysis: `docs/benchmarking/results/20260226/ANALYSIS.md`

---

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

[0.1.0-preview.1]: https://github.com/majormartintibor/Opossum/releases/tag/v0.1.0-preview.1
