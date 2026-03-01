# DataSeeder Redesign Plan

**Status:** Draft  
**Scope:** `Opossum.Samples.DataSeeder`  
**Motivation:** Feature coverage gaps + performance ceiling for large-scale manual testing

---

## 1. Problem Statement

The DataSeeder has two independent but related problems:

### 1.1 Feature Coverage Gaps

The sample application has grown significantly since the seeder was last updated. Three entire DCB-pattern examples have no corresponding seed data, making those endpoints effectively untestable through manual exploration:

| Feature Area | Events Missing from Seeder | DCB Pattern Demonstrated |
|---|---|---|
| **Course Announcements** | `CourseAnnouncementPostedEvent`, `CourseAnnouncementRetractedEvent` | Idempotency / Prevent Record Duplication |
| **Exam Registration Tokens** | `ExamRegistrationTokenIssuedEvent`, `ExamRegistrationTokenRedeemedEvent`, `ExamRegistrationTokenRevokedEvent` | Opt-In Token |
| **Course Books** | `CourseBookDefinedEvent`, `CourseBookPriceChangedEvent`, `CourseBookPurchasedEvent`, `CourseBooksOrderedEvent` | Dynamic Product Price (F1, F2, F3) |

### 1.2 Performance Ceiling

The current seeder is too slow to produce a database with millions of records for large-scale manual performance testing.

**Root cause — read-before-write on every single event:**

| Phase | Mechanism | Per-event cost |
|---|---|---|
| Students, Courses, Tier upgrades, Capacity changes | `IEventStore.AppendAsync(event, null)` | ✅ Write only |
| **Enrollments** | `IMediator.InvokeAsync(EnrollStudentToCourse)` | ❌ 3 index reads + lock + write |
| **Invoices** | `IMediator.InvokeAsync(CreateInvoice)` | ❌ Full table scan of all invoices + lock + write |

The enrollment handler calls `BuildDecisionModelAsync` with three projections — `CourseCapacity`, `StudentEnrollmentLimit`, and `AlreadyEnrolled` — before every single append. With 1 million enrollments this is O(n) index reads. The invoice handler reads all existing invoice events to find the last sequence number, making it O(n²) total for a large run.

**In addition, none of the three new feature areas would ever be feasible through the mediator path**, because:
- Idempotency tokens require a read-before-write per announcement
- Exam token lifecycle requires read-before-write per token operation  
- Dynamic price validation requires reading current price state before every purchase

At millions of events, even the O(1) `AppendAsync` path with the cross-process lock is too slow because the **index update strategy is also O(n)** — each `AddPositionAsync` call loads the entire index file, appends one position, and rewrites the whole file.

---

## 2. Current Storage Architecture

Understanding the on-disk layout is central to the redesign. Opossum uses this directory tree:

```
{RootPath}/
  {StoreName}/
    .ledger                              ← JSON: { lastSequencePosition, eventCount }
    events/
      0000000001.json                    ← SequencedEvent (see §2.1)
      0000000002.json
      ...
    Indices/
      EventType/
        StudentRegisteredEvent.json      ← JSON: { positions: [1, 5, 12, ...] }
        CourseCreatedEvent.json
        ...
      Tags/
        studentId_<guid>.json            ← JSON: { positions: [3, 7, ...] }
        courseId_<guid>.json
        ...
```

### 2.1 Event File Format

```json
{
  "event": {
    "eventType": "StudentRegisteredEvent",
    "event": {
      "$type": "Opossum.Samples.CourseManagement.Events.StudentRegisteredEvent, Opossum.Samples.CourseManagement",
      "studentId": "...",
      "firstName": "Emma",
      "lastName": "Smith",
      "email": "emma.smith@privateschool.edu"
    },
    "tags": [
      { "key": "studentEmail", "value": "emma.smith@privateschool.edu" },
      { "key": "studentId",    "value": "<guid>" }
    ]
  },
  "position": 1,
  "metadata": {
    "timestamp": "2024-07-15T08:32:11+00:00"
  }
}
```

### 2.2 Index File Format

```json
{
  "positions": [1, 5, 12, 47, 103]
}
```

Tag index file names are produced by sanitising the tag key and value (replacing invalid filename characters with `_`). A tag `studentId: <guid>` becomes `studentId_<guid>.json`.

---

## 3. Options Analysis

### Option A — Larger Batch `AppendAsync` (Incremental)

Generate all events in memory first, then call `IEventStore.AppendAsync(events[], null)` in chunks. No changes to the Opossum library.

