# DataSeeder Redesign Plan

**Status:** Implemented
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

**Root cause â€” read-before-write on every single event:**

| Phase | Mechanism | Per-event cost |
|---|---|---|
| Students, Courses, Tier upgrades, Capacity changes | `IEventStore.AppendAsync(event, null)` | âś… Write only |
| **Enrollments** | `IMediator.InvokeAsync(EnrollStudentToCourse)` | âťŚ 3 index reads + lock + write |
| **Invoices** | `IMediator.InvokeAsync(CreateInvoice)` | âťŚ Full table scan of all invoices + lock + write |

The enrollment handler calls `BuildDecisionModelAsync` with three projections â€” `CourseCapacity`, `StudentEnrollmentLimit`, and `AlreadyEnrolled` â€” before every single append. With 1 million enrollments this is O(n) index reads. The invoice handler reads all existing invoice events to find the last sequence number, making it O(nÂ˛) total for a large run.

**In addition, none of the three new feature areas would ever be feasible through the mediator path**, because:
- Idempotency tokens require a read-before-write per announcement
- Exam token lifecycle requires read-before-write per token operation  
- Dynamic price validation requires reading current price state before every purchase

At millions of events, even the O(1) `AppendAsync` path with the cross-process lock is too slow because the **index update strategy is also O(n)** â€” each `AddPositionAsync` call loads the entire index file, appends one position, and rewrites the whole file.

---

## 2. Current Storage Architecture

Understanding the on-disk layout is central to the redesign. Opossum uses this directory tree:

```
{RootPath}/
  {StoreName}/
    .ledger                              â† JSON: { lastSequencePosition, eventCount }
    events/
      0000000001.json                    â† SequencedEvent (see Â§2.1)
      0000000002.json
      ...
    Indices/
      EventType/
        StudentRegisteredEvent.json      â† JSON: { positions: [1, 5, 12, ...] }
        CourseCreatedEvent.json
        ...
      Tags/
        studentId_<guid>.json            â† JSON: { positions: [3, 7, ...] }
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

### Option A â€” Larger Batch `AppendAsync` (Incremental)

Generate all events in memory first, then call `IEventStore.AppendAsync(events[], null)` in chunks. No changes to the Opossum library.

| | |
|---|---|
| âś… | Zero library changes |
| âś… | One cross-process lock per batch instead of per event |
| âś… | Correct indices guaranteed by Opossum |
| âťŚ | Index update strategy unchanged: still one file rewrite per event per index key |
| âťŚ | For 1 M events, the EventType index for `StudentEnrolledToCourseEvent` is loaded and rewritten 1 M times within the batch loop |
| âťŚ | Does not solve the fundamental O(n) index rebuild problem |

**Verdict:** Good quick win for small/medium datasets (< 100 K events). Not sufficient for millions.

---

### Option B â€” `InternalsVisibleTo` (Library Access)

Add `[assembly: InternalsVisibleTo("Opossum.Samples.DataSeeder")]` to `Opossum.csproj` and let the DataSeeder use `EventFileManager`, `LedgerManager`, and `IndexManager` directly.

| | |
|---|---|
| âś… | Single file-format source of truth stays in Opossum |
| âś… | No new public API surface |
| âťŚ | The internal components are still per-event oriented; `IndexManager.AddEventToIndicesAsync` still reads and rewrites each index file per call |
| âťŚ | Would require refactoring the internal components to support bulk mode anyway |
| âťŚ | Tight coupling between a sample tool and library internals |

**Verdict:** Solves the format-duplication concern but does not solve the performance problem without further work on the internal components.

---

### Option C â€” New Library API: `IBulkEventAppender` (Clean Architecture)

Extend `IEventStoreMaintenance` (or add a new interface) with a `BulkSeedAsync` method that:
1. Accepts a pre-built list of `SequencedEvent` objects (position, event, metadata all pre-assigned)
2. Writes all event files concurrently
3. Builds all index structures **in memory** across the full batch
4. Flushes each index file exactly **once** at the end

| | |
|---|---|
| âś… | Cleanest architecture â€” library manages its own format |
| âś… | Maximum write performance (parallel file I/O + single index flush) |
| âś… | Can be reused by future tooling (migration tools, test fixtures, etc.) |
| âťŚ | Adds to the library's surface area |
| âťŚ | The interface is strictly write-only â€” the caller must guarantee data integrity (no DCB enforcement by design) |

---

### Option D â€” Standalone `DirectEventWriter` in the DataSeeder (Maximum Performance)

Implement the full write path inside `Opossum.Samples.DataSeeder` using `System.IO` and `System.Text.Json` directly. Build all index structures in memory, then flush all files concurrently at the very end.

| | |
|---|---|
| âś… | Maximum possible performance |
| âś… | Zero changes to the Opossum library |
| âś… | DataSeeder is a dev tool in the same repository â€” format changes are visible at the same commit |
| âš ď¸Ź | Replicates the file-format knowledge (mitigated: it is fully documented in Â§2 above) |
| âš ď¸Ź | If the storage format ever changes, the seeder must be updated â€” acceptable for a dev tool |

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
â”Śâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
â”‚                    LAYER 1: GENERATORS                      â”‚
â”‚   Pure C#, no I/O.  All business invariants enforced here.  â”‚
â”‚                                                              â”‚
â”‚  StudentGenerator     CourseGenerator     BookGenerator      â”‚
â”‚  AnnouncementGen      ExamTokenGenerator  InvoiceGenerator   â”‚
â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¬â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
                            â”‚ IReadOnlyList<SeedEvent>
                            â–Ľ
â”Śâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
â”‚                   LAYER 2: ORCHESTRATOR                      â”‚
â”‚   Coordinates generators, assigns positions, sorts by       â”‚
â”‚   timestamp, manages cross-feature shared state.            â”‚
â”‚                                                              â”‚
â”‚                     SeedPlan                                 â”‚
â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¬â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
                            â”‚ IReadOnlyList<SequencedSeedEvent>
                            â–Ľ
â”Śâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
â”‚                    LAYER 3: WRITER                           â”‚
â”‚   I/O only â€” no domain logic.  Two implementations:         â”‚
â”‚                                                              â”‚
â”‚  DirectEventWriter  â†â”€â”€ recommended (Â§4)                    â”‚
â”‚  EventStoreWriter   â†â”€â”€ Option A fallback (--use-event-store)â”‚
â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
```

