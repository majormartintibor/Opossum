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
- [Performance](#-performance)

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
    options.AddContext("MyApp");                     // Bounded context name
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

var events = await _eventStore.ReadAsync(query, ReadOption.None);

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

    public StudentDetails? Apply(StudentDetails? current, IEvent evt)
    {
        return evt switch
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

### Sequenced Events

When appended, events receive a **sequence position** (monotonic increasing number) and become `SequencedEvent`:

```csharp
public class SequencedEvent
{
    public long Position { get; set; }        // Global sequence number
    public Event Event { get; set; }          // Your domain event
    public EventMetadata Metadata { get; set; } // Timestamp, tags, etc.
}
```

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
    public CourseEnrollmentState? Apply(CourseEnrollmentState? current, IEvent evt)
    {
        return evt switch
        {
            StudentEnrolledToCourseEvent enrolled => 
                current with { EnrollmentCount = current.EnrollmentCount + 1 },
            _ => current
        };
    }
}
```

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

---

## ?? Configuration

### OpossumOptions

```csharp
builder.Services.AddOpossum(options =>
{
    // Root directory for event storage (REQUIRED)
    // Must be an absolute path
    options.RootPath = @"D:\MyApp\EventStore";

    // Bounded context name (REQUIRED)
    // ?? MVP: Only ONE context is currently supported
    options.AddContext("MyApplicationContext");

    // Flush events to disk immediately (OPTIONAL, default: true)
    // TRUE: Events are durable (survive power failure) but slower (~1-5ms per event)
    // FALSE: Faster but events may be lost on power failure (use for testing only)
    options.FlushEventsImmediately = true;
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
    "Contexts": ["MyApp"],
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

    // Contexts must be added programmatically
    var contexts = builder.Configuration.GetSection("Opossum:Contexts").Get<string[]>();
    if (contexts != null)
    {
        foreach (var context in contexts)
        {
            options.AddContext(context);
        }
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
    // Append one or more events
    Task AppendAsync(SequencedEvent events, AppendCondition? condition = null);
    Task AppendAsync(SequencedEvent[] events, AppendCondition? condition = null);

    // Read events matching a query
    Task<SequencedEvent[]> ReadAsync(Query query, ReadOption[]? readOptions = null);
}
```

### Extension Methods

```csharp
// Convert domain event to SequencedEvent
IEvent.ToDomainEvent()

// Add tags
SequencedEvent.WithTag(string key, string value)
SequencedEvent.WithTags(IEnumerable<Tag> tags)

// Set timestamp
SequencedEvent.WithTimestamp(DateTimeOffset timestamp)
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
    After = 42
}
```

### IProjectionStore

Query projections (read models):

```csharp
public interface IProjectionStore
{
    // Get single projection by key
    Task<TState?> GetAsync<TState>(string projectionName, string key) 
        where TState : class;

    // Query projections by tags
    Task<TState[]> QueryByTagsAsync<TState>(Tag[] tags) 
        where TState : class;

    // Rebuild all projections
    Task RebuildAllAsync();
}
```

---

## ?? Full Example

Here's a complete example showing student registration with email uniqueness validation:

```csharp
// 1. Define Events
public record StudentRegisteredEvent(
    Guid StudentId,
    string FirstName,
    string LastName,
    string Email) : IEvent;

// 2. Command Handler
public class RegisterStudentHandler
{
    private readonly IEventStore _eventStore;

    public async Task<Result> RegisterAsync(string firstName, string lastName, string email)
    {
        // Validate email is unique
        var emailQuery = Query.FromItems(new QueryItem
        {
            Tags = [new Tag { Key = "studentEmail", Value = email }]
        });

        var existing = await _eventStore.ReadAsync(emailQuery, ReadOption.None);
        if (existing.Length > 0)
        {
            return Result.Fail("Email already registered");
        }

        // Create event
        var studentId = Guid.NewGuid();
        var evt = new StudentRegisteredEvent(studentId, firstName, lastName, email)
            .ToDomainEvent()
            .WithTag("studentId", studentId.ToString())
            .WithTag("studentEmail", email)
            .WithTimestamp(DateTimeOffset.UtcNow);

        // Append with DCB to prevent race conditions
        try
        {
            await _eventStore.AppendAsync(
                evt,
                condition: new AppendCondition { FailIfEventsMatch = emailQuery });

            return Result.Success(studentId);
        }
        catch (ConcurrencyException)
        {
            return Result.Fail("Email was just registered by another request");
        }
    }
}

// 3. Projection
public record StudentListItem(Guid StudentId, string FullName, string Email);

[ProjectionDefinition("StudentList")]
public class StudentListProjection : IProjectionDefinition<StudentListItem>
{
    public string ProjectionName => "StudentList";
    public string[] EventTypes => [nameof(StudentRegisteredEvent)];

    public string KeySelector(SequencedEvent evt) =>
        evt.Event.Tags.First(t => t.Key == "studentId").Value;

    public StudentListItem? Apply(StudentListItem? current, IEvent evt)
    {
        if (evt is StudentRegisteredEvent registered)
        {
            return new StudentListItem(
                registered.StudentId,
                $"{registered.FirstName} {registered.LastName}",
                registered.Email);
        }
        return current;
    }
}

// 4. Query Projection
public class StudentController
{
    private readonly IProjectionStore _projectionStore;

    public async Task<StudentListItem?> GetStudentAsync(Guid studentId)
    {
        return await _projectionStore.GetAsync<StudentListItem>(
            "StudentList",
            studentId.ToString());
    }
}
```

---

## ? Performance

### Typical Throughput

| Operation | Throughput | Notes |
|-----------|-----------|-------|
| **Append (FlushImmediately = true)** | ~200-1000 events/sec | Limited by disk flush (SSD recommended) |
| **Append (FlushImmediately = false)** | ~5000-10000 events/sec | OS page cache only (testing mode) |
| **Read by tag** | ~10000-50000 events/sec | Index-based, very fast |
| **Read by position** | ~20000-100000 events/sec | Direct file access |
| **Projection rebuild** | ~5000-20000 events/sec | Depends on projection complexity |

### Optimization Tips

? **Use SSDs** - Flush operations are much faster  
? **Batch appends** - Append multiple events at once  
? **Index with tags** - Avoid full scans  
? **Keep projections simple** - Complex logic slows rebuilds  
? **Use `FlushEventsImmediately = false`** for testing only  

### Scalability Limits

Opossum is designed for **single-server deployments**:

| Metric | Recommended Limit | Notes |
|--------|------------------|-------|
| **Events per day** | < 100,000 | ~1 event/second average |
| **Total events** | < 10 million | Performance degrades with file count |
| **Projections** | < 100 types | More = slower startup |
| **Tags per event** | < 20 | Affects index write speed |

**Beyond these limits?** Consider cloud-based event stores (EventStoreDB, Azure Event Hubs).

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