| | |
|---|---|
| ✅ | Zero library changes |
| ✅ | One cross-process lock per batch instead of per event |
| ✅ | Correct indices guaranteed by Opossum |
| ❌ | Index update strategy unchanged: still one file rewrite per event per index key |
| ❌ | For 1 M events, the EventType index for `StudentEnrolledToCourseEvent` is loaded and rewritten 1 M times within the batch loop |
| ❌ | Does not solve the fundamental O(n) index rebuild problem |

**Verdict:** Good quick win for small/medium datasets (< 100 K events). Not sufficient for millions.

---

### Option B — `InternalsVisibleTo` (Library Access)

Add `[assembly: InternalsVisibleTo("Opossum.Samples.DataSeeder")]` to `Opossum.csproj` and let the DataSeeder use `EventFileManager`, `LedgerManager`, and `IndexManager` directly.

| | |
|---|---|
| ✅ | Single file-format source of truth stays in Opossum |
| ✅ | No new public API surface |
| ❌ | The internal components are still per-event oriented; `IndexManager.AddEventToIndicesAsync` still reads and rewrites each index file per call |
| ❌ | Would require refactoring the internal components to support bulk mode anyway |
| ❌ | Tight coupling between a sample tool and library internals |

**Verdict:** Solves the format-duplication concern but does not solve the performance problem without further work on the internal components.

---

### Option C — New Library API: `IBulkEventAppender` (Clean Architecture)

Extend `IEventStoreMaintenance` (or add a new interface) with a `BulkSeedAsync` method that:
1. Accepts a pre-built list of `SequencedEvent` objects (position, event, metadata all pre-assigned)
2. Writes all event files concurrently
3. Builds all index structures **in memory** across the full batch
4. Flushes each index file exactly **once** at the end

| | |
|---|---|
| ✅ | Cleanest architecture — library manages its own format |
| ✅ | Maximum write performance (parallel file I/O + single index flush) |
| ✅ | Can be reused by future tooling (migration tools, test fixtures, etc.) |
| ❌ | Adds to the library's surface area |
| ❌ | The interface is strictly write-only — the caller must guarantee data integrity (no DCB enforcement by design) |

---

### Option D — Standalone `DirectEventWriter` in the DataSeeder (Maximum Performance)

Implement the full write path inside `Opossum.Samples.DataSeeder` using `System.IO` and `System.Text.Json` directly. Build all index structures in memory, then flush all files concurrently at the very end.

| | |
|---|---|
| ✅ | Maximum possible performance |
| ✅ | Zero changes to the Opossum library |
| ✅ | DataSeeder is a dev tool in the same repository — format changes are visible at the same commit |
| ⚠️ | Replicates the file-format knowledge (mitigated: it is fully documented in §2 above) |
| ⚠️ | If the storage format ever changes, the seeder must be updated — acceptable for a dev tool |

---

## 4. Recommended Design

**Primary recommendation: Option D (standalone `DirectEventWriter`) for the performance path, combined with full feature coverage across all DCB examples.**

**Rationale:**
- The DataSeeder is a developer tool, not a production dependency. Tight format coupling to the library is acceptable and manageable within the same repository.
- Option C (library API) is the architecturally cleanest choice but extends the library's public surface with seeding-specific concerns. Unless bulk-import becomes a general library feature (future roadmap item), this is premature.
- Option D lets us implement a truly zero-overhead write path: parallel file I/O, in-memory index accumulation, and a single flush per index file regardless of how many events land in it.

**For small datasets (< ~50 K events)**, Option A (batch `AppendAsync` with no condition) is a perfectly valid simpler path and should be offered as an opt-in mode (`--use-event-store`).

---

## 5. Architecture of the Redesigned Seeder

The redesigned seeder separates concerns into three independent layers:

```
┌────────────────────────────────────────────────────────────┐
│                    LAYER 1: GENERATORS                      │
│   Pure C#, no I/O.  All business invariants enforced here.  │
│                                                              │
│  StudentGenerator     CourseGenerator     BookGenerator      │
│  AnnouncementGen      ExamTokenGenerator  InvoiceGenerator   │
└───────────────────────────┬────────────────────────────────┘
                            │ IReadOnlyList<SeedEvent>
                            ▼
┌────────────────────────────────────────────────────────────┐
│                   LAYER 2: ORCHESTRATOR                      │
│   Coordinates generators, assigns positions, sorts by       │
│   timestamp, manages cross-feature shared state.            │
│                                                              │
│                     SeedPlan                                 │
└───────────────────────────┬────────────────────────────────┘
                            │ IReadOnlyList<SequencedSeedEvent>
                            ▼
┌────────────────────────────────────────────────────────────┐
│                    LAYER 3: WRITER                           │
│   I/O only — no domain logic.  Two implementations:         │
│                                                              │
│  DirectEventWriter  ←── recommended (§4)                    │
│  EventStoreWriter   ←── Option A fallback (--use-event-store)│
└────────────────────────────────────────────────────────────┘
```

