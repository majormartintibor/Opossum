# Quick Start

Build your first event-sourced feature with Opossum in 5 minutes.

## What We'll Build

A minimal student registration system that:
1. Defines domain events
2. Appends events to the store
3. Reads events back
4. Maintains a projection (read model)
5. Enforces a business rule with DCB concurrency control

---

## Step 1 — Install and Configure

```bash
dotnet add package Opossum
```

```csharp
using Opossum.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpossum(options =>
{
    options.RootPath = @"D:\MyData\EventStore";
    options.UseStore("QuickStart");
});

builder.Services.AddProjections(options =>
{
    options.ScanAssembly(typeof(Program).Assembly);
});

var app = builder.Build();
app.Run();
```

---

## Step 2 — Define Your Events

Events are immutable records implementing `IEvent`:

```csharp
using Opossum;

public record StudentRegisteredEvent(
    Guid StudentId,
    string Name,
    string Email) : IEvent;

public record StudentEnrolledToCourseEvent(
    Guid StudentId,
    Guid CourseId) : IEvent;
```

---

## Step 3 — Append Events

Inject `IEventStore` and append events using `NewEvent` + `DomainEvent`:

```csharp
using Opossum;
using Opossum.Core;
using Opossum.Extensions;

public class StudentService(IEventStore eventStore)
{
    public async Task<Guid> RegisterAsync(string name, string email)
    {
        var studentId = Guid.NewGuid();

        var evt = new StudentRegisteredEvent(studentId, name, email)
            .ToDomainEvent()
            .WithTag("studentId", studentId.ToString())
            .WithTimestamp(DateTimeOffset.UtcNow);

        // DomainEventBuilder implicitly converts to NewEvent
        await eventStore.AppendAsync([evt], condition: null);

        return studentId;
    }
}
```

> `ToDomainEvent()` and `WithTag()` are extension methods from `Opossum.Extensions`.

---

## Step 4 — Read Events

```csharp
using Opossum.Core;

// Read all events for a specific student
var query = Query.FromItems(new QueryItem
{
    Tags = [new Tag("studentId", studentId.ToString())]
});

var events = await eventStore.ReadAsync(query, readOptions: null);

foreach (var e in events)
{
    Console.WriteLine($"[{e.Position}] {e.Event.EventType}");
}
```

---

## Step 5 — Create a Projection

Projections are materialized views maintained automatically by `IProjectionManager`:

```csharp
using Opossum.Core;
using Opossum.Projections;

public record StudentView(Guid StudentId, string Name, string Email, int EnrolledCourses);

[ProjectionDefinition("StudentView")]
public class StudentViewProjection : IProjectionDefinition<StudentView>
{
    public string ProjectionName => "StudentView";

    public string[] EventTypes =>
    [
        nameof(StudentRegisteredEvent),
        nameof(StudentEnrolledToCourseEvent)
    ];

    public string KeySelector(SequencedEvent evt) =>
        evt.Event.Tags.First(t => t.Key == "studentId").Value;

    public StudentView? Apply(StudentView? current, SequencedEvent evt) =>
        evt.Event.Event switch
        {
            StudentRegisteredEvent r => new StudentView(r.StudentId, r.Name, r.Email, 0),
            StudentEnrolledToCourseEvent when current is not null =>
                current with { EnrolledCourses = current.EnrolledCourses + 1 },
            _ => current
        };
}
```

Query the projection via `IProjectionStore<T>`:

```csharp
using Opossum.Projections;

public class StudentController(IProjectionStore<StudentView> store)
{
    public async Task<StudentView?> GetStudentAsync(Guid studentId) =>
        await store.GetAsync(studentId.ToString());
}
```

---

## Step 6 — Enforce a Business Rule with DCB

Use `AppendCondition` to prevent duplicate registrations — the DCB read → decide → append pattern:

```csharp
public async Task<Guid> RegisterUniqueAsync(string email)
{
    // 1. READ — find any existing registration for this email
    var query = Query.FromItems(new QueryItem
    {
        EventTypes = [nameof(StudentRegisteredEvent)],
        Tags = [new Tag("studentEmail", email)]
    });

    var existing = await eventStore.ReadAsync(query, readOptions: null);

    // 2. DECIDE — enforce the "no duplicate email" invariant
    if (existing.Length > 0)
        throw new InvalidOperationException($"Email {email} is already registered.");

    var highestPosition = existing.Length > 0 ? existing[^1].Position : (long?)null;

    // 3. APPEND — with a guard: fail if a conflicting event appeared since our read
    var studentId = Guid.NewGuid();
    var condition = new AppendCondition
    {
        FailIfEventsMatch = query,
        AfterSequencePosition = highestPosition
    };

    var evt = new StudentRegisteredEvent(studentId, "New Student", email)
        .ToDomainEvent()
        .WithTag("studentId", studentId.ToString())
        .WithTag("studentEmail", email);

    try
    {
        // DomainEventBuilder implicitly converts to NewEvent
        await eventStore.AppendAsync([evt], condition);
    }
    catch (AppendConditionFailedException)
    {
        // A concurrent writer registered the same email — retry or surface error
        throw new InvalidOperationException("Concurrent registration detected. Please retry.");
    }

    return studentId;
}
```

---

## What Happens on Disk

After running the above, your event store directory looks like:

```
D:\MyData\EventStore\
  QuickStart\
    events\
      position_1.json
      position_2.json
    index\
      event-types\
        StudentRegisteredEvent.idx
      tags\
        studentId_<guid>.idx
    ledger.json
    projections\
      StudentView\
        <student-guid>.json
```

Every event is a plain JSON file. Projections are JSON files too. No binary formats, no proprietary encodings — you can inspect everything with any text editor.

---

## Next Steps

→ [Configuration](configuration.md) — tune flush, auto-rebuild, polling interval  
→ [Concepts: Event Store](../concepts/event-store.md) — understand the storage model  
→ [Concepts: DCB](../concepts/dcb.md) — deep dive on the specification  
→ [Concepts: Projections](../concepts/projections.md) — advanced projection patterns  
→ [Use Cases](../guides/use-cases.md) — see Opossum in real-world scenarios
