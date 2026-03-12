# Mediator Pattern in Opossum

Opossum ships a lightweight, convention-based mediator that dispatches commands and queries to handlers discovered automatically at startup.

---

## Why a Mediator?

The mediator pattern decouples the **sender** of a request from its **handler**. In a CQRS/event-sourcing context this means:

- API endpoints send a command/query object — they don't know which class handles it.
- Handlers are discovered by reflection at startup — no manual wiring.
- Business logic stays in focused, testable handler classes rather than leaking into endpoints.

---

## Setup

```csharp
using Opossum.DependencyInjection;
using Opossum.Mediator;

// In Program.cs
builder.Services
    .AddOpossum(options => { /* ... */ })
    .AddMediator();
```

`AddMediator()` scans the calling assembly for handler classes and registers them automatically.

To scan additional assemblies:

```csharp
builder.Services.AddMediator(options =>
{
    options.Assemblies.Add(typeof(MyHandler).Assembly);
});
```

---

## Defining a Handler

A handler is a plain class — no interface implementation is required. The mediator discovers it by **convention**:

1. The class name ends with `Handler`, **or** it is decorated with `[MessageHandler]`.
2. It exposes a public method named `HandleAsync`, `Handle`, `ConsumeAsync`, or `Consume`.
3. The **first parameter** of that method is the message type (the command or query).
4. Additional parameters are resolved from the DI container at invocation time.

### Command Handler Example

From the Course Management sample app — registering a student:

```csharp
using Opossum.Core;
using Opossum.Extensions;

// Command message
public sealed record RegisterStudentCommand(
    Guid StudentId, string FirstName, string LastName, string Email);

// Handler — discovered by naming convention (ends with "Handler")
public sealed class RegisterStudentCommandHandler()
{
    public async Task<CommandResult> HandleAsync(
        RegisterStudentCommand command,
        IEventStore eventStore)            // resolved from DI
    {
        // Build the event using the fluent builder
        NewEvent newEvent = new StudentRegisteredEvent(
                command.StudentId,
                command.FirstName,
                command.LastName,
                command.Email)
            .ToDomainEvent()
            .WithTag("studentId", command.StudentId.ToString())
            .WithTag("studentEmail", command.Email)
            .WithTimestamp(DateTimeOffset.UtcNow);

        // Append with DCB concurrency guard (unique email)
        var emailQuery = Query.FromItems(new QueryItem
        {
            Tags = [new Tag("studentEmail", command.Email)]
        });

        await eventStore.AppendAsync(
            newEvent,
            condition: new AppendCondition { FailIfEventsMatch = emailQuery });

        return CommandResult.Ok();
    }
}
```

### Command Handler with Decision Model

For commands that need to enforce multiple business rules, use `BuildDecisionModelAsync` and `ExecuteDecisionAsync`:

```csharp
using Opossum.Core;
using Opossum.DecisionModel;
using Opossum.Exceptions;
using Opossum.Extensions;

public sealed record EnrollStudentToCourseCommand(Guid CourseId, Guid StudentId);

public sealed class EnrollStudentToCourseCommandHandler()
{
    public async Task<CommandResult> HandleAsync(
        EnrollStudentToCourseCommand command,
        IEventStore eventStore)
    {
        try
        {
            return await eventStore.ExecuteDecisionAsync(
                (store, ct) => TryEnrollStudentAsync(command, store));
        }
        catch (AppendConditionFailedException)
        {
            return CommandResult.Fail(
                "Failed to enroll student due to concurrent updates. Please try again.");
        }
    }

    private static async Task<CommandResult> TryEnrollStudentAsync(
        EnrollStudentToCourseCommand command,
        IEventStore eventStore)
    {
        // Build decision model from three independent projections in a single read
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

        await eventStore.AppendAsync(enrollmentEvent, appendCondition);

        return CommandResult.Ok();
    }
}
```

---

## Dispatching via `IMediator`

Inject `IMediator` and call `InvokeAsync<TResponse>`:

```csharp
using Opossum.Core;
using Opossum.Mediator;

// Minimal API endpoint
app.MapPost("/students", async (
    [FromBody] RegisterStudentRequest request,
    [FromServices] IMediator mediator) =>
{
    var studentId = Guid.NewGuid();

    var command = new RegisterStudentCommand(
        StudentId: studentId,
        FirstName: request.FirstName,
        LastName: request.LastName,
        Email: request.Email);

    var commandResult = await mediator.InvokeAsync<CommandResult>(command);

    if (!commandResult.Success)
        return Results.BadRequest(commandResult.ErrorMessage);

    return Results.Created($"/students/{studentId}", new { id = studentId });
});
```

The mediator resolves the correct handler, creates an instance via `ActivatorUtilities.CreateInstance`, injects method parameters from DI, executes the method, and returns the result.

---

## Query Handlers

The same convention works for queries. Query handlers typically inject `IProjectionStore<T>` to read materialized views:

```csharp
using Opossum.Core;
using Opossum.Projections;

public sealed record GetCourseShortInfoCommand(Guid CourseId);

public sealed class GetCourseShortInfoCommandHandler()
{
    public async Task<CommandResult<CourseShortInfo>> HandleAsync(
        GetCourseShortInfoCommand command,
        IProjectionStore<CourseShortInfo> projectionStore)
    {
        var course = await projectionStore.GetAsync(command.CourseId.ToString());

        return course is null
            ? CommandResult<CourseShortInfo>.Fail("Course not found.")
            : CommandResult<CourseShortInfo>.Ok(course);
    }
}
```

```csharp
// Dispatch from a minimal API endpoint
var result = await mediator.InvokeAsync<CommandResult<CourseShortInfo>>(
    new GetCourseShortInfoCommand(courseId));
```

---

## Handler Discovery Rules

- The handler class name **ends with `Handler`**, or it is decorated with `[MessageHandler]`. Either condition is sufficient.
- The handler must expose a public method named `HandleAsync`, `Handle`, `ConsumeAsync`, or `Consume`.
- The first parameter of that method is the **message type** (command or query).
- Additional parameters are **resolved from DI** at invocation time — this includes `IEventStore`, `IProjectionStore<T>`, `CancellationToken`, and any other registered service.
- Exactly **one handler per message type** is supported — a duplicate throws at startup.
- Handlers are discovered by reflection at startup and stored in the mediator's internal handler map. They are created per invocation via `ActivatorUtilities.CreateInstance`.

---

## Timeout Support

`InvokeAsync` accepts an optional `timeout` parameter:

```csharp
var result = await mediator.InvokeAsync<CommandResult>(
    command,
    cancellationToken: cts.Token,
    timeout: TimeSpan.FromSeconds(5));
```

If the handler does not complete within the timeout, an `OperationCanceledException` is thrown.

---

## API Reference

See the generated API docs for full details:

- [`IMediator`](../../api/Opossum.Mediator.IMediator.yml)
- [`IMessageHandler`](../../api/Opossum.Mediator.IMessageHandler.yml)
- [`MessageHandlerAttribute`](../../api/Opossum.Mediator.MessageHandlerAttribute.yml)
- [`MediatorServiceExtensions`](../../api/Opossum.Mediator.MediatorServiceExtensions.yml)