### 5.1 Layer 1 â€” Generators

Each generator is a stateless class responsible for producing a list of `SeedEvent` records for one domain area. Generators receive a shared `SeedContext` that contains the in-memory state produced by earlier generators (student list, course list, book list, etc.) and a seeded `Random` instance.

**Invariants that generators must enforce in pure code (no I/O):**

| Generator | Key invariants |
|---|---|
| `StudentGenerator` | Unique emails; tier distribution matches config percentages |
| `CourseGenerator` | Unique IDs; capacity within size-category bounds |
| `TierUpgradeGenerator` | Only upgrades students that are not already `Master` |
| `CapacityChangeGenerator` | New capacity â‰Ą 10 |
| `EnrollmentGenerator` | No duplicate student-course pair; course capacity respected; student tier limit respected; produces `StudentEnrolledToCourseEvent` |
| `InvoiceGenerator` | Invoice numbers are sequential integers starting at 1; the generator maintains a counter â€” no read required |
| `AnnouncementGenerator` | Each announcement has a unique `AnnouncementId` and a unique `IdempotencyToken`; ~20% of announcements are also retracted (retraction event follows the post event) |
| `ExamTokenGenerator` | Each token has a unique `TokenId`; ~70% are redeemed, ~10% revoked, ~20% remain open; redeemed and revoked token events must be ordered after the issued event in the timeline |
| `CourseBookGenerator` | Books are defined before they are priced or purchased; price changes must be for existing books; purchases use the current price at the time of purchase (simple: use the price from the `CourseBookDefinedEvent` or the latest `CourseBookPriceChangedEvent` in the in-memory state) |

### 5.2 Layer 2 â€” SeedPlan Orchestrator

The `SeedPlan` class:
1. Runs all generators in dependency order
2. Collects all `SeedEvent` objects from every generator into one flat list
3. Sorts the list by `Timestamp` (preserving relative ordering within the same millisecond via a stable sort)
4. Assigns sequential `Position` values (1, 2, 3, â€¦) after the sort

This produces a deterministic, temporally consistent event stream where global ordering matches the timestamps â€” exactly as a real production event store would look.

### 5.3 Layer 3 â€” DirectEventWriter

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

The `$type` value format is: `"<full-type-name>, <assembly-name>"` â€” e.g.  
`"Opossum.Samples.CourseManagement.Events.StudentRegisteredEvent, Opossum.Samples.CourseManagement"`

---

## 6. Complete Event Catalogue

This section defines every event the redesigned seeder must produce, its required tags, and the temporal window for its timestamp.

### 6.1 Existing Events (must be preserved)

| Event | Tags | Timestamp window |
|---|---|---|
| `StudentRegisteredEvent` | `studentEmail:{email}`, `studentId:{id}` | 365â€“180 days ago |
| `CourseCreatedEvent` | `courseId:{id}` | 365â€“200 days ago |
| `StudentSubscriptionUpdatedEvent` | `studentId:{id}` | 180â€“30 days ago |
| `CourseStudentLimitModifiedEvent` | `courseId:{id}` | 150â€“60 days ago |
| `StudentEnrolledToCourseEvent` | `courseId:{id}`, `studentId:{id}` | 120â€“1 days ago |
| `InvoiceCreatedEvent` | `invoiceNumber:{n}` | 90â€“1 days ago |

### 6.2 New Events â€” Course Announcements

