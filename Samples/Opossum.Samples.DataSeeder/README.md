# Opossum Data Seeder

## Overview

The **Opossum.Samples.DataSeeder** is a high-performance console application that generates realistic baseline data for the Opossum event sourcing sample at any scale — from a quick ~620-event exploration dataset up to a ~5 million-event production-scale load test.

It covers all nine event-producing DCB-pattern examples implemented in `Opossum.Samples.CourseManagement`, making every Swagger endpoint immediately exercisable after seeding.

---

## Quick Start

```bash
cd Samples/Opossum.Samples.DataSeeder
dotnet run
```

The interactive menu guides you through preset selection, a reset prompt, and a confirmation summary before writing anything to disk.

---

## Architecture

The seeder is organised in three independent layers:

```
┌──────────────────────────────────────────────────────────────┐
│                    LAYER 1: GENERATORS                       │
│   Pure C#, no I/O.  All business invariants enforced here.   │
│                                                              │
│  StudentGenerator     CourseGenerator     BookGenerator      │
│  AnnouncementGen      ExamTokenGenerator  InvoiceGenerator   │
└───────────────────────────────┬──────────────────────────────┘
                                │ IReadOnlyList<SeedEvent>
                                ▼
┌──────────────────────────────────────────────────────────────┐
│                   LAYER 2: ORCHESTRATOR                      │
│   Coordinates generators, assigns positions, sorts by        │
│   timestamp, manages cross-feature shared state.             │
│                                                              │
│                     SeedPlan                                 │
└───────────────────────────────┬──────────────────────────────┘
                                │ IReadOnlyList<SequencedSeedEvent>
                                ▼
┌──────────────────────────────────────────────────────────────┐
│                    LAYER 3: WRITER                           │
│   I/O only — no domain logic.  Two implementations:         │
│                                                              │
│  DirectEventWriter  ←── default (high-performance)          │
│  EventStoreWriter   ←── fallback (--use-event-store)        │
└──────────────────────────────────────────────────────────────┘
```

### Layer 1 — Generators

Nine stateless generator classes produce `SeedEvent` lists in dependency order. Each generator reads shared state from a `SeedContext` populated by earlier generators and enforces all domain invariants in pure code — no I/O, no event-store reads.

| Generator | Events produced | Key invariants enforced |
|---|---|---|
| `StudentGenerator` | `StudentRegisteredEvent` | Unique emails; tier distribution |
| `TierUpgradeGenerator` | `StudentSubscriptionUpdatedEvent` | Non-Master students only |
| `CourseGenerator` | `CourseCreatedEvent` | Unique IDs; capacity bounds |
| `CapacityChangeGenerator` | `CourseStudentLimitModifiedEvent` | Minimum capacity of 10 |
| `EnrollmentGenerator` | `StudentEnrolledToCourseEvent` | No duplicates; capacity; tier limit |
| `InvoiceGenerator` | `InvoiceCreatedEvent` | Sequential numbers from counter |
| `AnnouncementGenerator` | `CourseAnnouncementPostedEvent`, `CourseAnnouncementRetractedEvent` | Unique `AnnouncementId` + `IdempotencyToken` per announcement |
| `ExamTokenGenerator` | `ExamRegistrationTokenIssuedEvent`, `ExamRegistrationTokenRedeemedEvent`, `ExamRegistrationTokenRevokedEvent` | Redeemed/revoked mutually exclusive; timestamps ordered correctly |
| `CourseBookGenerator` | `CourseBookDefinedEvent`, `CourseBookPriceChangedEvent`, `CourseBookPurchasedEvent`, `CourseBooksOrderedEvent` | One book per course; `PricePaid` matches in-memory price at purchase time |

### Layer 2 — SeedPlan

`SeedPlan` collects all generator output into one flat list, stable-sorts by `Metadata.Timestamp`, assigns sequential 1-based positions, then hands the result to the writer.

### Layer 3 — Writers

**`DirectEventWriter`** (default) achieves maximum write throughput by:
- Building all index structures in memory across the full batch
- Writing all event JSON files in parallel (default: `Environment.ProcessorCount` threads)
- Flushing each index file and the `.ledger` exactly **once** at the end — O(1) per-event I/O regardless of batch size
- Appending to existing databases by reading the current ledger offset first

**`EventStoreWriter`** (fallback) delegates to `IEventStore.AppendAsync`. Suitable for small datasets or when you want DCB enforcement during seeding.

---

## Presets

| Preset | Students | Courses | Books | Invoices | Est. total events |
|---|---|---|---|---|---|
| **Small** | 40 | 8 | 8 | 30 | ~620 |
| **Medium** | 7,000 | 1,400 | 1,400 | 2,500 | ~104,000 |
| **Large** | 70,000 | 14,000 | 14,000 | 15,000 | ~1,030,000 |
| **Prod** | 350,000 | 70,000 | 70,000 | 75,000 | ~5,150,000 |

All presets use the same per-entity multipliers:
- 3 announcements per course, ~20% retracted
- 2 exams per course, 5 tokens per exam (~70% redeemed, ~10% revoked)
- ~40% of books get a price change; ~20 single-book purchases per book
- ~30% of students get a tier upgrade; ~20% of courses get a capacity change

---

## Interactive Console Menu

When started without `--size`, the seeder presents a step-by-step menu:

