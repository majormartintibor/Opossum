# ?? Opossum

**A file system-based event store for .NET that implements the DCB (Dynamic Consistency Boundaries) specification.**

Opossum turns your file system into a fully functional event store with projections, optimistic concurrency control, and tag-based indexing. Perfect for scenarios where simplicity, offline operation, and local data sovereignty matter more than cloud scalability.

[![NuGet](https://img.shields.io/nuget/v/Opossum.svg)](https://www.nuget.org/packages/Opossum/)
[![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download)
[![License](https://img.shields.io/github/license/majormartintibor/Opossum.svg)](LICENSE)

---

## ?? Table of Contents

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
- [Performance](#-performance)
- [Known Limitations](#️-known-limitations)

---

## ?? What is Opossum?

Opossum is an **event sourcing framework** that uses your **file system as the database**. It's designed for applications that need:

- ? **100% offline operation** - No internet required
- ? **Complete audit trail** - Every state change is an immutable event
- ? **Local data ownership** - Your data never leaves your server
- ? **Optimistic concurrency** - Built-in DCB pattern for consistency
- ? **Simple deployment** - Just files, no database server to manage
- ? **Projections** - Materialized views that rebuild from events
- ? **Tag-based indexing** - Fast queries without full scans

### What Makes Opossum Different?

Unlike cloud-based event stores (EventStoreDB, Azure Event Hubs) or database-backed solutions, Opossum **stores events directly as files** in a structured directory hierarchy. This makes it ideal for:

- ?? **On-premises applications** (POS systems, dealership software)
- ?? **Offline-first applications** (field service, remote installations)
- ?? **SMB solutions** (where cloud costs don't make sense)
- ?? **Data sovereignty requirements** (keep data in-country/on-site)
- ?? **Development & testing** (no Docker/database setup needed)
- ? **Multi-workstation deployments** (multiple PCs sharing a store on a network drive — cross-process append safety via OS file locking)

---

## ? When to Use Opossum

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

? **Single server/small deployment** (< 100k events/day)  
? **Offline-first requirements**  
? **Simple IT environment** (IT staff comfortable with files/folders)  
? **Budget-conscious** (avoid monthly cloud fees)  
? **Data residency requirements** (legal/compliance)  
? **Complete audit trail needed**

---

## ? When NOT to Use Opossum

Opossum is **not designed** for:

| Don't Use If... | Use Instead |
|----------------|-------------|
| ? **Distributed systems** across multiple servers | EventStoreDB, Kafka |
| ? **High throughput** (> 100k events/day per server) | Cloud event stores |
| ? **Cloud-native microservices** | Azure Event Hubs, AWS Kinesis |
| ? **Multi-region replication** needed | Distributed event stores |
| ? **Event streaming** to multiple consumers | Kafka, RabbitMQ |
| ? **Massive scale** (millions of events) | Purpose-built event stores |

**Rule of thumb:** If your application runs on a single server (or small cluster) and needs offline capabilities, Opossum is great. If you need cloud-scale distribution, choose a cloud-native solution.

---

## ?? Quick Start

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
    Tags = [new Tag { Key = "studentId", Value = studentId.ToString() }]
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

    public string[] EventTypes => new[]
    {
        nameof(StudentRegisteredEvent),
        nameof(StudentEnrolledToCourseEvent)
    };

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

            StudentEnrolledToCourseEvent enrolled when current != null =>
                current with { EnrolledCoursesCount = current.EnrolledCoursesCount + 1 },

            _ => current
        };
    }
}
```

### 7. Query Projections

```csharp
using Opossum.Projections;

public class StudentController
{
    private readonly IProjectionStore _projectionStore;

    public async Task<StudentDetails?> GetStudentAsync(Guid studentId)
    {
        return await _projectionStore.GetAsync<StudentDetails>(
            "StudentDetails",
            studentId.ToString());
    }
}
```

---

## ?? Core Concepts

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
    Tags = [new Tag { Key = "studentId", Value = "123" }]
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
    Tags = [new Tag { Key = "studentId", Value = "123" }]
});
```

### Projections

**Materialized views** rebuilt from events. Think of them as denormalized read models:

```csharp
[ProjectionDefinition("CourseEnrollmentCount")]
public class CourseEnrollmentProjection : IProjectionDefinition<CourseEnrollmentState>
{
    public CourseEnrollmentState? Apply(CourseEnrollmentState? current, SequencedEvent evt)
    {
        return evt.Event.Event switch
        {
            StudentEnrolledToCourseEvent =>
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
                Tags = [new Tag { Key = "studentId", Value = enrolled.StudentId.ToString() }],
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
        yield return new Tag { Key = "IsFull", Value = state.IsFull.ToString() };
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
    [new Tag { Key = "IsFull", Value = "False" }]);
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
            Tags = [new Tag { Key = "id", Value = id.ToString() }]
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

Three `BuildDecisionModelAsync` overloads are available: single-projection (`BuildDecisionModelAsync<T>` → `DecisionModel<T>`), two-projection (`(T1, T2, AppendCondition)`), and three-projection (`(T1, T2, T3, AppendCondition)`). Use `ExecuteDecisionAsync` to wrap the entire cycle with automatic exponential-backoff retry.

See the [Full Example](#-full-example) section for a complete real-world walkthrough enforcing three invariants across two independent tag-based queries in a single read.

### Dynamic Consistency Boundaries (DCB)

Enforce **optimistic concurrency** using append conditions:

```csharp
// Ensure email is unique across ALL students
var validateEmailQuery = Query.FromItems(new QueryItem
{
    Tags = [new Tag { Key = "studentEmail", Value = email }]
});

// This will fail if any event with this email already exists
await _eventStore.AppendAsync(
    evt,
    condition: new AppendCondition 
    { 
        FailIfEventsMatch = validateEmailQuery 
    });
```

**Why this matters:** Prevents race conditions without distributed locks.

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

## ?? Configuration

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

    // Rebuild all projections on startup (default: false)
    options.RebuildOnStartup = false;
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
    "RebuildOnStartup": false
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

## ?? How Events Are Stored

Opossum creates a **file-based database** with the following structure:

```
D:\MyApp\EventStore\                 # RootPath
??? MyApplicationContext\             # Context name
    ??? .ledger                       # Ledger file (current sequence position)
    ??? Events\                       # Event files (one per event)
    ?   ??? 0000000001.json           # Event at position 1
    ?   ??? 0000000002.json           # Event at position 2
    ?   ??? ...
    ??? Indices\                      # Index directories
        ??? EventType\                # Index by event type
        ?   ??? StudentRegisteredEvent.idx
        ?   ??? StudentEnrolledToCourseEvent.idx
        ??? Tags\                     # Index by tags
            ??? studentId_123.idx     # All events with tag studentId=123
            ??? studentEmail_test@example.com.idx
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

## ?? API Reference

### IEventStore

Core event store operations:

```csharp
public interface IEventStore
{
    // Append one or more events (position is assigned by the store)
    Task AppendAsync(NewEvent @event, AppendCondition? condition = null);
    Task AppendAsync(NewEvent[] events, AppendCondition? condition = null);

    // Read events matching a query (returns sequenced events with positions)
    // fromPosition: when provided, only events with Position > fromPosition are returned
    Task<SequencedEvent[]> ReadAsync(Query query, ReadOption[]? readOptions, long? fromPosition = null);
}
```

### Extension Methods

```csharp
// Convert a domain event (IEvent) to a fluent DomainEventBuilder, then to NewEvent:
NewEvent evt = new MyEvent(...)
    .ToDomainEvent()                          // IEvent → DomainEventBuilder
    .WithTag("key", "value")                  // add a single tag
    .WithTags(tag1, tag2)                     // add multiple tags
    .WithTimestamp(DateTimeOffset.UtcNow);    // set timestamp
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
        await courseStore.QueryByTagsAsync([new Tag { Key = "IsFull", Value = "False" }]);

    public async Task<CourseShortInfo?> GetByIdAsync(Guid courseId) =>
        await courseStore.GetAsync(courseId.ToString());
}
```

### IProjectionManager

Manage projection lifecycle for operational tasks such as disaster recovery or evolving projections in a live system:

```csharp
public interface IProjectionManager
{
    // Rebuild all projections in parallel (respects MaxConcurrentRebuilds config)
    // forceRebuild: true  = rebuild even projections that already have a checkpoint
    // forceRebuild: false = only rebuild projections with checkpoint = 0 (new/missing)
    Task<ProjectionRebuildResult> RebuildAllAsync(bool forceRebuild = false);

    // Rebuild a single named projection
    Task RebuildAsync(string projectionName);

    // Rebuild a specific subset — useful after fixing a bug in one projection
    Task<ProjectionRebuildResult> RebuildAsync(string[] projectionNames);

    // Poll current rebuild progress
    Task<ProjectionRebuildStatus> GetRebuildStatusAsync();
}
```

Expose as an admin endpoint (add proper authentication in production):

```csharp
app.MapPost("/admin/projections/rebuild", async (IProjectionManager manager) =>
{
    var result = await manager.RebuildAllAsync(forceRebuild: false);
    return result.Success
        ? Results.Ok(result)
        : Results.Problem($"Rebuild failed: {string.Join(", ", result.FailedProjections)}");
})
.RequireAuthorization("Admin");
```

---

## ?? Full Example

The following example is taken directly from the [Course Management sample](Samples/Opossum.Samples.CourseManagement/) and shows the full DCB pattern at its most expressive: **three independent business invariants enforced atomically through a single read**.

Enrolling a student in a course requires checking three separate concerns simultaneously:

- ? **Course capacity** — the course must exist and have available seats
- ? **Student enrollment limit** — the student must be registered and below their tier's course limit  
- ? **Duplicate prevention** — the student must not already be enrolled in this course

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
                Tags = [new Tag { Key = "courseId", Value = courseId.ToString() }]
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
                Tags = [new Tag { Key = "studentId", Value = studentId.ToString() }]
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
                    new Tag { Key = "courseId", Value = courseId.ToString() },
                    new Tag { Key = "studentId", Value = studentId.ToString() }
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

## ⚡ Performance

### Typical Throughput

**Benchmarked on Windows 11, .NET 10.0.2, SSD storage (2026-02-26):**

| Operation | Throughput | Notes |
|-----------|-----------|-------|
| **Append (FlushImmediately = true, single event)** | ~58 events/sec | True durability: event + index files flushed (~17ms/event on SSD) |
| **Append (FlushImmediately = true, batch 10)** | ~78 events/sec | ~13ms/event when amortised over a batch |
| **Append (FlushImmediately = false)** | ~218 events/sec | OS page cache only (testing/dev mode — data loss risk on power failure) |
| **Tag query (high selectivity)** | ~553 μs | Index-based, excellent for targeted queries |
| **Tag query (1K events)** | ~10 ms | Sub-linear scaling |
| **Read by EventType (10K events)** | ~201 ms | Index-based |
| **Projection rebuild** | ~15,000 events/sec | Batched I/O (see rebuild note below) |
| **Incremental projection update** | ~11 μs | ~500x faster than full rebuild; zero allocation |

### Query Performance by Selectivity

| Selectivity | 10K Events | Performance |
|------------|-----------|-------------|
| **High** (few matches) | ~590 μs | ⭐ Excellent - tag index highly effective |
| **Medium** (moderate matches) | ~5.5 ms | ✅ Good - typical use case |
| **Low** (many matches) | ~111 ms | ⚠️ Expected - must deserialize many events |

### Optimization Tips

✅ **Use SSDs** - Flush operations are much faster (10ms vs 50ms+ on HDD)  
✅ **Use tag-based queries** - ~590μs for high selectivity vs ~5.5ms for broader queries  
✅ **Enable parallel projection rebuilding** - `MaxConcurrentRebuilds` config; note: after the rebuild I/O optimization, sequential and parallel complete in similar time (~370ms for 4 projections) — the disk bottleneck is gone  
✅ **Use incremental projection updates** - ~500x faster than full rebuild  
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

**Detailed benchmarks:** See `docs/benchmarking/results/20260226/`

> **Rebuild performance note:** The projection rebuild I/O optimisation reduced the 4-projection sequential rebuild from **5.5 s → 370 ms (~15×)** and memory from **85.9 MB → 21.5 MB (~4×)**. As a consequence, the parallel-over-sequential speedup collapsed to near-parity — the disk I/O bottleneck that made parallelism valuable was eliminated. See `CHANGELOG.md` for the full benchmark comparison.

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

## ⚠️ Known Limitations

### Crash-Recovery Position Collision (tracked for 0.5.0)

**Severity:** High — silent data loss possible on power failure during an append.

If the process crashes or loses power **after** event files are written to disk but **before**
the ledger is updated, orphaned event files remain at positions the ledger does not record.
On the next `AppendAsync`, those positions are reallocated and the orphaned files are
**silently overwritten**.

- `WriteProtectEventFiles = true` does **not** protect against this — the write path strips
  the read-only attribute before overwriting.
- `FlushEventsImmediately = true` ensures the original events survive on disk but does not
  prevent the overwrite on restart.

**Workaround:** After any unclean shutdown, check for event files at positions greater than
the value in `.ledger`. Full analysis and manual recovery steps:
[`docs/limitations/crash-recovery-position-collision.md`](docs/limitations/crash-recovery-position-collision.md).

**Fix target:** 0.5.0 (write-ahead log approach).

---

## ?? Documentation

- [DCB Specification](https://dcb.events/specification/) - Dynamic Consistency Boundaries pattern
- [Sample Application](Samples/Opossum.Samples.CourseManagement/) - Complete working example
- [API Documentation](docs/) - Detailed implementation docs

---

## ?? Contributing

Contributions are welcome! Please read [CONTRIBUTING.md](CONTRIBUTING.md) first.

---

## ?? License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

## ?? Acknowledgments

- Inspired by the [DCB Specification](https://dcb.events/)
- Built for real-world use cases in automotive retail and SMB applications

---

**Made with ?? for developers who value simplicity and local-first data ownership.**