| Event | Tags | Timestamp window | Notes |
|---|---|---|---|
| `CourseAnnouncementPostedEvent` | `courseId:{id}`, `idempotency:{token}` | 90â€“30 days ago | `AnnouncementId = Guid.NewGuid()`, unique `IdempotencyToken` per announcement |
| `CourseAnnouncementRetractedEvent` | `courseId:{id}`, `idempotency:{token}` | Posted timestamp + 1â€“7 days | ~20% of posted announcements; must use the same `IdempotencyToken` as the corresponding Posted event |

**Seeding quantities (defaults):** ~3 announcements per course; ~20% retracted.

### 6.3 New Events â€” Exam Registration Tokens

| Event | Tags | Timestamp window | Notes |
|---|---|---|---|
| `ExamRegistrationTokenIssuedEvent` | `examToken:{tokenId}`, `examId:{examId}`, `courseId:{courseId}` | 60â€“14 days ago | One exam per course; multiple tokens per exam |
| `ExamRegistrationTokenRedeemedEvent` | `examToken:{tokenId}`, `examId:{examId}`, `studentId:{studentId}` | Issued timestamp + 1â€“5 days | ~70% of tokens; student must be enrolled in the course |
| `ExamRegistrationTokenRevokedEvent` | `examToken:{tokenId}`, `examId:{examId}` | Issued timestamp + 1â€“3 days | ~10% of tokens; cannot be both redeemed and revoked |

**Seeding quantities (defaults):** ~2 exams per course; ~5 tokens per exam.

### 6.4 New Events â€” Course Books

| Event | Tags | Timestamp window | Notes |
|---|---|---|---|
| `CourseBookDefinedEvent` | `bookId:{id}`, `courseId:{id}` (when assigned) | 300â€“250 days ago | Each book has title, author, ISBN, initial price; the seeder assigns each book to one course (see Â§13.2) |
| `CourseBookPriceChangedEvent` | `bookId:{id}` | 200â€“100 days ago | ~40% of books get one price change |
| `CourseBookPurchasedEvent` | `bookId:{id}`, `studentId:{id}` | 100â€“7 days ago | Single book purchase; `PricePaid` must equal the current price at the time of purchase (use in-memory price state) |
| `CourseBooksOrderedEvent` | `bookId:{id}` (per item), `studentId:{id}` | 100â€“7 days ago | Multi-book order (2â€“4 books per order); all prices validated against in-memory price state |

**Seeding quantities (defaults):** 30 books total; ~20 purchases per book; ~50 multi-book orders.

---

## 7. SeedingConfiguration and Presets

### 7.1 Preset Definitions

The four interactive presets are defined as static factory methods on `SeedingPresets`. All per-entity multipliers (announcement count, exam count, etc.) are identical across presets â€” only the entity counts change. Estimated totals are calculated using the formula in Â§7.3.

| Preset | Students | Courses | Books | Invoices | Multi-book orders | Est. total events |
|---|---|---|---|---|---|---|
| **Small** | 40 | 8 | 8 | 30 | 8 | ~620 |
| **Medium** | 7,000 | 1,400 | 1,400 | 2,500 | 600 | ~104,000 |
| **Large** | 70,000 | 14,000 | 14,000 | 15,000 | 7,000 | ~1,030,000 |
| **Prod** | 350,000 | 70,000 | 70,000 | 75,000 | 35,000 | ~5,150,000 |

**Design goal â€” one book per course:** The `CourseBookCount` is set equal to `CourseCount` in every preset. This is a seeder-generation goal, not a domain invariant: the `CourseBookGenerator` will attempt to assign one book to every course and will succeed for ~100% of courses. Courses without a book are acceptable; books without a course are not generated.

> **Note on enrollment counts:** Enrollments are the largest single event type but are bounded by the smaller of *(students Ă— average tier max Ă— 80% utilisation)* and *(courses Ă— average capacity Ă— 80% utilisation)*. For Medium and above, course capacity becomes the binding constraint. The estimates above reflect the capacity-constrained path.

### 7.2 SeedingConfiguration Shape

```csharp
public class SeedingConfiguration
{
    // --- Entity counts (set by preset or overridden by user) ---
    public int StudentCount          { get; set; } = 10_000;
    public int CourseCount           { get; set; } = 2_000;
    public int CourseBookCount       { get; set; } = 200;
    public int InvoiceCount          { get; set; } = 1_000;
    public int MultiBookOrders       { get; set; } = 200;

    // --- Per-entity multipliers (shared across all presets) ---
    public int AnnouncementsPerCourse           { get; set; } = 3;
    public int AnnouncementRetractionPercentage { get; set; } = 20;
    public int ExamsPerCourse                   { get; set; } = 2;
    public int TokensPerExam                    { get; set; } = 5;
    public int TokenRedemptionPercentage        { get; set; } = 70;
    public int TokenRevocationPercentage        { get; set; } = 10;
    public int TierUpgradePercentage            { get; set; } = 30;
    public int CapacityChangePercentage         { get; set; } = 20;
    public int PriceChangePercentage            { get; set; } = 40;
    public int SingleBookPurchasesPerBook       { get; set; } = 20;

    // --- Tier distribution ---
    public int BasicTierPercentage        { get; set; } = 20;
    public int StandardTierPercentage     { get; set; } = 40;
    public int ProfessionalTierPercentage { get; set; } = 30;
    public int MasterTierPercentage       { get; set; } = 10;

    // --- Writer options ---
    public bool UseEventStoreWriter { get; set; } = false; // true = Option A fallback
    public int  WriteParallelism    { get; set; } = 0;     // 0 = Environment.ProcessorCount

    // --- Misc ---
    public bool ResetDatabase        { get; set; } = false;
    public bool RequireConfirmation  { get; set; } = true;
}
```