```
🌱 Opossum Data Seeder
======================

Database: D:\Database\OpossumSampleApp

Select a dataset size:
  [1] Small   ~620 events       — explore the data model
  [2] Medium  ~104 000 events   — growing business, a few months of data
  [3] Large   ~1 030 000 events — established platform, 1-3 years of data
  [4] Prod    ~5 150 000 events — large-scale performance testing

Your choice (1-4): _
```

After selection a reset prompt and a confirmation summary are shown before any writes occur.

---

## CLI Flags

For scripted runs (CI, benchmarks) all prompts can be bypassed:

```
Usage: dotnet run -- [flags]

  --size <small|medium|large|prod>   Select a preset non-interactively
  --reset                            Delete existing data before seeding
  --no-confirm                       Skip all confirmation prompts
  --use-event-store                  Use IEventStore instead of DirectEventWriter
  --parallelism <n>                  File write threads (default: cpu count)
  --help, -h                         Display this help message
```

**Examples:**

```bash
# CI seed for integration tests — fast, deterministic, no prompts
dotnet run -- --size small --reset --no-confirm

# Large dataset for manual performance testing
dotnet run -- --size large --reset --no-confirm

# Append a medium dataset without erasing existing data
dotnet run -- --size medium --no-confirm

# Use the EventStore writer (applies DCB enforcement, slower for large datasets)
dotnet run -- --size small --reset --no-confirm --use-event-store
```

---

## Event Catalogue

The seeder produces 15 distinct event types covering all DCB-pattern examples:

| Event | Tags | Timestamp window |
|---|---|---|
| `StudentRegisteredEvent` | `studentEmail`, `studentId` | 365–180 days ago |
| `CourseCreatedEvent` | `courseId` | 365–200 days ago |
| `CourseBookDefinedEvent` | `bookId`, `courseId` | 300–250 days ago |
| `CourseBookPriceChangedEvent` | `bookId` | 200–100 days ago |
| `StudentSubscriptionUpdatedEvent` | `studentId` | 180–30 days ago |
| `CourseStudentLimitModifiedEvent` | `courseId` | 150–60 days ago |
| `ExamRegistrationTokenIssuedEvent` | `examToken`, `examId`, `courseId` | 60–14 days ago |
| `ExamRegistrationTokenRedeemedEvent` | `examToken`, `examId`, `studentId` | Issued + 1–5 days |
| `ExamRegistrationTokenRevokedEvent` | `examToken`, `examId` | Issued + 1–3 days |
| `CourseAnnouncementPostedEvent` | `courseId`, `idempotency` | 90–30 days ago |
| `CourseAnnouncementRetractedEvent` | `courseId`, `idempotency` | Posted + 1–7 days |
| `StudentEnrolledToCourseEvent` | `courseId`, `studentId` | 120–1 days ago |
| `CourseBookPurchasedEvent` | `bookId`, `studentId`, `courseId` | 100–7 days ago |
| `CourseBooksOrderedEvent` | `bookId` (per item), `studentId`, `courseId` | 100–7 days ago |
| `InvoiceCreatedEvent` | `invoiceNumber` | 90–1 days ago |

---

## Configuration

`SeedingConfiguration` exposes all knobs that the presets set. You can start from a preset and override individual properties programmatically:

```csharp
var config = SeedingPresets.Medium();
config.AnnouncementsPerCourse = 5;
config.TokensPerExam = 10;
```

Key properties:

| Property | Default | Description |
|---|---|---|
| `StudentCount` | 10,000 | Number of students |
| `CourseCount` | 2,000 | Number of courses |
| `CourseBookCount` | 2,000 | Number of books (one per course) |
| `InvoiceCount` | 1,000 | Number of invoices |
| `MultiBookOrders` | 200 | Number of multi-book cart orders |
| `AnnouncementsPerCourse` | 3 | Announcements posted per course |
| `AnnouncementRetractionPercentage` | 20 | % of announcements retracted |
| `ExamsPerCourse` | 2 | Exams per course |
| `TokensPerExam` | 5 | Registration tokens per exam |
| `TokenRedemptionPercentage` | 70 | % of tokens redeemed |
| `TokenRevocationPercentage` | 10 | % of tokens revoked |
| `TierUpgradePercentage` | 30 | % of students upgraded |
| `CapacityChangePercentage` | 20 | % of courses with capacity change |
| `PriceChangePercentage` | 40 | % of books with a price change |
| `SingleBookPurchasesPerBook` | 20 | Individual purchases per book |
| `UseEventStoreWriter` | false | Use `IEventStore` writer instead |
| `WriteParallelism` | 0 (= cpu count) | Parallel file-write threads |
| `ResetDatabase` | false | Clear database before seeding |
| `RequireConfirmation` | true | Prompt before writing |

---

## Database Location

The seeder reads the database path from the CourseManagement sample app's `appsettings.Development.json`:

```json
{
  "Opossum": {
    "RootPath": "D:\\Database",
    "StoreName": "OpossumSampleApp"
  }
}
```

The context path written to is `{RootPath}/{StoreName}`.

---

## On-Disk Layout

After seeding, the database has this structure:

```
{RootPath}/{StoreName}/
  .ledger
  events/
    0000000001.json
    0000000002.json
    ...
  Indices/
    EventType/
      StudentRegisteredEvent.json
      CourseCreatedEvent.json
      ...
    Tags/
      studentId_<guid>.json
      courseId_<guid>.json
      ...
```

The format is byte-for-bit identical to what `IEventStore.AppendAsync` produces — `DirectEventWriter` uses the same JSON serialisation options, the same temp-file + atomic-rename strategy, and the same index file schema as Opossum's internal `EventFileManager`.