### 5.1 Layer 1 — Generators

Each generator is a stateless class responsible for producing a list of `SeedEvent` records for one domain area. Generators receive a shared `SeedContext` that contains the in-memory state produced by earlier generators (student list, course list, book list, etc.) and a seeded `Random` instance.

**Invariants that generators must enforce in pure code (no I/O):**

| Generator | Key invariants |
|---|---|
| `StudentGenerator` | Unique emails; tier distribution matches config percentages |
| `CourseGenerator` | Unique IDs; capacity within size-category bounds |
| `TierUpgradeGenerator` | Only upgrades students that are not already `Master` |
| `CapacityChangeGenerator` | New capacity ≥ 10 |
| `EnrollmentGenerator` | No duplicate student-course pair; course capacity respected; student tier limit respected; produces `StudentEnrolledToCourseEvent` |
| `InvoiceGenerator` | Invoice numbers are sequential integers starting at 1; the generator maintains a counter — no read required |
| `AnnouncementGenerator` | Each announcement has a unique `AnnouncementId` and a unique `IdempotencyToken`; ~20% of announcements are also retracted (retraction event follows the post event) |
| `ExamTokenGenerator` | Each token has a unique `TokenId`; ~70% are redeemed, ~10% revoked, ~20% remain open; redeemed and revoked token events must be ordered after the issued event in the timeline |
| `CourseBookGenerator` | Books are defined before they are priced or purchased; price changes must be for existing books; purchases use the current price at the time of purchase (simple: use the price from the `CourseBookDefinedEvent` or the latest `CourseBookPriceChangedEvent` in the in-memory state) |

### 5.2 Layer 2 — SeedPlan Orchestrator

The `SeedPlan` class:
1. Runs all generators in dependency order
2. Collects all `SeedEvent` objects from every generator into one flat list
3. Sorts the list by `Timestamp` (preserving relative ordering within the same millisecond via a stable sort)
4. Assigns sequential `Position` values (1, 2, 3, …) after the sort

This produces a deterministic, temporally consistent event stream where global ordering matches the timestamps — exactly as a real production event store would look.

### 5.3 Layer 3 — DirectEventWriter