### 7.3 Event Count Estimation Formula

```
E_students  = StudentCount Ă— (1 + TierUpgrade% + avg_enrollments_per_student)
E_courses   = CourseCount  Ă— (1 + CapacityChange%
                              + AnnouncementsPerCourse Ă— (1 + Retraction%)
                              + ExamsPerCourse Ă— TokensPerExam Ă— (1 + Redemption% + Revocation%))
E_books     = CourseBookCount Ă— (1 + PriceChange% + SingleBookPurchasesPerBook)
              + MultiBookOrders
E_invoices  = InvoiceCount

Total â‰ E_students + E_courses + E_books + E_invoices
```

Average enrollment per student is **capacity-constrained** for all presets above Small:
```
avg_enrollments = min(avg_tier_max Ă— 0.8,
                      CourseCount Ă— avg_capacity / StudentCount)
```
where `avg_tier_max â‰ 7.9` and `avg_capacity â‰ 26.6`.

---

## 8. Console UX

When the seeder starts it presents an **interactive console menu** â€” no CLI flags required for the common case.

### 8.1 Startup Flow

```
đźŚ± Opossum Data Seeder
======================

Database: D:\Database\OpossumSampleApp

Select a dataset size:
  [1] Small   ~620 events       â€” explore the data model
  [2] Medium  ~104 000 events   â€” growing business, a few months of data
  [3] Large   ~1 030 000 events â€” established platform, 1-3 years of data
  [4] Prod    ~5 150 000 events â€” large-scale performance testing

Your choice (1-4): _
```

After the user picks a size, a second prompt:

```
Reset existing database? (y/N): _
```

Then a confirmation summary before seeding begins:

```
Configuration:
  Size:        Large
  Students:    70 000
  Courses:     14 000
  Books:       14 000
  Invoices:    15 000
  Est. events: ~1 030 000
  Reset:       NO
  Writer:      DirectEventWriter (parallel, 8 threads)

Proceed? (y/N): _
```

### 8.2 CLI Flags (Advanced / Automation)

For scripted runs (CI, benchmarks), flags bypass the interactive menu:

```
Usage: dotnet run -- [flags]

  --size <small|medium|large|prod>   Select a preset non-interactively
  --reset                            Delete existing data before seeding
  --no-confirm                       Skip all confirmation prompts
  --use-event-store                  Use IEventStore instead of DirectEventWriter
  --parallelism <n>                  File write threads (default: cpu count)
  --help
```

Example (CI seed for integration tests):
```bash
dotnet run -- --size small --reset --no-confirm
```

---

## 9. Implementation Phases

### Phase 1 â€” Foundation (no new features yet)

1. Extract `IEventWriter` interface with two implementations: `DirectEventWriter` and `EventStoreWriter`
2. Move the five existing generators into dedicated generator classes (`StudentGenerator`, `CourseGenerator`, etc.)
3. Implement `SeedPlan` orchestrator (in-memory sort + position assignment)
4. Wire up `DirectEventWriter` and verify it produces a valid Opossum database by running the sample app against the output
5. Benchmark: compare wall-clock time for 1 M enrollments between `DirectEventWriter` and current mediator path

### Phase 2 â€” Feature Coverage

6. Add `AnnouncementGenerator` + `CourseAnnouncementPostedEvent` / `CourseAnnouncementRetractedEvent`
7. Add `ExamTokenGenerator` + three exam token event types
8. Add `CourseBookGenerator` + four course book event types
9. Update `SeedingConfiguration` with all new properties
10. Extend CLI argument parsing for new flags and presets

### Phase 3 â€” Polish

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
| Opossum changes the on-disk format | Low â€” format is stable | Both are in the same repo; a format-breaking change requires a deliberate version bump and the seeder test (Â§9, step 4) will immediately catch the drift |
| Generated data is semantically invalid (e.g. orphan exam tokens) | Low | Generator invariant table in Â§10 is exhaustive; unit tests for each generator with fixed `Random` seed |
| Index files become corrupted by the direct writer | Low | `DirectEventWriter` uses the same temp-file + atomic rename strategy as `EventFileManager` |
| Parallelism causes position gaps | N/A | Positions are assigned in-memory by `SeedPlan` before any I/O begins; writers receive pre-assigned positions |

---

## 12. Decisions Log

