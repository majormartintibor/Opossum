# Fluent Event Builder API

The Opossum library provides a fluent API for building and appending events with minimal boilerplate.

## Table of Contents
1. [Basic Usage](#basic-usage)
2. [Fluent Builder API](#fluent-builder-api)
3. [Simple AppendEventAsync](#simple-appendeventasync)
4. [Batch Operations](#batch-operations)
5. [Comparison with Manual Approach](#comparison-with-manual-approach)

---

## Basic Usage

### Before (Manual Boilerplate)
```csharp
var sequencedEvent = new SequencedEvent
{
    Position = 0,
    Event = new DomainEvent
    {
        EventType = nameof(StudentRegisteredEvent),
        Event = new StudentRegisteredEvent(
                    studentId,
                    firstName,
                    lastName,
                    email),
        Tags =
        [
            new Tag { Key = "studentId", Value = studentId.ToString()},
            new Tag { Key = "studentEmail", Value = email }
        ]
    },
    Metadata = new Metadata
    {
        Timestamp = DateTimeOffset.UtcNow,
        CorrelationId = Guid.NewGuid()
    }
};

await eventStore.AppendAsync([sequencedEvent], condition);
```

### After (Fluent API)
```csharp
SequencedEvent sequencedEvent = new StudentRegisteredEvent(studentId, firstName, lastName, email)
    .ToDomainEvent()
    .WithTag("studentId", studentId.ToString())
    .WithTag("studentEmail", email);

await eventStore.AppendAsync(sequencedEvent, condition);
```

**Result: 70% reduction in boilerplate code!** 🎉

---

## Fluent Builder API

The `ToDomainEvent()` extension method on `IEvent` returns a `DomainEventBuilder` with the following fluent methods:

### Adding Tags

```csharp
// Single tag
var event = new StudentRegisteredEvent(...)
    .ToDomainEvent()
    .WithTag("studentId", "123")
    .WithTag("studentEmail", "alice@example.com");

// Multiple tags at once
var event = new StudentRegisteredEvent(...)
    .ToDomainEvent()
    .WithTags(
        new Tag { Key = "studentId", Value = "123" },
        new Tag { Key = "courseId", Value = "456" }
    );
```

### Setting Metadata

```csharp
// Custom correlation ID
var event = new StudentEnrolledEvent(...)
    .ToDomainEvent()
    .WithCorrelationId(correlationId)
    .WithTag("studentId", studentId.ToString());

// Full metadata control
var customMetadata = new Metadata
{
    Timestamp = DateTimeOffset.UtcNow,
    CorrelationId = correlationId,
    CausationId = causationId,
    UserId = currentUserId
};

var event = new StudentEnrolledEvent(...)
    .ToDomainEvent()
    .WithMetadata(customMetadata)
    .WithTag("studentId", studentId.ToString());

// Individual metadata properties
var event = new StudentEnrolledEvent(...)
    .ToDomainEvent()
    .WithCorrelationId(correlationId)
    .WithCausationId(causationId)
    .WithUserId(userId)
    .WithTimestamp(customTimestamp);
```

### Implicit Conversion

The builder supports implicit conversion to `SequencedEvent`, so you don't need to call `.Build()`:

```csharp
// No need for .Build() - implicit conversion!
SequencedEvent sequencedEvent = new StudentRegisteredEvent(...)
    .ToDomainEvent()
    .WithTag("studentId", "123");

await eventStore.AppendAsync(sequencedEvent);
```

---

## Simple AppendEventAsync

For even simpler scenarios, use `AppendEventAsync` to skip the builder entirely:

```csharp
// Minimal syntax - auto-generates metadata
await eventStore.AppendEventAsync(
    new StudentRegisteredEvent(studentId, firstName, lastName, email),
    tags: [
        new Tag { Key = "studentId", Value = studentId.ToString() },
        new Tag { Key = "studentEmail", Value = email }
    ]
);

// With custom metadata
await eventStore.AppendEventAsync(
    new StudentRegisteredEvent(...),
    tags: [...],
    metadata: customMetadata
);

// With append condition (DCB pattern)
await eventStore.AppendEventAsync(
    new StudentRegisteredEvent(...),
    tags: [...],
    condition: new AppendCondition { FailIfEventsMatch = validateEmailQuery }
);
```

---

## Batch Operations

Append multiple events at once with shared metadata and tags:

```csharp
var events = new IEvent[]
{
    new StudentRegisteredEvent(...),
    new CourseCreatedEvent(...),
    new InstructorAssignedEvent(...)
};

// All events get the same correlation ID and timestamp
await eventStore.AppendEventsAsync(
    events,
    tags: [new Tag { Key = "batch", Value = "import-2024" }],
    metadata: new Metadata
    {
        Timestamp = DateTimeOffset.UtcNow,
        CorrelationId = batchCorrelationId
    }
);
```

---

## Comparison with Manual Approach

| Feature | Manual | Fluent Builder | AppendEventAsync |
|---------|--------|----------------|------------------|
| **Boilerplate** | High | Low | Minimal |
| **Type Safety** | ✅ | ✅ | ✅ |
| **Custom Metadata** | ✅ | ✅ | ✅ |
| **Fluent Chaining** | ❌ | ✅ | ❌ |
| **Implicit Conversion** | ❌ | ✅ | N/A |
| **Best For** | Complex scenarios | Most use cases | Quick appends |

---

## Real-World Example: RegisterStudentCommandHandler

```csharp
public async Task<CommandResult> HandleAsync(
    RegisterStudentCommand command,
    IEventStore eventStore)
{
    // 1. Validate email uniqueness
    var validateEmailNotTakenQuery = Query.FromItems(
        new QueryItem
        {
            Tags = [new Tag { Key = "studentEmail", Value = command.Email }],
            EventTypes = []
        });

    var emailValidationResult = await eventStore.ReadAsync(validateEmailNotTakenQuery);

    if (emailValidationResult.Length != 0)
    {
        return new CommandResult(Success: false, "A user with this email already exists.");
    }

    // 2. Build event using fluent API
    SequencedEvent sequencedEvent = new StudentRegisteredEvent(
            command.StudentId,
            command.FirstName,
            command.LastName,
            command.Email)
        .ToDomainEvent()
        .WithTag("studentId", command.StudentId.ToString())
        .WithTag("studentEmail", command.Email);

    // 3. Append with DCB concurrency control
    await eventStore.AppendAsync(
        sequencedEvent,
        condition: new AppendCondition { FailIfEventsMatch = validateEmailNotTakenQuery });

    return new CommandResult(Success: true);
}
```

This pattern:
- ✅ **Enforces global uniqueness** (no duplicate emails) via DCB pattern
- ✅ **Minimal boilerplate** with fluent API
- ✅ **Type-safe** event construction
- ✅ **Race condition protection** through optimistic concurrency

---

## API Reference

### EventExtensions

```csharp
public static class EventExtensions
{
    // Converts IEvent to DomainEventBuilder
    public static DomainEventBuilder ToDomainEvent(this IEvent @event);
}
```

### DomainEventBuilder

```csharp
public class DomainEventBuilder
{
    // Tag management
    public DomainEventBuilder WithTag(string key, string value);
    public DomainEventBuilder WithTags(params Tag[] tags);
    
    // Metadata management
    public DomainEventBuilder WithMetadata(Metadata metadata);
    public DomainEventBuilder WithCorrelationId(Guid correlationId);
    public DomainEventBuilder WithCausationId(Guid causationId);
    public DomainEventBuilder WithOperationId(Guid operationId);
    public DomainEventBuilder WithUserId(Guid userId);
    public DomainEventBuilder WithTimestamp(DateTimeOffset timestamp);
    
    // Build final event
    public SequencedEvent Build();
    
    // Implicit conversion
    public static implicit operator SequencedEvent(DomainEventBuilder builder);
}
```

### EventStoreExtensions

```csharp
public static class EventStoreExtensions
{
    // Simple single event append
    public static Task AppendEventAsync(
        this IEventStore eventStore,
        IEvent @event,
        IEnumerable<Tag>? tags = null,
        Metadata? metadata = null,
        AppendCondition? condition = null);
    
    // Batch event append with shared metadata
    public static Task AppendEventsAsync(
        this IEventStore eventStore,
        IEvent[] events,
        IEnumerable<Tag>? tags = null,
        Metadata? metadata = null,
        AppendCondition? condition = null);
}
```

---

## Best Practices

1. **Use Fluent Builder** for most scenarios - it's clean, type-safe, and flexible
2. **Use AppendEventAsync** for quick one-liners without complex metadata needs
3. **Use AppendEventsAsync** for batch operations with shared correlation IDs
4. **Always tag events** for queryability - especially for uniqueness constraints
5. **Leverage implicit conversion** - no need to call `.Build()` explicitly
6. **Chain fluent methods** for better readability

---

## Summary

The fluent event builder API dramatically reduces boilerplate while maintaining type safety and flexibility. It integrates seamlessly with the DCB pattern for optimistic concurrency control and makes event sourcing with Opossum a joy to use! 🚀