The `DirectEventWriter`:
1. Creates the directory structure (`events/`, `Indices/EventType/`, `Indices/Tags/`)
2. Accumulates all index data in-memory: `Dictionary<string, SortedSet<long>>` keyed by index filename
3. Writes all event files concurrently (configurable parallelism, default: `Environment.ProcessorCount`)
4. Writes all index files and the `.ledger` file sequentially after events (indices are small)
5. Optionally sets `FileAttributes.ReadOnly` on committed event files (matches Opossum's `WriteProtect` option)

**Serialisation:** The `DirectEventWriter` uses `System.Text.Json` with the same options as `JsonEventSerializer` in Opossum:
- `WriteIndented = true`
- `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`
- `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull`
- Polymorphic `IEvent` serialisation via `$type` property (assembly-qualified type name)

The `$type` value format is: `"<full-type-name>, <assembly-name>"` — e.g.  
`"Opossum.Samples.CourseManagement.Events.StudentRegisteredEvent, Opossum.Samples.CourseManagement"`

---

## 6. Complete Event Catalogue

This section defines every event the redesigned seeder must produce, its required tags, and the temporal window for its timestamp.

### 6.1 Existing Events (must be preserved)

| Event | Tags | Timestamp window |
|---|---|---|
| `StudentRegisteredEvent` | `studentEmail:{email}`, `studentId:{id}` | 365–180 days ago |
| `CourseCreatedEvent` | `courseId:{id}` | 365–200 days ago |
| `StudentSubscriptionUpdatedEvent` | `studentId:{id}` | 180–30 days ago |
| `CourseStudentLimitModifiedEvent` | `courseId:{id}` | 150–60 days ago |
| `StudentEnrolledToCourseEvent` | `courseId:{id}`, `studentId:{id}` | 120–1 days ago |
| `InvoiceCreatedEvent` | `invoiceNumber:{n}` | 90–1 days ago |

### 6.2 New Events — Course Announcements

| Event | Tags | Timestamp window | Notes |
|---|---|---|---|
| `CourseAnnouncementPostedEvent` | `courseId:{id}`, `idempotency:{token}` | 90–30 days ago | `AnnouncementId = Guid.NewGuid()`, unique `IdempotencyToken` per announcement |
| `CourseAnnouncementRetractedEvent` | `courseId:{id}`, `idempotency:{token}` | Posted timestamp + 1–7 days | ~20% of posted announcements; must use the same `IdempotencyToken` as the corresponding Posted event |

**Seeding quantities (defaults):** ~3 announcements per course; ~20% retracted.

### 6.3 New Events — Exam Registration Tokens

| Event | Tags | Timestamp window | Notes |
|---|---|---|---|
| `ExamRegistrationTokenIssuedEvent` | `examToken:{tokenId}`, `examId:{examId}`, `courseId:{courseId}` | 60–14 days ago | One exam per course; multiple tokens per exam |
| `ExamRegistrationTokenRedeemedEvent` | `examToken:{tokenId}`, `examId:{examId}`, `studentId:{studentId}` | Issued timestamp + 1–5 days | ~70% of tokens; student must be enrolled in the course |
| `ExamRegistrationTokenRevokedEvent` | `examToken:{tokenId}`, `examId:{examId}` | Issued timestamp + 1–3 days | ~10% of tokens; cannot be both redeemed and revoked |

**Seeding quantities (defaults):** ~2 exams per course; ~5 tokens per exam.

### 6.4 New Events — Course Books

| Event | Tags | Timestamp window | Notes |
|---|---|---|---|
| `CourseBookDefinedEvent` | `bookId:{id}` | 300–250 days ago | Each book has title, author, ISBN, initial price |
| `CourseBookPriceChangedEvent` | `bookId:{id}` | 200–100 days ago | ~40% of books get one price change |
| `CourseBookPurchasedEvent` | `bookId:{id}`, `studentId:{id}` | 100–7 days ago | Single book purchase; `PricePaid` must equal the current price at the time of purchase (use in-memory price state) |
| `CourseBooksOrderedEvent` | `bookId:{id}` (per item), `studentId:{id}` | 100–7 days ago | Multi-book order (2–4 books per order); all prices validated against in-memory price state |

**Seeding quantities (defaults):** 30 books total; ~20 purchases per book; ~50 multi-book orders.

---

## 7. SeedingConfiguration Extensions

The `SeedingConfiguration` class requires new properties:

```csharp
// Existing
public int StudentCount { get; set; } = 1_000;
public int CourseCount { get; set; } = 100;
public int InvoiceCount { get; set; } = 500;

// New — Course Announcements
public int AnnouncementsPerCourse { get; set; } = 3;
public int AnnouncementRetractionPercentage { get; set; } = 20;

// New — Exam Registration Tokens  
public int ExamsPerCourse { get; set; } = 2;
public int TokensPerExam { get; set; } = 5;
public int TokenRedemptionPercentage { get; set; } = 70;
public int TokenRevocationPercentage { get; set; } = 10;

// New — Course Books
public int CourseBookCount { get; set; } = 30;
public int CourseBooksWithPriceChangePercentage { get; set; } = 40;
public int SingleBookPurchasesPerBook { get; set; } = 20;
public int MultiBookOrders { get; set; } = 50;

// New — Writer mode
public bool UseEventStoreWriter { get; set; } = false; // true = Option A fallback

// New — Performance tuning (DirectEventWriter only)
public int WriteParallelism { get; set; } = 0; // 0 = Environment.ProcessorCount
```

---

## 8. CLI Interface (Proposed)

```
Usage: dotnet run [options]

Core:
  --students <n>         Number of students (default: 1000)
  --courses <n>          Number of courses (default: 100)
  --invoices <n>         Number of invoices (default: 500)
  --reset                Delete existing database before seeding
  --no-confirm           Skip confirmation prompt

Feature quantities:
  --announcements-per-course <n>   (default: 3)
  --exams-per-course <n>           (default: 2)
  --tokens-per-exam <n>            (default: 5)
  --books <n>                      Number of course books (default: 30)

Performance / writer:
  --use-event-store      Use IEventStore.AppendAsync instead of direct file writer
  --parallelism <n>      File write parallelism (default: cpu count)

Presets:
  --preset small         ~10 K events  (students:200, courses:30, ...)
  --preset medium        ~100 K events (students:1000, courses:100, ...)
  --preset large         ~1 M events   (students:5000, courses:500, ...)
  --preset stress        ~10 M events  (students:20000, courses:2000, ...)
```

---

## 9. Implementation Phases

### Phase 1 — Foundation (no new features yet)

1. Extract `IEventWriter` interface with two implementations: `DirectEventWriter` and `EventStoreWriter`
2. Move the five existing generators into dedicated generator classes (`StudentGenerator`, `CourseGenerator`, etc.)
3. Implement `SeedPlan` orchestrator (in-memory sort + position assignment)
4. Wire up `DirectEventWriter` and verify it produces a valid Opossum database by running the sample app against the output
5. Benchmark: compare wall-clock time for 1 M enrollments between `DirectEventWriter` and current mediator path

### Phase 2 — Feature Coverage

6. Add `AnnouncementGenerator` + `CourseAnnouncementPostedEvent` / `CourseAnnouncementRetractedEvent`
7. Add `ExamTokenGenerator` + three exam token event types
8. Add `CourseBookGenerator` + four course book event types
9. Update `SeedingConfiguration` with all new properties
10. Extend CLI argument parsing for new flags and presets

### Phase 3 — Polish

11. Update `README.md` in the DataSeeder project
12. Update `CHANGELOG.md`
13. Verify end-to-end: seed a `--preset large` database, start the sample app, exercise all endpoints via Swagger UI

---

## 10. Correctness Guarantees

The redesigned seeder does not use DCB enforcement. Instead, correctness is guaranteed by the generators:

| What DCB enforced at runtime | What the generator does instead |
|---|---|
| Course exists before enrollment | Enrollment generator only picks courses from `SeedContext.Courses` |
| Student exists before enrollment | Enrollment generator only picks students from `SeedContext.Students` |
| Course not over capacity | Enrollment generator tracks `courseEnrollments[courseId]` counter |
| Student not over tier limit | Enrollment generator tracks `studentEnrollments[studentId]` counter |
| No duplicate enrollment | Enrollment generator tracks `enrolledPairs: HashSet<(Guid,Guid)>` |
| Idempotency token not reused | Announcement generator assigns a fresh `Guid.NewGuid()` per announcement |
| Exam token not redeemed twice | Token generator sets aside unique tokens per redemption |
| Book exists before price change | Book generator builds list of defined books, price-change generator draws from that list |
| Price at purchase matches stored price | Book purchase generator maintains `currentPrice[bookId]` dictionary, updated as it applies price-change events |
| Invoice numbers are sequential | Invoice generator maintains a simple `int nextNumber = 1` counter |

All of these checks are O(1) dictionary / hashset lookups. **No event store reads are required during generation.**

---

## 11. Risks and Mitigations

| Risk | Likelihood | Mitigation |
|---|---|---|
| Opossum changes the on-disk format | Low — format is stable | Both are in the same repo; a format-breaking change requires a deliberate version bump and the seeder test (§9, step 4) will immediately catch the drift |
| Generated data is semantically invalid (e.g. orphan exam tokens) | Low | Generator invariant table in §10 is exhaustive; unit tests for each generator with fixed `Random` seed |
| Index files become corrupted by the direct writer | Low | `DirectEventWriter` uses the same temp-file + atomic rename strategy as `EventFileManager` |
| Parallelism causes position gaps | N/A | Positions are assigned in-memory by `SeedPlan` before any I/O begins; writers receive pre-assigned positions |

---

## 12. Open Questions

1. **Should the default student/course counts be raised** now that the direct writer makes large datasets feasible? Current defaults are 350/75; suggested new defaults are 1 000/100. Raising them makes the sample app feel more realistic out of the box.

2. **Should `CourseBooksOrderedEvent` tag all book IDs?** The current `OrderCourseBooksCommand` tags `bookId` per item — the seeder must replicate this. This means one `CourseBooksOrderedEvent` produces N tag index entries (one per book in the order). Confirm this is the intended behaviour before implementing.

3. **Exam-to-course mapping:** The current domain model has no `ExamCreatedEvent` — an exam only exists implicitly through its token events. The seeder will need to decide exam IDs upfront (generated by the seeder, not recorded in the event store). Is this acceptable or should an `ExamCreatedEvent` / `ExamScheduledEvent` be added to the sample domain first?

4. **Preset large dataset targets:** The `stress` preset (10 M events) implies ~20 000 students and ~2 000 courses. Are these the right numbers, or should the presets be calibrated against specific benchmark targets?