All four open questions from the initial draft have been resolved.

### Q1 â€” Preset UI and scale targets âś… Resolved

**Decision:** Four presets selectable via an **interactive console menu** at startup (no CLI flags for the common path). The four sizes and their entity counts are defined in Â§7.1. The `prod` preset targets ~5 M events, not 10 M.

### Q2 â€” `CourseBooksOrderedEvent`: tag all book IDs? âś… Resolved

**Decision:** **Full tagging â€” replicate the command handler exactly.**

The actual `OrderCourseBooksCommand` emits one `studentId` tag plus one `bookId` tag per item in the order. Although no current projection or query uses the `bookId` tag on order events (the `CourseBookOrderHistoryProjection` is a persisted projection keyed by position that reads all purchase events without tag filtering), the seeder must produce data identical to what the real application produces. Full tagging keeps the database honest and future-proof.

Practical impact for the seeder: each multi-book order event (avg 3 books) creates 4 tag index entries instead of 1. At large preset scale (~50 K orders), this adds ~100 K tag index files â€” expected and acceptable.

### Q3 â€” Exam-to-course mapping: `ExamCreatedEvent` needed? âś… Resolved

**Decision:** No `ExamCreatedEvent` needed. The seeder assigns `examId` values upfront as `Guid.NewGuid()` â€” one per exam, stored in the `SeedContext`. Exams exist implicitly through their token events, exactly as the current domain model intends.

### Q4 â€” Preset calibration âś… Resolved

**Decision:** Presets are calibrated so that the total event count (all event types combined) matches the stated target. The enrollment count is the primary lever but is capacity-constrained for Medium and above â€” the course count is sized accordingly to provide enough total seats. See Â§7.1 for the final parameter table and Â§7.3 for the estimation formula.

---

## 13. Planned Read Model Features

Two new read endpoints are planned for the sample application. Both leverage the seeder's data and are documented here because they have direct implications for what the seeder must generate (Â§13.1 needs no changes; Â§13.2 requires a domain model extension).

---

### 13.1 Student's Purchased Books Projection

**Endpoint:** `GET /students/{studentId}/purchased-books`  
**Returns:** All books the given student has ever purchased (both individual purchases and shopping cart orders), deduplicated by `bookId`.

#### Domain model changes required

None. `CourseBookPurchasedEvent` and `CourseBooksOrderedEvent` already carry a `studentId` tag. âś…

#### Projection design

```
Type:     Persisted  (IProjectionDefinition<StudentPurchasedBooksState>)
Key:      studentId tag   â† same pattern as CourseBookCatalogProjection uses bookId tag
Events:   CourseBookPurchasedEvent, CourseBooksOrderedEvent
```

State shape:

```csharp
public sealed record PurchasedBookEntry(
    Guid   BookId,
    decimal TotalPaid,
    int     PurchaseCount,
    DateTimeOffset FirstPurchasedAt,
    DateTimeOffset LastPurchasedAt);

public sealed record StudentPurchasedBooksState(
    Guid StudentId,
    IReadOnlyList<PurchasedBookEntry> Books);
```

Fold logic:
- `CourseBookPurchasedEvent` â†’ upsert one entry (add new or increment existing `bookId` entry)
- `CourseBooksOrderedEvent` â†’ upsert one entry per item in the order

#### Seeder implications

None. The seeder already generates these events with `studentId` tags. The projection will rebuild correctly over any seeded database without any changes.

---

### 13.2 Course Book Buyers Projection

**Endpoint:** `GET /courses/{courseId}/book-buyers`  
**Returns:** The course name and all students who have purchased the course's textbook.

This feature requires establishing an explicit **course â†” book association** in the domain model, which does not currently exist.

#### Why the association is missing

`CourseBookDefinedEvent` currently carries only catalog metadata (title, author, ISBN, price). There is no `courseId` property or tag. Purchase events (`CourseBookPurchasedEvent`, `CourseBooksOrderedEvent`) consequently have no `courseId` either.

#### Domain model change: `CourseId` required on `CourseBookDefinedEvent`

`CourseId` is a **required, non-nullable property**. This is a breaking change to the event record and to `DefineCourseBookCommand` â€” both are updated together (we are in active development; no backward-compatibility concern):

```csharp
public sealed record CourseBookDefinedEvent(
    Guid    BookId,
    string  Title,
    string  Author,
    string  Isbn,
    decimal Price,
    Guid    CourseId) : IEvent;
```

`DefineCourseBookCommand` gains a matching `CourseId` parameter. The `DefineCourseBookCommandHandler` always emits a `courseId` tag on the event alongside the existing `bookId` tag.

#### Command handler changes: tag `courseId` on purchase events

For the persisted projection to be keyed by `courseId`, all purchase events must carry that tag. The `courseId` for a given `bookId` is already stored as a `CourseBookDefinedEvent` in the event store â€” we read it **inside the existing `BuildDecisionModelAsync` call** at no extra I/O cost.

