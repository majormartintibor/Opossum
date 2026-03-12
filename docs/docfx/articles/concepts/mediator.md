# Mediator Pattern in Opossum

Opossum ships a lightweight mediator that dispatches commands and queries to registered handlers, discovered automatically via dependency injection.

---

## Why a Mediator?

The mediator pattern decouples the **sender** of a request from its **handler**. In a CQRS/event-sourcing context this means:

- Controllers and API endpoints send a command/query object — they don't know which class handles it.
- Handlers are registered in DI and discovered at startup — no manual wiring.
- Business logic stays in focused, testable handler classes rather than leaking into controllers.

---

## Setup

```csharp
using Opossum.Mediator;

// In Program.cs / Startup
builder.Services.AddMediator();
```

`AddMediator()` scans the entry assembly for all classes decorated with `[MessageHandler]` and registers them automatically.

To scan additional assemblies:

```csharp
builder.Services.AddMediator(options =>
{
    options.ScanAssembly(typeof(MyHandler).Assembly);
});
```

---

## Defining a Handler

Implement `IMessageHandler<TMessage, TResponse>` and decorate with `[MessageHandler]`:

```csharp
using Opossum.Mediator;

// Command / request message
public record RegisterStudentCommand(string Name, string Email);

// Handler
[MessageHandler]
public class RegisterStudentHandler(IEventStore eventStore)
    : IMessageHandler<RegisterStudentCommand, Guid>
{
    public async Task<Guid> HandleAsync(
        RegisterStudentCommand command,
        CancellationToken cancellationToken)
    {
        var studentId = Guid.NewGuid();

        var evt = new StudentRegisteredEvent(studentId, command.Name, command.Email)
            .ToDomainEvent()
            .WithTag("studentId", studentId.ToString())
            .WithTag("studentEmail", command.Email);

        await eventStore.AppendAsync(
            [new NewEvent { Event = evt }],
            condition: null,
            cancellationToken);

        return studentId;
    }
}
```

---

## Dispatching via `IMediator`

Inject `IMediator` and call `InvokeAsync<TResponse>`:

```csharp
public class StudentController(IMediator mediator)
{
    [HttpPost]
    public async Task<IActionResult> Register([FromBody] RegisterStudentRequest request)
    {
        var studentId = await mediator.InvokeAsync<Guid>(
            new RegisterStudentCommand(request.Name, request.Email));

        return Ok(studentId);
    }
}
```

The mediator resolves the correct handler from DI, executes it, and returns the result.

---

## Query Handlers

The same pattern works for queries:

```csharp
public record GetStudentQuery(Guid StudentId);

[MessageHandler]
public class GetStudentHandler(IProjectionStore store)
    : IMessageHandler<GetStudentQuery, StudentView?>
{
    public async Task<StudentView?> HandleAsync(
        GetStudentQuery query,
        CancellationToken cancellationToken) =>
        await store.GetAsync<StudentView>("StudentView", query.StudentId.ToString());
}
```

```csharp
// Usage
var student = await mediator.InvokeAsync<StudentView?>(new GetStudentQuery(studentId));
```

---

## Handler Discovery Rules

- The handler class **must** be decorated with `[MessageHandler]`.
- The handler class **must** implement `IMessageHandler<TMessage, TResponse>`.
- Exactly **one handler per message type** is supported.
- Handlers are registered as **transient** services.

---

## Timeout Support

`InvokeAsync` accepts an optional `timeout` parameter:

```csharp
var result = await mediator.InvokeAsync<Guid>(
    command,
    cancellationToken: cts.Token,
    timeout: TimeSpan.FromSeconds(5));
```

If the handler does not complete within the timeout, an `OperationCanceledException` is thrown.

---

## API Reference

See the generated API docs for full details:

- [`IMediator`](../../api/Opossum.Mediator.IMediator.yml)
- [`IMessageHandler<TMessage, TResponse>`](../../api/Opossum.Mediator.IMessageHandler.yml)
- [`MessageHandlerAttribute`](../../api/Opossum.Mediator.MessageHandlerAttribute.yml)
- [`MediatorServiceExtensions`](../../api/Opossum.Mediator.MediatorServiceExtensions.yml)
