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
using Opossum.Projections;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOpossum(options =>
    {
        options.RootPath = @"D:\MyData\EventStore";
        options.UseStore("QuickStart");
    })
    .AddProjections(options =>
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

public sealed record StudentRegisteredEvent(
    Guid StudentId,
    string FirstName,
    string LastName,
    string Email) : IEvent;

public sealed record StudentEnrolledToCourseEvent(
    Guid CourseId,
    Guid StudentId) : IEvent;
```

---

## Step 3 — Append Events

Inject `IEventStore` and append events using the fluent builder from `Opossum.Extensions`:

```csharp
using Opossum;
using Opossum.Core;
using Opossum.Extensions;

public class StudentService(IEventStore eventStore)
{
    public async Task<Guid> RegisterAsync(
        string firstName, string lastName, string email)
    {
        var studentId = Guid.NewGuid();

        // Fluent builder — implicitly converts to NewEvent
        NewEvent evt = new StudentRegisteredEvent(
                studentId, firstName, lastName, email)
            .ToDomainEvent()
            .WithTag("studentId", studentId.ToString())
            .WithTag("studentEmail", email)
            .WithTimestamp(DateTimeOffset.UtcNow);

        // Single-event convenience extension (from Opossum.Extensions)
        await eventStore.AppendAsync(evt, condition: null);

        return studentId;
    }
}
```

> `ToDomainEvent()` and `WithTag()` are extension methods from `Opossum.Extensions`. The `DomainEventBuilder` has an implicit conversion to `NewEvent`.

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

public sealed record StudentView(
    Guid StudentId,
    string FirstName,
    string LastName,
    string Email,
    int EnrolledCourses);

[ProjectionDefinition("StudentView")]
public sealed class StudentViewProjection : IProjectionDefinition<StudentView>
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
            StudentRegisteredEvent r => new StudentView(
                r.StudentId, r.FirstName, r.LastName, r.Email, 0),
            StudentEnrolledToCourseEvent when current is not null =>
                current with { EnrolledCourses = current.EnrolledCourses + 1 },
            _ => current
        };
}
```

Query the projection via `IProjectionStore<T>`:

```csharp
using Opossum.Projections;

// Inject IProjectionStore<T> via DI — one registration per projection type
var student = await projectionStore.GetAsync(studentId.ToString());

// Or query all with a predicate
var enrolled = await projectionStore.QueryAsync(
    s => s.EnrolledCourses > 0);
```

---

## Step 6 — Enforce a Business Rule with DCB

Use `AppendCondition` to prevent duplicate registrations — the DCB read → decide → append pattern:

```csharp
public async Task<CommandResult> RegisterUniqueAsync(
    RegisterStudentCommand command, IEventStore eventStore)
{
    // 1. READ — find any existing registration for this email
    var emailQuery = Query.FromItems(new QueryItem
    {
        Tags = [new Tag("studentEmail", command.Email)]
    });

    var existing = await eventStore.ReadAsync(emailQuery, ReadOption.None);

    // 2. DECIDE — enforce the "no duplicate email" invariant
    if (existing.Length != 0)
        return CommandResult.Fail("A user with this email already exists.");

    // 3. APPEND — with a guard: fail if a conflicting event appeared since our read
    NewEvent newEvent = new StudentRegisteredEvent(
            command.StudentId,
            command.FirstName,
            command.LastName,
            command.Email)
        .ToDomainEvent()
        .WithTag("studentId", command.StudentId.ToString())
        .WithTag("studentEmail", command.Email)
        .WithTimestamp(DateTimeOffset.UtcNow);

    // The AppendCondition guards against concurrent writes:
    // If any event with the same email tag appeared between our read and this
    // append, the event store throws AppendConditionFailedException.
    await eventStore.AppendAsync(
        newEvent,
        condition: new AppendCondition { FailIfEventsMatch = emailQuery });

    return CommandResult.Ok();
}
```

For more complex scenarios with multiple business rules, use `BuildDecisionModelAsync` which composes multiple projections and returns a combined `AppendCondition` automatically. See the [Mediator](../concepts/mediator.md) article for a full example.

---

## What Happens on Disk

After running the above, your event store directory looks like:

```
D:\MyData\EventStore\
  QuickStart\
    .ledger
    Events\
      0000000001.json
      0000000002.json
    Indices\
      EventType\
        StudentRegisteredEvent.idx
      Tags\
        studentId_<guid>.idx
    Projections\
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