**New projection factory** (added to `CourseBookPriceProjections`):

```csharp
public static IDecisionProjection<Guid?> CourseIdForBook(Guid bookId) =>
    new DecisionProjection<Guid?>(
        initialState: null,
        query: Query.FromItems(new QueryItem
        {
            EventTypes = [nameof(CourseBookDefinedEvent)],
            Tags = [new Tag("bookId", bookId.ToString())]
        }),
        apply: (_, evt) => evt.Event.Event is CourseBookDefinedEvent e ? e.CourseId : null);
```

**`PurchaseCourseBookCommandHandler`** â€” extend the binary DCB call to a ternary call (no new round-trip):

```csharp
var (priceState, courseId, appendCondition) = await eventStore.BuildDecisionModelAsync(
    CourseBookPriceProjections.PriceWithGracePeriod(command.BookId),
    CourseBookPriceProjections.CourseIdForBook(command.BookId));
```

The `CourseBookPurchasedEvent` is tagged with `courseId` when `courseId` is not null.

**`OrderCourseBooksCommandHandler`** â€” extend the N-ary call to also include one `CourseIdForBook` projection per cart item. Collect the unique `courseId` values from all items; add one `courseId` tag per unique value on the `CourseBooksOrderedEvent`.

#### Projection design

```
Type:     Persisted  (IProjectionDefinition<CourseBuyersState>)
Key:      courseId tag  â† present on all four event types after the above changes
Events:   CourseCreatedEvent
          CourseBookDefinedEvent
          CourseBookPurchasedEvent
          CourseBooksOrderedEvent
```

State shape:

```csharp
public sealed record CourseBuyerEntry(
    Guid           StudentId,
    Guid           BookId,
    decimal        PricePaid,
    DateTimeOffset PurchasedAt);

public sealed record CourseBuyersState(
    Guid                        CourseId,
    string                      CourseName,
    Guid                        BookId,
    IReadOnlyList<CourseBuyerEntry> Buyers);
```

Fold logic:
- `CourseCreatedEvent` â†’ initialize state with course name and empty buyers list
- `CourseBookDefinedEvent` â†’ set `BookId` on state (links the course to its textbook)
- `CourseBookPurchasedEvent` â†’ append one `CourseBuyerEntry`
- `CourseBooksOrderedEvent` â†’ append one `CourseBuyerEntry` for each item whose `BookId` matches the current state's `BookId`

#### Known limitation: multi-course cart orders

A `CourseBooksOrderedEvent` that contains books from different courses (e.g. a student buys textbooks for Course A and Course B in one cart) carries multiple `courseId` tags. Opossum's `KeySelector` returns a **single key** â€” it uses the first `courseId` tag found on the event. Only that course's `CourseBuyersState` entry is updated; the other courses' entries are not updated from this order event.

This means: a student who bought Course B's book via a mixed-course cart order will **not** appear in Course B's buyer list. They will appear correctly if they made a separate single-book purchase (`CourseBookPurchasedEvent`) for Course B's book.

The seeder generates multi-book orders where all books belong to the **same course** (a student buying the textbook plus supplemental materials for one course). This avoids the limitation in seeded data. The limitation only manifests for cross-course orders made through the real application's Swagger UI, and is documented in the endpoint description.

#### Seeder implications

1. `CourseBookDefinedEvent` gets the required `CourseId` property and `courseId` tag (seeder reads `courseId` from `SeedContext` â€” no store lookup)
2. `CourseBookPurchasedEvent` gets `courseId` tag (seeder reads from `SeedContext.BookToCourse` â€” no store lookup)
3. `CourseBooksOrderedEvent` â€” seeder constrains all books in one order to the same course, producing exactly one `courseId` tag per order event, fully avoiding the limitation described above

---

## 14. Implementation Sessions

The full scope of this document is implemented across **nine focused sessions**. Each session is self-contained, has explicit dependencies, and ends with a verifiable acceptance criterion. Sessions within the same group (Sample App / DataSeeder) can be parallelised; cross-group ordering must be respected.

---

### Session 1 — Sample App: `CourseBookDefinedEvent.CourseId`

**Scope — `Opossum.Samples.CourseManagement`**

| # | Task |
|---|---|
| 1 | Add required `CourseId` property to `CourseBookDefinedEvent` |
| 2 | Add `CourseId` parameter to `DefineCourseBookRequest`, `DefineCourseBookCommand`, and endpoint |
| 3 | Update `DefineCourseBookCommandHandler` to always emit the `courseId` tag |
| 4 | Update `CourseBookPriceProjections.BookExists` and `PriceWithGracePeriod` — no logic change, but re-read for correctness |
| 5 | Update all existing unit and integration tests that create `CourseBookDefinedEvent` or call `DefineCourseBook` |
| 6 | Update XML doc comments and endpoint `.WithDescription` |
| 7 | Update `CHANGELOG.md` |

**Acceptance:** `dotnet build` 0 warnings; all existing tests pass.  
**Dependencies:** None.

---

