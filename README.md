# 🦝 Opossum

**A file system-based event store for .NET that implements the DCB (Dynamic Consistency Boundaries) specification.**

Opossum turns your file system into a fully functional event store with projections, optimistic concurrency control, and tag-based indexing. Perfect for scenarios where simplicity, offline operation, and local data sovereignty matter more than cloud scalability.

[![NuGet](https://img.shields.io/nuget/v/Opossum.svg)](https://www.nuget.org/packages/Opossum/)
[![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download)
[![License](https://img.shields.io/github/license/majormartintibor/Opossum.svg)](LICENSE)
[![Docs](https://img.shields.io/badge/docs-gh--pages-blue)](https://majormartintibor.github.io/Opossum/)

---

## 📋 Table of Contents

- [What is Opossum?](#-what-is-opossum)
- [When to Use Opossum](#-when-to-use-opossum)
- [When NOT to Use Opossum](#-when-not-to-use-opossum)
- [Quick Start](#-quick-start)
- [Core Concepts](#-core-concepts)
- [Configuration](#-configuration)
- [How Events Are Stored](#-how-events-are-stored)
- [API Reference](#-api-reference)
- [Full Example](#-full-example)
- [Event-Sourced Aggregate — Alternative Write-Side Pattern](#-event-sourced-aggregate--alternative-write-side-pattern)
- [DCB Examples Coverage](#-dcb-examples-coverage)
- [Consecutive Sequences — Invoice Numbers](#-consecutive-sequences--invoice-numbers)
- [Dynamic Product Price — Course Books](#-dynamic-product-price--course-books)
- [Opt-In Token — Server-Generated Single-Use Tokens](#️-opt-in-token--server-generated-single-use-tokens)
- [Performance](#-performance)
- [OpenTelemetry](#-opentelemetry)
- [Known Limitations](#️-known-limitations)

---

## 🦝 What is Opossum?

Opossum is an **event sourcing framework** that uses your **file system as the database**. It's designed for applications that need:

- ✅ **100% offline operation** - No internet required
- ✅ **Complete audit trail** - Every state change is an immutable event
- ✅ **Local data ownership** - Your data never leaves your server
- ✅ **Optimistic concurrency** - Built-in DCB pattern for consistency
- ✅ **Simple deployment** - Just files, no database server to manage
- ✅ **Projections** - Materialized views that rebuild from events
- ✅ **Tag-based indexing** - Fast queries without full scans

### What Makes Opossum Different?

Unlike cloud-based event stores (EventStoreDB, Azure Event Hubs) or database-backed solutions, Opossum **stores events directly as files** in a structured directory hierarchy. This makes it ideal for:

- 🏢 **On-premises applications** (POS systems, dealership software)
- 📴 **Offline-first applications** (field service, remote installations)
- 💼 **SMB solutions** (where cloud costs don't make sense)
- 🔒 **Data sovereignty requirements** (keep data in-country/on-site)
- 🧪 **Development & testing** (no Docker/database setup needed)
- 🖥️ **Multi-workstation deployments** (multiple PCs sharing a store on a network drive — cross-process append safety via OS file locking)

---

## ✅ When to Use Opossum

### Perfect Use Cases

| Scenario | Why Opossum Fits |
|----------|------------------|
| **Automotive dealership sales tracking** | Offline operation, complete audit trail, local compliance |
| **Point-of-sale systems** | Works during internet outages, simple IT management |
| **Field service applications** | Offline data collection, sync when connected |
| **Small business ERP** | No cloud costs, data stays on-premises |
| **Compliance-heavy industries** | Immutable audit log, easy to backup/archive |
| **Development & testing** | Zero infrastructure setup, just run the app |

### Key Characteristics

✅ **Single server/small deployment** (< 100k events/day)  
✅ **Offline-first requirements**  
✅ **Simple IT environment** (IT staff comfortable with files/folders)  
✅ **Budget-conscious** (avoid monthly cloud fees)  
✅ **Data residency requirements** (legal/compliance)  
✅ **Complete audit trail needed**

---

## ❌ When NOT to Use Opossum

Opossum is **not designed** for:

| Don't Use If... | Use Instead |
|----------------|-------------|
| ❌ **Distributed systems** across multiple servers | EventStoreDB, Kafka |
| ❌ **High throughput** (> 100k events/day per server) | Cloud event stores |
| ❌ **Cloud-native microservices** | Azure Event Hubs, AWS Kinesis |
| ❌ **Multi-region replication** needed | Distributed event stores |
| ❌ **Event streaming** to multiple consumers | Kafka, RabbitMQ |
| ❌ **Massive scale** (millions of events) | Purpose-built event stores |

**Rule of thumb:** If your application runs on a single server (or small cluster) and needs offline capabilities, Opossum is great. If you need cloud-scale distribution, choose a cloud-native solution.

---

## 🚀 Quick Start

### 1. Install the NuGet Package

```bash
dotnet add package Opossum
```

### 2. Configure Opossum

```csharp
using Opossum.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add Opossum event store
builder.Services.AddOpossum(options =>
{
    options.RootPath = @"D:\MyAppData\EventStore";  // Where to store events
    options.UseStore("MyApp");                       // Store name
    options.FlushEventsImmediately = true;           // Durability guarantee (recommended)
});

// Add projection system for read models
builder.Services.AddProjections(options =>
{
    options.ScanAssembly(typeof(Program).Assembly);  // Auto-discover projections
});

// Add mediator for command handling (optional but recommended)
builder.Services.AddMediator();

var app = builder.Build();
app.Run();
```

### 3. Define Your Events

Events are immutable records that represent state changes:

```csharp
using Opossum;

public record StudentRegisteredEvent(
    Guid StudentId,
    string FirstName,
    string LastName,
    string Email) : IEvent;

public record StudentEnrolledToCourseEvent(
    Guid StudentId,
    Guid CourseId) : IEvent;
```

### 4. Append Events to the Store

```csharp
using Opossum.Core;
using Opossum.Extensions;

public class RegisterStudentHandler
{
    private readonly IEventStore _eventStore;

    public RegisterStudentHandler(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    public async Task<Guid> RegisterStudentAsync(string firstName, string lastName, string email)
    {
        var studentId = Guid.NewGuid();

        // Create event
        var evt = new StudentRegisteredEvent(studentId, firstName, lastName, email)
            .ToDomainEvent()
            .WithTag("studentId", studentId.ToString())
            .WithTag("studentEmail", email)
            .WithTimestamp(DateTimeOffset.UtcNow);

        // Append to event store
        await _eventStore.AppendAsync(evt);

        return studentId;
    }
}
```

### 5. Query Events

```csharp
using Opossum.Core;

// Query all events for a specific student
var query = Query.FromItems(new QueryItem
{
    Tags = [new Tag("studentId", studentId.ToString())]
});

var events = await _eventStore.ReadAsync(query);

foreach (var evt in events)
{
    Console.WriteLine($"[{evt.Position}] {evt.Event.EventType}");
}
```

### 6. Create Projections (Read Models)

Projections transform events into queryable views:

```csharp
using Opossum.Projections;

public record StudentDetails(
    Guid StudentId,
    string FirstName,
    string LastName,
    string Email,
    int EnrolledCoursesCount);

[ProjectionDefinition("StudentDetails")]
public class StudentDetailsProjection : IProjectionDefinition<StudentDetails>
{
    public string ProjectionName => "StudentDetails";

    public string[] EventTypes =>
    [
        nameof(StudentRegisteredEvent),
        nameof(StudentEnrolledToCourseEvent)
    ];

    public string KeySelector(SequencedEvent evt)
    {
        // Extract student ID from tags
        return evt.Event.Tags.First(t => t.Key == "studentId").Value;
    }

    public StudentDetails? Apply(StudentDetails? current, SequencedEvent evt)
    {
        return evt.Event.Event switch
        {
            StudentRegisteredEvent registered => new StudentDetails(
                registered.StudentId,
                registered.FirstName,
                registered.LastName,
                registered.Email,
                EnrolledCoursesCount: 0),

            StudentEnrolledToCourseEvent enrolled when current is not null =>
                current with { EnrolledCoursesCount = current.EnrolledCoursesCount + 1 },

            _ => current
        };
    }
}
```

### 7. Query Projections

```csharp
using Opossum.Projections;

public class StudentController(IProjectionStore<StudentDetails> projectionStore)
{
    public async Task<StudentDetails?> GetStudentAsync(Guid studentId)
    {
        return await projectionStore.GetAsync(studentId.ToString());
    }
}
```

---

## 🧠 Core Concepts

### Events

**Immutable records** that represent state changes in your domain. Every event implements `IEvent` and gets stored permanently.

```csharp
public record CourseCreatedEvent(Guid CourseId, string Name, int MaxStudents) : IEvent;
```

### NewEvent (Write Side)

What you pass to `AppendAsync`. Contains the event payload and optional metadata, but **no position** — the store assigns that during append:

```csharp
public class NewEvent
{
    public DomainEvent Event { get; set; }  // Your domain event + EventType + Tags
    public Metadata Metadata { get; set; }  // Optional: Timestamp, correlation IDs
}
```

You rarely construct this directly — use the fluent builder instead (see [Extension Methods](#extension-methods)).

### SequencedEvent (Read Side)

What `ReadAsync` returns. Wraps the original event with a **position** assigned by the store:

```csharp
public class SequencedEvent
{
    public long Position { get; set; }      // Global sequence number (assigned by store)
    public DomainEvent Event { get; set; }  // Wrapper containing your domain event + tags
    public Metadata Metadata { get; set; }  // Timestamp, correlation/causation IDs
}
```

This is the DCB-spec distinction: **`Event`** (write input, no position) vs **`SequencedEvent`** (read output, position assigned by store).

### Tags

Domain-specific metadata for **fast filtering** without full scans:

```csharp
.WithTag("studentId", studentId.ToString())
.WithTag("courseId", courseId.ToString())
.WithTag("studentEmail", "student@example.com")
```

Tags are indexed automatically, enabling efficient queries like:
- "All events for student X"
- "All course enrollments in January 2024"

### Queries

Filter events by **EventType** and/or **Tags**:

```csharp
// All events for a specific student
var query = Query.FromItems(new QueryItem
{
    Tags = [new Tag("studentId", "123")]
});

// All StudentRegistered events
var query = Query.FromItems(new QueryItem
{
    EventTypes = [nameof(StudentRegisteredEvent)]
});

// Combination: StudentEnrolled events for student 123
var query = Query.FromItems(new QueryItem
{
    EventTypes = [nameof(StudentEnrolledToCourseEvent)],
    Tags = [new Tag("studentId", "123")]
});
```

### Projections

**Materialized views** rebuilt from events. Think of them as denormalized read models:

```csharp
[ProjectionDefinition("CourseEnrollmentCount")]
public class CourseEnrollmentProjection : IProjectionDefinition<CourseEnrollmentState>
{
    public string ProjectionName => "CourseEnrollmentCount";

    public string[] EventTypes => [nameof(StudentEnrolledToCourseEvent)];

    public string KeySelector(SequencedEvent evt) =>
        evt.Event.Tags.First(t => t.Key == "courseId").Value;

    public CourseEnrollmentState? Apply(CourseEnrollmentState? current, SequencedEvent evt)
    {
        return evt.Event.Event switch
        {
            StudentEnrolledToCourseEvent when current is not null =>
                current with { EnrollmentCount = current.EnrollmentCount + 1 },
            _ => current
        };
    }
}
```

#### Related-Event Enrichment (`IProjectionWithRelatedEvents<T>`)

When building a projection's state requires data from events matched by an additional query — events with different types or tags — implement `IProjectionWithRelatedEvents<TState>` instead of `IProjectionDefinition<TState>`. The framework calls `GetRelatedEventsQuery` before `Apply`, executes that second query, and passes the results in as a third parameter — no N+1 queries, no manual secondary reads.

Example: `CourseDetailsProjection` needs student names when a `StudentEnrolledToCourseEvent` arrives, but that data lives in `StudentRegisteredEvent` under a different tag:

```csharp
[ProjectionDefinition("CourseDetails")]
public sealed class CourseDetailsProjection : IProjectionWithRelatedEvents<CourseDetails>
{
    public string ProjectionName => "CourseDetails";
    public string[] EventTypes => [nameof(CourseCreatedEvent), nameof(StudentEnrolledToCourseEvent)];
    public string KeySelector(SequencedEvent evt) =>
        evt.Event.Tags.First(t => t.Key == "courseId").Value;

    // Return a query for the extra events needed, or null if this event needs nothing extra.
    public Query? GetRelatedEventsQuery(SequencedEvent evt)
    {
        if (evt.Event.Event is StudentEnrolledToCourseEvent enrolled)
            return Query.FromItems(new QueryItem
            {
                Tags = [new Tag("studentId", enrolled.StudentId.ToString())],
                EventTypes = [nameof(StudentRegisteredEvent)]
            });
        return null;
    }

    // relatedEvents contains what GetRelatedEventsQuery returned (empty array when null was returned).
    public CourseDetails? Apply(CourseDetails? current, SequencedEvent evt, SequencedEvent[] relatedEvents) =>
        evt.Event.Event switch
        {
            CourseCreatedEvent created => new CourseDetails(
                CourseId: created.CourseId,
                Name: created.Name,
                MaxStudentCount: created.MaxStudentCount,
                CurrentEnrollmentCount: 0,
                EnrolledStudents: []),

            StudentEnrolledToCourseEvent when current is not null
                && relatedEvents.FirstOrDefault(e => e.Event.Event is StudentRegisteredEvent) is
                    { Event.Event: StudentRegisteredEvent reg } =>
                current with
                {
                    CurrentEnrollmentCount = current.CurrentEnrollmentCount + 1,
                    EnrolledStudents = [..current.EnrolledStudents,
                        new EnrolledStudentInfo(reg.StudentId, reg.FirstName, reg.LastName, reg.Email)]
                },

            _ => current
        };
}
```

#### Tag-Indexed Queries (`IProjectionTagProvider<T>`)

By default, projections are keyed by a single string (`KeySelector`) so `GetAsync` retrieves one instance by key. To also query projections by their current state properties — e.g., "all courses that are not yet full" — attach a tag provider. The index is updated automatically every time a projection is saved.

```csharp
// 1. Implement the tag provider — return whatever tags should be queryable
public sealed class CourseShortInfoTagProvider : IProjectionTagProvider<CourseShortInfo>
{
    public IEnumerable<Tag> GetTags(CourseShortInfo state)
    {
        yield return new Tag("IsFull", state.IsFull.ToString());
    }
}

// 2. Attach it to the projection with [ProjectionTags] — auto-discovered during assembly scanning
[ProjectionDefinition("CourseShortInfo")]
[ProjectionTags(typeof(CourseShortInfoTagProvider))]
public sealed class CourseShortInfoProjection : IProjectionDefinition<CourseShortInfo>
{
    // ... normal IProjectionDefinition<T> implementation
}

// 3. Query by tag — uses the persisted index, no full table scan
IProjectionStore<CourseShortInfo> courseStore = ...;
var availableCourses = await courseStore.QueryByTagsAsync(
    [new Tag("IsFull", "False")]);
```

### Decision Model Projections

**Write-side, ephemeral projections** used in the DCB read → decide → append pattern. Each projection is a typed in-memory fold that yields state _and_ a pre-built `AppendCondition` — no persistence, no background services.

Unlike read-side projections, Decision Model projections are:
- **In-memory only** — run once per command, result is never stored
- **Strongly typed** — each projection owns a single business concern
- **Composable** — multiple projections share a single `ReadAsync` call and produce one `AppendCondition` that spans all their queries

```csharp
// Each projection is a self-contained factory method:
IDecisionProjection<MyState?> MyProjection(Guid id) =>
    new DecisionProjection<MyState?>(
        initialState: null,
        query: Query.FromItems(new QueryItem
        {
            EventTypes = [nameof(MyEvent)],
            Tags = [new Tag("id", id.ToString())]
        }),
        apply: (state, evt) => evt.Event.Event switch
        {
            MyEvent e => new MyState(e.Value),
            _ => state
        });

// Compose up to three projections — one read, one atomic AppendCondition:
var (state1, state2, state3, appendCondition) =
    await eventStore.BuildDecisionModelAsync(
        MyProjection(id1),
        AnotherProjection(id2),
        YetAnotherProjection(id3));

// Wrap the full cycle with automatic retry on concurrency conflicts:
return await eventStore.ExecuteDecisionAsync(async (store, ct) =>
{
    var (s1, s2, s3, condition) = await store.BuildDecisionModelAsync(p1, p2, p3, ct);
    // ... check invariants using s1, s2, s3 ...
    await store.AppendAsync(newEvent, condition);
    return result;
});
// If all retries are exhausted, ExecuteDecisionAsync re-throws AppendConditionFailedException.
```

Four `BuildDecisionModelAsync` overloads are available: single-projection (`BuildDecisionModelAsync<T>` → `DecisionModel<T>`), two-projection (`(T1, T2, AppendCondition)`), three-projection (`(T1, T2, T3, AppendCondition)`), and **N-ary** (`IReadOnlyList<IDecisionProjection<TState>>` → `(IReadOnlyList<TState>, AppendCondition)`) for a runtime-variable list of homogeneous projections (e.g. shopping cart). Use `ExecuteDecisionAsync` to wrap the entire cycle with automatic exponential-backoff retry.

See the [Full Example](#-full-example) section for a complete real-world walkthrough

### Idempotency Tokens — Prevent Record Duplication

Enforce **"process this request exactly once"** using a client-generated idempotency token stored as a tag. This is the DCB pattern for infrastructure constraints — constraints that are **not** directly related to the domain.

> Full specification: <https://dcb.events/examples/prevent-record-duplication/>

The key insight: the domain may allow an operation to happen multiple times (a course can have many announcements), but an accidental HTTP retry should not create duplicates. The idempotency token is the **sole guard** — and unlike a domain uniqueness constraint, it is controlled entirely by the client.

```csharp
// The projection folds the token's lifecycle: Posted → true, Retracted → false (token freed).
// The query is scoped exclusively to the idempotency tag, so two concurrent requests with
// DIFFERENT tokens produce completely independent AppendConditions — they never block each other.
IDecisionProjection<bool> IdempotencyTokenWasUsed(Guid token) =>
    new DecisionProjection<bool>(
        initialState: false,
        query: Query.FromItems(new QueryItem
        {
            EventTypes = [nameof(AnnouncementPostedEvent), nameof(AnnouncementRetractedEvent)],
            Tags = [new Tag("idempotency", token.ToString())]
        }),
        apply: (_, evt) => evt.Event.Event switch
        {
            AnnouncementPostedEvent    => true,   // token consumed
            AnnouncementRetractedEvent => false,  // token freed — may be reused
            _                          => false
        });

// In the command handler — compose with a business prerequisite:
var (courseExists, tokenWasUsed, appendCondition) = await eventStore.BuildDecisionModelAsync(
    CourseExists(command.CourseId),
    IdempotencyTokenWasUsed(command.IdempotencyToken));

if (!courseExists)   return Fail("Course does not exist.");
if (tokenWasUsed)    return Fail("Re-submission detected.");

await eventStore.AppendAsync(
    new AnnouncementPostedEvent(Guid.NewGuid(), command.CourseId, command.Title, command.IdempotencyToken)
        .ToDomainEvent()
        .WithTag("courseId", command.CourseId.ToString())
        .WithTag("idempotency", command.IdempotencyToken.ToString()),
    appendCondition);
```

**Token reuse after retraction:** When an announcement is retracted, the `AnnouncementRetractedEvent` is stored with the **same** `idempotency` tag. On the next post attempt the projection folds both events in sequence order — `Posted → true`, then `Retracted → false` — and the final state is `false`. The token is free with no changes to the post handler.

See `CourseAnnouncementProjections` and `CourseAnnouncementRetractionProjection` in the sample application for the full implementation.

> **Contrast with Opt-In tokens:** Idempotency tokens are *client-generated* and protect against accidental retry duplicates. For *server-generated* single-use tokens that can be issued, redeemed, and revoked, see the [Opt-In Token pattern](#️-opt-in-token--server-generated-single-use-tokens).

### Dynamic Consistency Boundaries (DCB)

Enforce **optimistic concurrency** using append conditions. The raw DCB API is ideal for straightforward global-uniqueness rules (e.g. unique email, the [Unique Username example](https://dcb.events/examples/unique-username/)):

```csharp
// Ensure email is unique across ALL students
var validateEmailQuery = Query.FromItems(new QueryItem
{
    Tags = [new Tag("studentEmail", email)]
});

// This will fail if any event with this email already exists
await _eventStore.AppendAsync(
    evt,
    condition: new AppendCondition 
    { 
        FailIfEventsMatch = validateEmailQuery 
    });
```

**Why this matters:** Prevents race conditions without distributed locks. For more complex decisions that need to examine state before deciding, prefer `BuildDecisionModelAsync` (see [Decision Model Projections](#decision-model-projections)).

### Mediator

Opossum includes a lightweight in-process mediator that automatically discovers command and query handlers — no manual registration of individual handlers needed.

**Discovery convention:** any class whose name ends with `Handler` (or is marked `[MessageHandler]`), with a method named `HandleAsync` or `Handle`, where the first parameter is the message type and any additional parameters are injected from the DI container.

```csharp
// 1. Register — auto-scans the calling assembly by default
builder.Services.AddMediator();

// 2. Define the command and its handler — no interface, no DI registration needed
public sealed record RegisterStudentCommand(Guid StudentId, string FirstName, string LastName, string Email);

public sealed class RegisterStudentCommandHandler
{
    // IEventStore is resolved automatically from DI
    public async Task<CommandResult> HandleAsync(
        RegisterStudentCommand command,
        IEventStore eventStore)
    {
        var evt = new StudentRegisteredEvent(command.StudentId, command.FirstName, command.LastName, command.Email)
            .ToDomainEvent()
            .WithTag("studentId", command.StudentId.ToString())
            .WithTimestamp(DateTimeOffset.UtcNow);

        await eventStore.AppendAsync(evt);
        return CommandResult.Ok();
    }
}

// 3. Dispatch — IMediator routes to the matching handler automatically
app.MapPost("/students", async ([FromBody] RegisterStudentRequest req, IMediator mediator) =>
{
    var command = new RegisterStudentCommand(Guid.NewGuid(), req.FirstName, req.LastName, req.Email);
    var result = await mediator.InvokeAsync<CommandResult>(command);
    return result.Success ? Results.Created() : Results.BadRequest(result.ErrorMessage);
});
```

---

## ⚙️ Configuration

### OpossumOptions

```csharp
builder.Services.AddOpossum(options =>
{
    // Root directory for event storage (REQUIRED)
    // Must be an absolute path
    options.RootPath = @"D:\MyApp\EventStore";

    // Store name (REQUIRED) — used as a subdirectory under RootPath
    options.UseStore("MyApplicationContext");

    // Flush events to disk immediately (OPTIONAL, default: true)
    // TRUE: Events are durable (survive power failure) but slower (~17ms per single event on SSD)
    //       Includes flushing event, index, and ledger files — the full durability guarantee.
    // FALSE: Faster but events may be lost on power failure (use for testing/dev only)
    options.FlushEventsImmediately = true;

    // Cross-process lock timeout (OPTIONAL, default: 5 seconds)
    // Relevant when multiple application instances share the same store directory over a network drive.
    // Increase this if appends consistently time out behind large batch operations on a slow share.
    options.CrossProcessLockTimeout = TimeSpan.FromSeconds(5);
});
```

### ProjectionOptions

```csharp
builder.Services.AddProjections(options =>
{
    // Scan assembly for projection definitions
    options.ScanAssembly(typeof(Program).Assembly);

    // Controls startup rebuild behaviour (default: MissingCheckpointsOnly)
    // None                   — no automatic rebuilds on startup
    // MissingCheckpointsOnly — only rebuild projections with no checkpoint file (default, recommended)
    // ForceFullRebuild       — rebuild all projections on every startup (dev / post-migration)
    options.AutoRebuild = AutoRebuildMode.MissingCheckpointsOnly;

    // Maximum projections rebuilt in parallel (default: 4). Increase on NVMe SSDs.
    options.MaxConcurrentRebuilds = 4;

    // Events loaded per batch during rebuild (default: 5 000). Lower = less peak memory.
    options.RebuildBatchSize = 5_000;

    // Events processed between crash-recovery journal flushes (default: 10 000).
    // Lower = more crash-durable; higher = less journal write overhead.
    options.RebuildFlushInterval = 10_000;
});
```

### Configuration via appsettings.json

```json
{
  "Opossum": {
    "RootPath": "D:\\MyApp\\EventStore",
    "StoreName": "MyApp",
    "FlushEventsImmediately": true
  },
  "Projections": {
    "AutoRebuild": "MissingCheckpointsOnly",
    "MaxConcurrentRebuilds": 4,
    "RebuildBatchSize": 5000,
    "RebuildFlushInterval": 10000
  }
}
```

Then bind in code:

```csharp
builder.Services.AddOpossum(options =>
{
    builder.Configuration.GetSection("Opossum").Bind(options);

    // StoreName must be set programmatically — UseStore enforces the single-store contract
    var storeName = builder.Configuration["Opossum:StoreName"];
    if (storeName != null)
    {
        options.UseStore(storeName);
    }
});
```

---

## 💾 How Events Are Stored

Opossum creates a **file-based database** with the following structure:

```
D:\MyApp\EventStore\                 # RootPath
└── MyApplicationContext\             # Store name
    ├── .ledger                       # Ledger file (current sequence position)
    ├── Events\                       # Event files (one per event)
    │   ├── 0000000001.json           # Event at position 1
    │   ├── 0000000002.json           # Event at position 2
    │   └── ...
    ├── Indices\                      # Index directories
    │   ├── EventType\                # Index by event type
    │   │   ├── StudentRegisteredEvent.idx
    │   │   └── StudentEnrolledToCourseEvent.idx
    │   └── Tags\                     # Index by tags
    │       ├── studentId_123.idx     # All events with tag studentId=123
    │       └── studentEmail_test@example.com.idx
    └── Projections\                  # Projection data (read models)
        └── StudentDetails\
            └── <student-guid>.json
```

### File Formats

#### Event File (`Events/0000000001.json`)

```json
{
  "Event": {
    "EventType": "StudentRegisteredEvent",
    "Data": "{\"StudentId\":\"abc-123\",\"FirstName\":\"John\",\"LastName\":\"Doe\",\"Email\":\"john@example.com\"}",
    "Tags": [
      { "Key": "studentId", "Value": "abc-123" },
      { "Key": "studentEmail", "Value": "john@example.com" }
    ]
  },
  "Position": 1,
  "Metadata": {
    "Timestamp": "2024-01-15T10:30:00Z"
  }
}
```

#### Ledger File (`.ledger`)

Simple text file containing the current sequence position:

```
42
```

#### Index File (`Indices/Tags/studentId_abc-123.idx`)

Newline-separated list of sequence positions:

```
1
5
12
27
```

### Why This Works

- **Events are immutable** - Once written, never modified
- **Append-only writes** - New events just get the next sequence number
- **Simple backup** - Just copy the directory
- **Easy debugging** - Open event files in any text editor
- **No corruption risk** - Each event is a separate file

---

## 📖 API Reference

### IEventStore

Core event store operations:

```csharp
public interface IEventStore
{
    // Append one or more events (position is assigned by the store)
    Task AppendAsync(NewEvent[] events, AppendCondition? condition, CancellationToken ct = default);

    // Read events matching a query (returns sequenced events with positions)
    // fromPosition: only events with Position > fromPosition are returned
    // maxCount: cap the number of events returned (for paged reads)
    Task<SequencedEvent[]> ReadAsync(Query query, ReadOption[]? readOptions, long? fromPosition = null, int? maxCount = null);

    // Read the single highest-position event matching a query — O(1) file reads
    // Returns null when no matching events exist
    // Ideal for consecutive-sequence patterns (e.g. invoice numbering)
    Task<SequencedEvent?> ReadLastAsync(Query query, CancellationToken ct = default);
}

// Convenience extension for single-event appends:
await eventStore.AppendAsync(singleEvent);
await eventStore.AppendAsync(singleEvent, condition);
```

### Extension Methods

```csharp
// Convert a domain event (IEvent) to a fluent DomainEventBuilder, then to NewEvent:
NewEvent evt = new MyEvent(...)
    .ToDomainEvent()                          // IEvent → DomainEventBuilder
    .WithTag("key", "value")                  // add a single tag
    .WithTags(tag1, tag2)                     // add multiple tags
    .WithTimestamp(DateTimeOffset.UtcNow)     // set timestamp
    .WithCorrelationId(correlationId)         // optional: correlation / causation / operation / user IDs
    .WithCausationId(causationId);
                                              // implicit conversion → NewEvent

// Read all matching events (ascending order):
SequencedEvent[] all = await eventStore.ReadAsync(query);

// Read only events appended after a known position (incremental polling):
SequencedEvent[] newEvents = await eventStore.ReadAsync(query, fromPosition: lastCheckpoint);

// Read in descending order (latest first):
SequencedEvent[] desc = await eventStore.ReadAsync(query, ReadOption.Descending);

// Decision model — read + fold + condition in one call:
DecisionModel<TState> model = await eventStore.BuildDecisionModelAsync(projection);

// Compose up to three projections (single ReadAsync, one AppendCondition spanning all):
var (t1, t2, t3, condition) = await eventStore.BuildDecisionModelAsync(p1, p2, p3);

// N-ary overload — runtime-variable list of homogeneous projections (e.g. shopping cart):
var projections = items.Select(item => PriceProjection(item.BookId)).ToList();
var (states, condition) = await eventStore.BuildDecisionModelAsync(
    (IReadOnlyList<IDecisionProjection<PriceState>>)projections);
// states[i] corresponds to projections[i]

// Execute the full read → decide → append cycle with automatic retry on concurrency conflicts:
TResult result = await eventStore.ExecuteDecisionAsync(async (store, ct) =>
{
    var model = await store.BuildDecisionModelAsync(projection, ct);
    // ... decide, append ...
    return result;
});
```

### Query Building

```csharp
// All events
Query.All()

// Events matching specific criteria
Query.FromItems(params QueryItem[] items)

// Shorthand: events of the given types
Query.FromEventTypes(nameof(InvoiceCreatedEvent))

// Shorthand: events carrying all of the given tags
Query.FromTags(new Tag("studentId", studentId.ToString()))

// Query items support AND/OR logic
new QueryItem
{
    EventTypes = ["EventA", "EventB"],  // EventA OR EventB
    Tags = [tagA, tagB]                 // AND tagA AND tagB
}
```

### AppendCondition (DCB)

```csharp
new AppendCondition
{
    // Fail if any event matches this query
    FailIfEventsMatch = query,

    // Only check events AFTER this position (optional)
    AfterSequencePosition = 42
}
```

### CommandResult and CommandResult\<T\>

Lightweight return types for command handlers. Use `CommandResult` when the handler produces no value, or `CommandResult<T>` when the handler returns a result (e.g. a generated ID or number):

```csharp
// No return value
public async Task<CommandResult> HandleAsync(RegisterStudentCommand cmd, IEventStore store)
{
    // ...
    return CommandResult.Ok();
    return CommandResult.Fail("A student with this email already exists.");
}

// With a return value
public async Task<CommandResult<int>> HandleAsync(CreateInvoiceCommand cmd, IEventStore store)
{
    // ...
    return CommandResult<int>.Ok(nextInvoiceNumber);
    return CommandResult<int>.Fail("Concurrent update — please retry.");
}

// Consume the result in the API endpoint:
var result = await mediator.InvokeAsync<CommandResult<int>>(command);
if (!result.Success) return Results.BadRequest(result.ErrorMessage);
return Results.Created($"/invoices/{result.Value}", new { invoiceNumber = result.Value });
```

### IProjectionStore\<TState\>

Query projections (read models). Resolved from DI as `IProjectionStore<TState>`:

```csharp
public interface IProjectionStore<TState> where TState : class
{
    // Get a single projection instance by key
    Task<TState?> GetAsync(string key);

    // Get all projection instances
    Task<IReadOnlyList<TState>> GetAllAsync();

    // Filter in-memory with a predicate
    Task<IReadOnlyList<TState>> QueryAsync(Func<TState, bool> predicate);

    // Query by a single tag — index-based, requires [ProjectionTags] (see above)
    Task<IReadOnlyList<TState>> QueryByTagAsync(Tag tag);

    // Query by multiple tags with AND logic — index-based, requires [ProjectionTags]
    Task<IReadOnlyList<TState>> QueryByTagsAsync(IEnumerable<Tag> tags);
}
```

```csharp
// Inject the typed store directly — no projection name string needed
public class CourseController(IProjectionStore<CourseShortInfo> courseStore)
{
    public async Task<IReadOnlyList<CourseShortInfo>> GetAvailableAsync() =>
        await courseStore.QueryByTagsAsync([new Tag("IsFull", "False")]);

    public async Task<CourseShortInfo?> GetByIdAsync(Guid courseId) =>
        await courseStore.GetAsync(courseId.ToString());
}
```

### IProjectionManager

Manages live projection lifecycle — registration, incremental updates, and checkpoint tracking. In normal use this is handled automatically by the background daemon:

```csharp
public interface IProjectionManager
{
    // Register a projection definition (called during startup)
    void RegisterProjection<TState>(IProjectionDefinition<TState> definition) where TState : class;

    // Apply new events to all registered projections (called by the daemon)
    Task UpdateAsync(SequencedEvent[] events, CancellationToken cancellationToken = default);

    // Read the last processed event position for a named projection
    Task<long> GetCheckpointAsync(string projectionName, CancellationToken cancellationToken = default);

    // Names of all currently registered projections
    IReadOnlyList<string> GetRegisteredProjections();
}
```

### IProjectionRebuilder

Rebuild projections from scratch — for disaster recovery, deploying projection logic fixes,
or post-migration replays. Available from DI alongside `IProjectionManager`:

```csharp
public interface IProjectionRebuilder
{
    // Rebuild a single named projection
    Task<ProjectionRebuildResult> RebuildAsync(
        string projectionName,
        CancellationToken cancellationToken = default);

    // Rebuild all registered projections in parallel (respects MaxConcurrentRebuilds)
    // forceRebuild: true  — rebuild every projection regardless of checkpoint
    // forceRebuild: false — only rebuild projections with no checkpoint file
    Task<ProjectionRebuildResult> RebuildAllAsync(
        bool forceRebuild = false,
        CancellationToken cancellationToken = default);

    // Rebuild a specific subset — useful after fixing a bug in one projection
    Task<ProjectionRebuildResult> RebuildAsync(
        string[] projectionNames,
        CancellationToken cancellationToken = default);

    // Poll current rebuild progress
    Task<ProjectionRebuildStatus> GetRebuildStatusAsync();
}
```

Expose as an admin endpoint (add proper authentication in production):

```csharp
app.MapPost("/admin/projections/rebuild", async (IProjectionRebuilder rebuilder) =>
{
    var result = await rebuilder.RebuildAllAsync(forceRebuild: false);
    return result.Success
        ? Results.Ok(result)
        : Results.Problem($"Rebuild failed: {string.Join(", ", result.FailedProjections)}");
})
.RequireAuthorization("Admin");
```

---

## 💡 Full Example

The following example is taken directly from the [Course Management sample](Samples/Opossum.Samples.CourseManagement/) and shows the full DCB pattern at its most expressive: **three independent business invariants enforced atomically through a single read**.

Enrolling a student in a course requires checking three separate concerns simultaneously:

- ✅ **Course capacity** — the course must exist and have available seats
- ✅ **Student enrollment limit** — the student must be registered and below their tier's course limit  
- ✅ **Duplicate prevention** — the student must not already be enrolled in this course

All three are evaluated from **one `ReadAsync` call**. The resulting `AppendCondition` spans all three queries automatically — a concurrent write matching any of them will cause `ExecuteDecisionAsync` to retry from scratch, with no manual retry logic required.

```csharp
// ── Step 1: Domain events ─────────────────────────────────────────────────────

public sealed record CourseCreatedEvent(
    Guid CourseId,
    string Name,
    string Description,
    int MaxStudentCount) : IEvent;

public sealed record StudentRegisteredEvent(
    Guid StudentId,
    string FirstName,
    string LastName,
    string Email) : IEvent;

public sealed record StudentEnrolledToCourseEvent(
    Guid CourseId,
    Guid StudentId) : IEvent;

// ── Step 2: Decision state types — one per business concern ──────────────────

// null until the course is created
public sealed record CourseCapacityState(int MaxCapacity, int CurrentEnrollmentCount)
{
    public bool IsFull => CurrentEnrollmentCount >= MaxCapacity;
}

// null until the student is registered
public sealed record StudentEnrollmentLimitState(EnrollmentTier Tier, int CurrentCourseCount)
{
    public int MaxAllowed => GetMaxCoursesByTier(Tier);   // e.g. Basic = 3, Professional = 10
    public bool IsAtLimit => CurrentCourseCount >= MaxAllowed;
}

// ── Step 3: Three focused, ephemeral decision projections ─────────────────────

public static class CourseEnrollmentProjections
{
    // Is the course over capacity?
    public static IDecisionProjection<CourseCapacityState?> CourseCapacity(Guid courseId) =>
        new DecisionProjection<CourseCapacityState?>(
            initialState: null,
            query: Query.FromItems(new QueryItem
            {
                EventTypes =
                [
                    nameof(CourseCreatedEvent),
                    nameof(CourseStudentLimitModifiedEvent),
                    nameof(StudentEnrolledToCourseEvent)
                ],
                Tags = [new Tag("courseId", courseId.ToString())]
            }),
            apply: (state, evt) => evt.Event.Event switch
            {
                CourseCreatedEvent created =>
                    new CourseCapacityState(created.MaxStudentCount, 0),
                CourseStudentLimitModifiedEvent modified when state is not null =>
                    state with { MaxCapacity = modified.NewMaxStudentCount },
                StudentEnrolledToCourseEvent when state is not null =>
                    state with { CurrentEnrollmentCount = state.CurrentEnrollmentCount + 1 },
                _ => state
            });

    // Has the student hit their tier's course limit?
    public static IDecisionProjection<StudentEnrollmentLimitState?> StudentEnrollmentLimit(Guid studentId) =>
        new DecisionProjection<StudentEnrollmentLimitState?>(
            initialState: null,
            query: Query.FromItems(new QueryItem
            {
                EventTypes =
                [
                    nameof(StudentRegisteredEvent),
                    nameof(StudentSubscriptionUpdatedEvent),
                    nameof(StudentEnrolledToCourseEvent)
                ],
                Tags = [new Tag("studentId", studentId.ToString())]
            }),
            apply: (state, evt) => evt.Event.Event switch
            {
                StudentRegisteredEvent =>
                    new StudentEnrollmentLimitState(EnrollmentTier.Basic, 0),
                StudentSubscriptionUpdatedEvent updated when state is not null =>
                    state with { Tier = updated.EnrollmentTier },
                StudentEnrolledToCourseEvent when state is not null =>
                    state with { CurrentCourseCount = state.CurrentCourseCount + 1 },
                _ => state
            });

    // Is this exact student–course pair already enrolled?
    // Both tags are required, so only the precise pair triggers this projection.
    public static IDecisionProjection<bool> AlreadyEnrolled(Guid courseId, Guid studentId) =>
        new DecisionProjection<bool>(
            initialState: false,
            query: Query.FromItems(new QueryItem
            {
                EventTypes = [nameof(StudentEnrolledToCourseEvent)],
                Tags =
                [
                    new Tag("courseId", courseId.ToString()),
                    new Tag("studentId", studentId.ToString())
                ]
            }),
            apply: (_, _) => true);   // any match means already enrolled
}

// ── Step 4: Command + handler — read → decide → append with automatic retry ──

public sealed record EnrollStudentToCourseCommand(Guid CourseId, Guid StudentId);

public sealed class EnrollStudentToCourseCommandHandler
{
    public async Task<CommandResult> HandleAsync(
        EnrollStudentToCourseCommand command,
        IEventStore eventStore)
    {
        try
        {
            return await eventStore.ExecuteDecisionAsync(
                (store, ct) => TryEnrollAsync(command, store));
        }
        catch (AppendConditionFailedException)
        {
            return CommandResult.Fail(
                "Failed to enroll student due to concurrent updates. Please try again.");
        }
    }

    private static async Task<CommandResult> TryEnrollAsync(
        EnrollStudentToCourseCommand command,
        IEventStore eventStore)
    {
        // One ReadAsync call materialises all three projections simultaneously.
        // appendCondition spans all three queries — if a concurrent write matches
        // ANY of them between this read and the append, AppendConditionFailedException
        // is thrown and ExecuteDecisionAsync retries automatically.
        var (courseCapacity, studentLimit, alreadyEnrolled, appendCondition) =
            await eventStore.BuildDecisionModelAsync(
                CourseEnrollmentProjections.CourseCapacity(command.CourseId),
                CourseEnrollmentProjections.StudentEnrollmentLimit(command.StudentId),
                CourseEnrollmentProjections.AlreadyEnrolled(command.CourseId, command.StudentId));

        if (courseCapacity is null)
            return CommandResult.Fail("Course does not exist.");
        if (studentLimit is null)
            return CommandResult.Fail("Student is not registered.");
        if (alreadyEnrolled)
            return CommandResult.Fail("Student is already enrolled in this course.");
        if (courseCapacity.IsFull)
            return CommandResult.Fail($"Course is at maximum capacity ({courseCapacity.MaxCapacity} students).");
        if (studentLimit.IsAtLimit)
            return CommandResult.Fail($"Student has reached their enrollment limit ({studentLimit.MaxAllowed} courses for {studentLimit.Tier} tier).");

        NewEvent enrollmentEvent = new StudentEnrolledToCourseEvent(
            CourseId: command.CourseId,
            StudentId: command.StudentId)
            .ToDomainEvent()
            .WithTag("courseId", command.CourseId.ToString())
            .WithTag("studentId", command.StudentId.ToString())
            .WithTimestamp(DateTimeOffset.UtcNow);

        // appendCondition guarantees all three invariants hold atomically at write time
        await eventStore.AppendAsync(enrollmentEvent, appendCondition);
        return CommandResult.Ok();
    }
}
```

**Why this matters:** Three separate business rules — spanning two independent tag-based queries (course events tagged with `courseId`, student events tagged with `studentId`) — are enforced with a single read and a single atomic append condition. There are no distributed locks, no sagas, and no two-phase commits. The DCB pattern handles concurrent writes through optimistic concurrency with automatic retry built in to `ExecuteDecisionAsync`.

See the [Course Management sample](Samples/Opossum.Samples.CourseManagement/) for the full working application including read-side projections and API endpoints.

---

## 🔄 Event-Sourced Aggregate — Alternative Write-Side Pattern

> **This is an alternative to the Decision Model pattern shown above — not a required addition.**
> Pick one style and apply it consistently. The sample includes both so you can compare them
> side by side on the same domain.

Opossum also supports the classic [Event-Sourced Aggregate](https://dcb.events/examples/event-sourced-aggregate/#dcb-approach) pattern. Instead of stateless ephemeral projections, all course state is encapsulated in a reconstituted aggregate object. The DCB insight is that **the repository replaces the traditional named-stream lock with a tag-scoped `AppendCondition`** — no stream concept needed.

### Choosing Between the Two Patterns

| | DCB Decision Model | Event-Sourced Aggregate |
|---|---|---|
| **State lives in** | Ephemeral in-memory fold, discarded after each command | Reconstituted aggregate object |
| **Invariants span** | Multiple entity types in one read (e.g. course capacity AND student tier) | One entity type (course only) |
| **Retry handled by** | `ExecuteDecisionAsync` (automatic exponential backoff) | Manual retry loop in the caller |
| **Concurrency boundary** | Union of all projection queries | All events for a single `courseId` tag |
| **Best for** | Cross-cutting business rules | Rich domain models with many entity-internal invariants |

### How the Aggregate Repository Works

```csharp
// Load: query by tag — no named stream needed
public async Task<CourseAggregate?> LoadAsync(Guid courseId)
{
    var query = Query.FromTags(new Tag("courseId", courseId.ToString()));
    var events = await eventStore.ReadAsync(query);

    if (events.Length == 0)
        return null;                            // course does not exist

    return CourseAggregate.Reconstitute(events); // replay events into state
}

// Save: append with the DCB tag-scoped optimistic lock
public async Task SaveAsync(CourseAggregate aggregate, CancellationToken ct = default)
{
    var query = Query.FromTags(new Tag("courseId", aggregate.CourseId.ToString()));

    var condition = new AppendCondition
    {
        FailIfEventsMatch = query,
        // null when Version == 0 (new): reject if ANY course event already exists.
        // Otherwise: reject only if a new event appeared after our last read.
        AfterSequencePosition = aggregate.Version == 0 ? null : aggregate.Version
    };

    var newEvents = aggregate.PullRecordedEvents()
        .Select(e => (NewEvent)(e.ToDomainEvent()
            .WithTag("courseId", aggregate.CourseId.ToString())
            .WithTimestamp(DateTimeOffset.UtcNow)))
        .ToArray();

    // Throws AppendConditionFailedException on conflict — reload and retry.
    await eventStore.AppendAsync(newEvents, condition, ct);
}
```

### The Aggregate Class (pure C#, no Opossum machinery)

```csharp
public sealed class CourseAggregate
{
    private readonly List<IEvent> _recordedEvents = [];

    public Guid CourseId { get; private set; }
    public int Capacity { get; private set; }
    public int EnrollmentCount { get; private set; }

    // Global store position of the last event seen — used as AfterSequencePosition.
    // Note: this is store-wide monotonic, not a per-aggregate counter.
    public long Version { get; private set; }

    public static CourseAggregate Create(Guid id, string name, string description, int maxStudents)
    {
        var instance = new CourseAggregate();
        instance.RecordEvent(new CourseCreatedEvent(id, name, description, maxStudents));
        return instance;
    }

    public static CourseAggregate Reconstitute(SequencedEvent[] events)
    {
        var instance = new CourseAggregate();
        foreach (var e in events)
        {
            instance.Apply(e.Event.Event);
            instance.Version = e.Position;
        }
        return instance;
    }

    public void ChangeCapacity(int newCapacity)
    {
        if (newCapacity == Capacity)
            throw new InvalidOperationException($"Course already has capacity {newCapacity}.");
        if (newCapacity < EnrollmentCount)
            throw new InvalidOperationException($"Can't set capacity below current enrollment.");

        RecordEvent(new CourseStudentLimitModifiedEvent(CourseId, newCapacity));
    }

    public void SubscribeStudent(Guid studentId)
    {
        if (EnrollmentCount >= Capacity)
            throw new InvalidOperationException("Course is already fully booked.");

        RecordEvent(new StudentEnrolledToCourseEvent(CourseId, studentId));
    }

    public IEvent[] PullRecordedEvents()
    {
        var events = _recordedEvents.ToArray();
        _recordedEvents.Clear();
        return events;
    }

    private void RecordEvent(IEvent @event) { _recordedEvents.Add(@event); Apply(@event); }

    private void Apply(IEvent @event)
    {
        switch (@event)
        {
            case CourseCreatedEvent c:   CourseId = c.CourseId; Capacity = c.MaxStudentCount; break;
            case CourseStudentLimitModifiedEvent m: Capacity = m.NewMaxStudentCount; break;
            case StudentEnrolledToCourseEvent:      EnrollmentCount++; break;
        }
    }
}
```

### Retry Pattern in the Endpoint

```csharp
// Reload → reapply → retry on concurrent write; last attempt propagates → HTTP 409
for (var attempt = 0; attempt < MaxRetries; attempt++)
{
    var aggregate = await repository.LoadAsync(courseId);
    if (aggregate is null)
        return Results.NotFound();

    try
    {
        aggregate.ChangeCapacity(request.NewCapacity);
        await repository.SaveAsync(aggregate);
        return Results.Ok();
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(ex.Message);   // invariant violation — no retry
    }
    catch (AppendConditionFailedException) when (attempt < MaxRetries - 1)
    {
        // concurrent write — reload fresh state and try again
    }
}
```

The full implementation lives in
[`Samples/Opossum.Samples.CourseManagement/CourseAggregate/`](Samples/Opossum.Samples.CourseManagement/CourseAggregate/).
Aggregate endpoints are tagged **"Aggregate (Event-Sourced)"** in the Scalar UI to distinguish
them from the Decision Model endpoints tagged **"Commands"**.

---

## 🗺️ DCB Examples Coverage

All 7 examples from [dcb.events/examples/](https://dcb.events/examples/) are implemented in the [Course Management sample](Samples/Opossum.Samples.CourseManagement/):

| DCB Example | Domain Adaptation | Key Pattern | Sample Location |
|---|---|---|---|
| [Course Subscriptions](https://dcb.events/examples/course-subscriptions/) | Student enrollment (capacity + tier limit + duplicate check) | `BuildDecisionModelAsync` (3-projection) | `CourseEnrollment/` |
| [Unique Username](https://dcb.events/examples/unique-username/) | Student email uniqueness | Raw `AppendCondition` (direct DCB API) | `StudentRegistration/` |
| [Invoice Number](https://dcb.events/examples/invoice-number/) | Gap-free invoice numbering | `ReadLastAsync` + `AppendCondition` | `InvoiceCreation/` |
| [Dynamic Product Price](https://dcb.events/examples/dynamic-product-price/) | Course book prices with grace period & shopping cart | N-ary `BuildDecisionModelAsync` + `TimeProvider` | `CourseBookPurchase/` |
| [Event-Sourced Aggregate](https://dcb.events/examples/event-sourced-aggregate/) | Course aggregate (capacity + enrollment) | DCB tag-scoped `AppendCondition` in repository | `CourseAggregate/` |
| [Opt-In Token](https://dcb.events/examples/opt-in-token/) | Exam registration tokens (issue / redeem / revoke) | Enum-state projection; event store as token registry | `ExamRegistration/` |
| [Prevent Record Duplication](https://dcb.events/examples/prevent-record-duplication/) | Course announcements with client idempotency token | `BuildDecisionModelAsync` (2-projection) + idempotency projection | `CourseAnnouncement/` |

---

## 🔢 Consecutive Sequences — Invoice Numbers

The [Invoice Number example](https://dcb.events/examples/invoice-number/) shows how to generate a gap-free, monotonically increasing sequence without a separate sequence table.

The key primitive is `ReadLastAsync` — it returns the single highest-position event matching a query in **O(1) file reads** (one index lookup, one file read), regardless of how many total events the store contains.

```csharp
// The query has NO tag filter — it spans ALL InvoiceCreatedEvents globally.
// Any new invoice created by anyone invalidates the "last number" we just read.
var query = Query.FromEventTypes(nameof(InvoiceCreatedEvent));

// Step 1 — Read: find the most recent invoice (O(1) file reads)
var last = await eventStore.ReadLastAsync(query);

// Step 2 — Decide: next consecutive number
var nextNumber = last is null ? 1 : ((InvoiceCreatedEvent)last.Event.Event).InvoiceNumber + 1;

// Step 3 — Append with a guard that rejects if ANY InvoiceCreatedEvent appeared since our read.
// AfterSequencePosition = null on the first invoice means "reject if ANY invoice already exists"
// — closing the bootstrap race condition.
var condition = new AppendCondition
{
    FailIfEventsMatch = query,
    AfterSequencePosition = last?.Position
};

NewEvent newEvent = new InvoiceCreatedEvent(nextNumber, customerId, amount)
    .ToDomainEvent()
    .WithTag("invoiceNumber", nextNumber.ToString())
    .WithTag("customerId", customerId.ToString())
    .WithTimestamp(DateTimeOffset.UtcNow);

// Throws AppendConditionFailedException on conflict — ExecuteDecisionAsync retries automatically.
await eventStore.AppendAsync(newEvent, condition);
```

**Why this works:** The consistency boundary is the entire set of invoice creation events — if any new invoice appears between our read and our append, the condition fires and `ExecuteDecisionAsync` retries the full cycle automatically.

See [`InvoiceCreation/CreateInvoice.cs`](Samples/Opossum.Samples.CourseManagement/InvoiceCreation/CreateInvoice.cs) in the sample for the full implementation.

---

## 💰 Dynamic Product Price — Course Books

The [Dynamic Product Price example](https://dcb.events/examples/dynamic-product-price/) shows three progressively complex features, all implemented as the Course Books feature in the sample.

### Feature 1 — Current Price (no grace period)

A single `DecisionProjection` folds the book's defined price. The displayed price must match the stored price exactly at the moment of purchase.

### Feature 2 — Grace Period

After a price change, both the old and the new price remain valid for a configurable window (default: 30 minutes). The fold function needs wall-clock time — use the `TimeProvider` constructor overload so the projection is unit-testable without sleeping:

```csharp
new DecisionProjection<CourseBookPriceState>(
    initialState: CourseBookPriceState.Empty,
    query: Query.FromItems(new QueryItem
    {
        EventTypes = [nameof(CourseBookDefinedEvent), nameof(CourseBookPriceChangedEvent)],
        Tags = [new Tag("bookId", bookId.ToString())]
    }),
    apply: (state, evt, timeProvider) =>
    {
        var age = timeProvider.GetUtcNow() - evt.Metadata.Timestamp;
        return evt.Event.Event switch
        {
            CourseBookDefinedEvent e     => state.ApplyDefined(e.Price, age, gracePeriod),
            CourseBookPriceChangedEvent e => state.ApplyPriceChanged(e.NewPrice, age, gracePeriod),
            _ => state
        };
    },
    timeProvider: timeProvider);  // null → TimeProvider.System in production
```

**Testing with time control:** Inject `FakeTimeProvider` (from `Microsoft.Extensions.TimeProvider.Testing`) to advance time in unit tests without sleeping:

```csharp
var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
var projection = CourseBookPriceProjections.PriceWithGracePeriod(bookId, timeProvider: fakeTime);
// ... append a price-changed event, then:
fakeTime.Advance(TimeSpan.FromMinutes(31));  // grace period expires
// projection now accepts only the new price
```

### Feature 3 — Shopping Cart (N-ary overload)

Validate the price of every item in a cart in a **single event store read** with a **single `AppendCondition` spanning all items**:

```csharp
// Build one projection per cart item
var projections = command.Items
    .Select(item => CourseBookPriceProjections.PriceWithGracePeriod(item.BookId))
    .ToList();

// One ReadAsync call → states[i] corresponds to projections[i]
var (states, appendCondition) = await eventStore.BuildDecisionModelAsync(
    (IReadOnlyList<IDecisionProjection<CourseBookPriceState>>)projections);

for (var i = 0; i < command.Items.Count; i++)
{
    if (states[i].CurrentPrice is null)
        return CommandResult.Fail($"Book '{command.Items[i].BookId}' does not exist.");
    if (!states[i].IsValidPrice(command.Items[i].DisplayedPrice))
        return CommandResult.Fail($"Price changed for book '{command.Items[i].BookId}'. Please refresh.");
}

// appendCondition covers all books atomically — a concurrent price change for any book
// in the cart invalidates the entire order and triggers a retry.
await eventStore.AppendAsync(orderEvent, appendCondition);
```

See [`CourseBookPurchase/`](Samples/Opossum.Samples.CourseManagement/CourseBookPurchase/) and [`CourseBookManagement/`](Samples/Opossum.Samples.CourseManagement/CourseBookManagement/) in the sample for the full implementation.

---

## 🎟️ Opt-In Token — Server-Generated Single-Use Tokens

The [Opt-In Token example](https://dcb.events/examples/opt-in-token/) shows how the event store itself can **replace a persistent "valid tokens" table entirely**.

> **Contrast with idempotency tokens:** Idempotency tokens are *client-generated* and protect against retry duplicates. Opt-In tokens are *server-generated* (the instructor creates them), handed out to a specific student, and consumed exactly once.

The key insight: a query scoped to `examToken:{tokenId}` IS the token registry — no `IProjectionDefinition` for token state is needed for correctness. A single enum projection replaces the two-bool pattern (`WasIssued` + `WasRedeemed`) and naturally accommodates revocation as a third state:

```csharp
public enum ExamTokenStatus { NotIssued, Issued, Revoked, Redeemed }

public sealed record ExamTokenState(ExamTokenStatus Status, Guid ExamId);

IDecisionProjection<ExamTokenState> TokenStatus(Guid tokenId) =>
    new DecisionProjection<ExamTokenState>(
        initialState: new ExamTokenState(ExamTokenStatus.NotIssued, Guid.Empty),
        query: Query.FromItems(new QueryItem
        {
            EventTypes =
            [
                nameof(ExamRegistrationTokenIssuedEvent),
                nameof(ExamRegistrationTokenRevokedEvent),
                nameof(ExamRegistrationTokenRedeemedEvent)
            ],
            Tags = [new Tag("examToken", tokenId.ToString())]
        }),
        apply: (state, evt) => evt.Event.Event switch
        {
            ExamRegistrationTokenIssuedEvent issued => new ExamTokenState(ExamTokenStatus.Issued, issued.ExamId),
            ExamRegistrationTokenRevokedEvent       => state with { Status = ExamTokenStatus.Revoked },
            ExamRegistrationTokenRedeemedEvent      => state with { Status = ExamTokenStatus.Redeemed },
            _                                       => state
        });

// Redeem — pattern-match the status; no if/else chains needed
var model = await eventStore.BuildDecisionModelAsync(TokenStatus(command.TokenId));

return model.State.Status switch
{
    ExamTokenStatus.NotIssued => CommandResult.Fail("Token not found."),
    ExamTokenStatus.Revoked   => CommandResult.Fail("Token has been revoked."),
    ExamTokenStatus.Redeemed  => CommandResult.Fail("Token has already been used."),
    _                         => await AppendRedemptionAsync(command, eventStore, model.State, model.AppendCondition)
};
```

**Concurrency safety:** Two concurrent redemptions of the same token — one succeeds; the other reads `Redeemed` on retry (via `ExecuteDecisionAsync`) and receives the appropriate error. Different tokens never contend because each query is scoped to a unique `examToken` tag value.

See [`ExamRegistration/`](Samples/Opossum.Samples.CourseManagement/ExamRegistration/) in the sample for issue, redeem, and revoke implementations.

---

## ⚡ Performance

### Typical Throughput

**Benchmarked on Windows 11, .NET 10.0.2, SSD storage (2026-03-11):**

| Operation | Throughput | Notes |
|-----------|-----------|-------|
| **Append (FlushImmediately = true, single event)** | ~55 events/sec | True durability: event + index files flushed (~18ms/event on SSD) |
| **Append (FlushImmediately = true, batch 10)** | ~78 events/sec | ~13ms/event when amortised over a batch |
| **Append (FlushImmediately = false)** | ~185 events/sec | OS page cache only (testing/dev mode — data loss risk on power failure) |
| **Tag query (high selectivity)** | ~524 μs | Index-based, excellent for targeted queries |
| **Tag query (1K events)** | ~11.6 ms | Sub-linear scaling |
| **ReadLastAsync (100 → 10K events)** | 948–1,158 μs | Near-O(1): one index lookup + one file read |
| **Read by EventType (10K events)** | ~206 ms | Index-based |
| **Projection rebuild** | ~4.5 ms / 50 events | Write-through; bounded memory regardless of unique key count |
| **Incremental projection update** | ~4.6 μs / 0 B | ~978× faster than full rebuild; zero allocation |

### Query Performance by Selectivity

| Selectivity | 10K Events | Performance |
|------------|-----------|-------------|
| **High** (few matches) | ~524 μs | ⭐ Excellent - tag index highly effective |
| **Medium** (moderate matches) | ~5.3 ms | ✅ Good - typical use case |
| **Low** (many matches) | ~103 ms | ⚠️ Expected - must deserialize many events |

### Optimization Tips

✅ **Use SSDs** - Flush operations are much faster (10ms vs 50ms+ on HDD)  
✅ **Use tag-based queries** - ~524μs for high selectivity vs ~5.3ms for broader queries  
✅ **Enable parallel projection rebuilding** - `MaxConcurrentRebuilds` config; Concurrency=4 is ~47 % faster than sequential for large datasets (write-through I/O parallelises well)  
✅ **Use incremental projection updates** - ~978× faster than full rebuild; zero allocation  
✅ **Optimize query selectivity** - More specific tags = faster queries  
⚠️ **Avoid Query.All() for large datasets** - Use projections for read models instead  
⚠️ **Use `FlushEventsImmediately = false`** for testing only (data loss risk on power failure)

### Descending Order Queries

✅ **Zero performance overhead** - Descending order is as fast as ascending (optimized in-place)

Perfect for:
- Activity feeds (latest first)
- Recent orders
- Audit log displays

### Scalability Limits

Opossum is designed for **single-server deployments**:

| Metric | Recommended Limit | Notes |
|--------|------------------|-------|
| **Events per day** | < 100,000 | ~1 event/second average |
| **Total events** | < 10 million | Performance degrades with file count |
| **Projections** | < 100 types | More = slower startup |
| **Tags per event** | < 20 | Affects index write speed |
| **Concurrent appends** | < 100 simultaneous | File system lock contention |

**Beyond these limits?** Consider cloud-based event stores (EventStoreDB, Azure Event Hubs).

**Detailed benchmarks:** See `docs/benchmarking/results/20260311/`

> **Rebuild performance note (0.5.0):** The write-through projection rebuild introduced in 0.5.0
> writes each `SaveAsync` call directly to disk during rebuild rather than accumulating state
> in memory. For large datasets, sequential rebuild of 4 projections takes ~3.7 s; with
> `MaxConcurrentRebuilds = 4` this drops to ~2.0 s (~47 % faster). This is the expected
> trade-off for bounded memory (no more OOM with 1 M+ unique keys) and crash-recovery
> durability. Rebuild is a rare, background operation — the memory and safety guarantees
> outweigh the I/O cost.

### IEventStoreAdmin

Administrative operations for store lifecycle management. Resolved from DI as `IEventStoreAdmin`:

```csharp
public interface IEventStoreAdmin
{
    // Permanently delete all store data (events, indices, projections, ledger).
    // Write-protected files are handled transparently — read-only attributes are stripped.
    // The store directory is recreated automatically on the next AppendAsync/ReadAsync call.
    Task DeleteStoreAsync();
}
```

---

## 📡 OpenTelemetry

Opossum emits distributed traces via `System.Diagnostics.ActivitySource` — no extra packages required. Register the activity source name with your OpenTelemetry pipeline:

```csharp
using Opossum.Telemetry;

tracerProviderBuilder.AddSource(OpossumTelemetry.ActivitySourceName); // "Opossum"
```

Traced operations:

| Activity | Operation Name | Description |
|---|---|---|
| `AppendAsync` | `EventStore.Append` | Every append, including batch appends |
| `ReadAsync` | `EventStore.Read` | Every query read |
| `ReadLastAsync` | `EventStore.ReadLast` | Every last-event read |
| `RebuildAsync` | `Projection.Rebuild` | Every projection rebuild |

When no listener is attached the overhead is a single null-check per operation.

---

## ⚠️ Known Limitations

### Single-Server / Single-Context Design

Opossum is designed as a **single-context** event store — one store name per `AddOpossum()`
call. Multi-tenancy is handled at the application layer (e.g. per-tenant root paths).
See [ADR-004](docs/decisions/004-single-context-by-design.md) for the full rationale.

### ProjectionTagIndex Lock Growth (long-running processes, high cardinality)

Each unique projection key that is ever written to a `[ProjectionTags]`-enabled projection
causes a per-key `SemaphoreSlim` to be allocated and held in memory for the lifetime of
the process. For projections with high-cardinality keys (e.g. one projection per order
over years), this map grows without bound — ~48 bytes per key.

**Impact:** Negligible for typical deployments (< 100 K unique keys = < 5 MB).
Relevant only for long-running processes on high-cardinality projections.

**Fix target:** 0.6.0 (lock pooling or weak-reference cleanup).

---

## 📚 Documentation

- [DCB Specification](https://dcb.events/specification/) - Dynamic Consistency Boundaries pattern
- [Sample Application](Samples/Opossum.Samples.CourseManagement/) - Complete working example
- [API Documentation](docs/) - Detailed implementation docs

---

## 🤝 Contributing

Contributions are welcome! Please read [CONTRIBUTING.md](CONTRIBUTING.md) first.

---

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

## 🙏 Acknowledgments

- Inspired by the [DCB Specification](https://dcb.events/)
- Built for real-world use cases in automotive retail and SMB applications

---

**Made with ❤️ for developers who value simplicity and local-first data ownership.**