### Session 2 — Sample App: `courseId` tag on purchase events

**Scope — `Opossum.Samples.CourseManagement`**

| # | Task |
|---|---|
| 1 | Add `CourseBookPriceProjections.CourseIdForBook(bookId)` decision projection |
| 2 | Extend `PurchaseCourseBookCommandHandler.TryPurchaseAsync` — add `CourseIdForBook` to the existing `BuildDecisionModelAsync` call; tag `CourseBookPurchasedEvent` with `courseId` |
| 3 | Extend `OrderCourseBooksCommandHandler.TryOrderAsync` — for each item add a `CourseIdForBook` projection to the N-ary call; tag `CourseBooksOrderedEvent` with all unique `courseId` values found |
| 4 | Add unit tests for `CourseIdForBook` projection |
| 5 | Add integration tests confirming that `CourseBookPurchasedEvent` and `CourseBooksOrderedEvent` carry the expected `courseId` tag after the commands execute |
| 6 | Update `CHANGELOG.md` |

**Acceptance:** `dotnet build` 0 warnings; all existing + new tests pass.  
**Dependencies:** Session 1.

---

### Session 3 — Sample App: `StudentPurchasedBooksProjection`

**Scope — `Opossum.Samples.CourseManagement`**

| # | Task |
|---|---|
| 1 | Create `StudentPurchasedBooks/StudentPurchasedBooksProjection.cs` — persisted `IProjectionDefinition<StudentPurchasedBooksState>` keyed by `studentId` tag; folds `CourseBookPurchasedEvent` and `CourseBooksOrderedEvent` |
| 2 | Create `StudentPurchasedBooks/GetStudentPurchasedBooks.cs` — endpoint `GET /students/{studentId}/purchased-books` + query handler |
| 3 | Register projection and handler in `Program.cs` |
| 4 | Add integration tests: define book, purchase it, query endpoint — verify returned entries |
| 5 | Update `CHANGELOG.md` |

**Acceptance:** `GET /students/{id}/purchased-books` returns correct deduplicated book list; tests pass.  
**Dependencies:** None (uses events and tags that already exist before Session 2; Session 2 adds `courseId` tag which is not required here).

---

### Session 4 — Sample App: `CourseBuyersProjection`

**Scope — `Opossum.Samples.CourseManagement`**

| # | Task |
|---|---|
| 1 | Create `CourseBuyers/CourseBuyersProjection.cs` — persisted `IProjectionDefinition<CourseBuyersState>` keyed by `courseId` tag; folds `CourseCreatedEvent`, `CourseBookDefinedEvent`, `CourseBookPurchasedEvent`, `CourseBooksOrderedEvent` |
| 2 | Create `CourseBuyers/GetCourseBuyers.cs` — endpoint `GET /courses/{courseId}/book-buyers` + query handler |
| 3 | Register projection and handler in `Program.cs` |
| 4 | Add integration tests covering: course created, book defined for course, student purchases book, endpoint returns student; also cover the multi-course-order limitation |
| 5 | Document the multi-course order limitation in the endpoint `.WithDescription` |
| 6 | Update `CHANGELOG.md` |

**Acceptance:** `GET /courses/{id}/book-buyers` returns correct buyer list with course name; tests pass.  
**Dependencies:** Sessions 1 and 2 (needs `courseId` tag on purchase events and `CourseId` on book definition).

---

### Session 5 — DataSeeder: Core Infrastructure

**Scope — `Opossum.Samples.DataSeeder`**

| # | Task |
|---|---|
| 1 | Define `SeedEvent` record: `{ DomainEvent Event, Metadata Metadata }` |
| 2 | Define `SeedContext` class: shared mutable state accumulator populated by generators |
| 3 | Define `IEventWriter` interface: `Task WriteAsync(IReadOnlyList<SeedEvent> events, string contextPath)` |
| 4 | Implement `DirectEventWriter`: builds in-memory `Dictionary<string, SortedSet<long>>` index map; writes all event JSON files in parallel via `Parallel.ForEachAsync`; writes index files + `.ledger` sequentially after; uses temp-file + atomic-rename strategy matching `EventFileManager` |
| 5 | Implement `EventStoreWriter`: thin wrapper calling `IEventStore.AppendAsync(events[], null)` in chunks |
| 6 | Implement `SeedPlan`: collects `SeedEvent` lists from all generators (via `ISeedGenerator` interface), stable-sorts by `Metadata.Timestamp`, assigns sequential `Position` values (1-based), hands `IReadOnlyList<SequencedSeedEvent>` to `IEventWriter` |
| 7 | Unit tests: write a small batch via `DirectEventWriter` to a temp folder; verify files exist and can be deserialized by `JsonEventSerializer`; verify index files and `.ledger` are correct |

**Acceptance:** `DirectEventWriter` produces a valid on-disk database; unit tests confirm file format matches Opossum's layout.  
**Dependencies:** None.

---

### Session 6 — DataSeeder: Existing Feature Generators

**Scope — `Opossum.Samples.DataSeeder`**

Port the logic that already exists in `DataSeeder.cs` into the new generator pattern. Delete nothing from `DataSeeder.cs` yet — keep the old file in place; generators are additive.

| # | Generator | Events produced | Key invariants enforced |
|---|---|---|---|
| 1 | `StudentGenerator` | `StudentRegisteredEvent` | Unique emails; tier distribution matches config |
| 2 | `TierUpgradeGenerator` | `StudentSubscriptionUpdatedEvent` | Only non-Master students; updates `SeedContext.Students` |
| 3 | `CourseGenerator` | `CourseCreatedEvent` | Unique IDs; capacity within size-category bounds |
| 4 | `CapacityChangeGenerator` | `CourseStudentLimitModifiedEvent` | New capacity ? 10; updates `SeedContext.Courses` |
| 5 | `EnrollmentGenerator` | `StudentEnrolledToCourseEvent` | No duplicate pair; course capacity; student tier limit |
| 6 | `InvoiceGenerator` | `InvoiceCreatedEvent` | Sequential numbers from counter; no store read |

Unit test each generator with a fixed-seed `Random(42)` and assert event count, tag presence, and key invariant properties.

**Acceptance:** All six generators produce correct events; unit tests pass.  
**Dependencies:** Session 5 (`SeedContext`, `SeedEvent`, `ISeedGenerator`).

---

### Session 7 — DataSeeder: New Feature Generators

**Scope — `Opossum.Samples.DataSeeder`**

| # | Generator | Events produced | Key invariants enforced |
|---|---|---|---|
| 1 | `AnnouncementGenerator` | `CourseAnnouncementPostedEvent`, `CourseAnnouncementRetractedEvent` | Unique `AnnouncementId` + `IdempotencyToken` per announcement; retracted events reference same token as posted event |
| 2 | `ExamTokenGenerator` | `ExamRegistrationTokenIssuedEvent`, `ExamRegistrationTokenRedeemedEvent`, `ExamRegistrationTokenRevokedEvent` | Unique `TokenId`; redeemed + revoked are mutually exclusive per token; redeemed student must be enrolled in the token's course; issued timestamp precedes redeemed/revoked timestamp |
| 3 | `CourseBookGenerator` | `CourseBookDefinedEvent`, `CourseBookPriceChangedEvent`, `CourseBookPurchasedEvent`, `CourseBooksOrderedEvent` | Book assigned to exactly one course (1:1); price changes reference existing books; `PricePaid` matches in-memory price at purchase time; multi-book orders constrained to same-course books; `courseId` tag on all four event types |

Unit test each generator with `Random(42)`; for `ExamTokenGenerator` verify the issued/redeemed/revoked ordering; for `CourseBookGenerator` verify price consistency.

**Acceptance:** All three generators produce correct events; unit tests pass.  
**Dependencies:** Sessions 1 (for `CourseId` on `CourseBookDefinedEvent`) and 6 (for `SeedContext` with student/course/enrollment data).

---

### Session 8 — DataSeeder: Console UX and Presets

**Scope — `Opossum.Samples.DataSeeder`**

| # | Task |
|---|---|
| 1 | Replace `SeedingConfiguration.cs` with the new shape from §7.2 |
| 2 | Add `SeedingPresets.cs` with four static factory methods returning configured `SeedingConfiguration` instances |
| 3 | Rewrite `Program.cs`: interactive four-option console menu › `SeedingPresets` › confirmation summary › `SeedPlan.RunAsync(config, writer)` |
| 4 | Add CLI flag parsing (`--size`, `--reset`, `--no-confirm`, `--use-event-store`, `--parallelism`) that bypasses the menu |
| 5 | Integration test: run `--size small --reset --no-confirm` against a temp folder; assert database contains expected event count; load the database via `FileSystemEventStore` and execute a sample query |

**Acceptance:** All four presets can be selected interactively and via CLI; integration test passes.  
**Dependencies:** Sessions 5, 6, 7.

---

### Session 9 — Final Polish

**Scope — Both projects + documentation**

| # | Task |
|---|---|
| 1 | Delete `Samples/Opossum.Samples.DataSeeder/DataSeeder.cs` (legacy monolithic class) |
| 2 | Rewrite `Samples/Opossum.Samples.DataSeeder/README.md` to document the new architecture, presets, and CLI flags |
| 3 | Add a comprehensive `CHANGELOG.md` entry covering all changes from Sessions 1–8 |
| 4 | Run `dotnet build` › confirm `0 Warning(s)` |
| 5 | Run `dotnet test` on all four test projects › confirm all pass |
| 6 | Seed a **Medium** preset database, start `Opossum.Samples.CourseManagement`, exercise every Swagger endpoint at least once |
| 7 | Update the plan document status from `Draft` to `Implemented` |

**Acceptance:** 0 build warnings; all tests pass; sample app exercises cleanly against a Medium database.  
**Dependencies:** Sessions 1–8.
