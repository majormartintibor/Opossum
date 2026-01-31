# API Design Patterns - Opossum Sample Application

This document defines the consistent API design patterns used in the Opossum Sample Application.

---

## 🎯 Core Principle

**We use resource-oriented URLs with behavioral endpoints, aligned with event sourcing and CQRS principles.**

---

## 📐 URL Pattern

### Standard Format:
```
{HTTP-VERB} /{aggregate-type}/{aggregate-id}/{behavior-or-aspect}
```

### Rationale:
1. **Aggregate Identity**: URL explicitly shows which aggregate instance handles the command
2. **HTTP Semantics**: Proper use of HTTP verbs for behavioral operations
3. **Authorization**: Aggregate ID immediately available for resource-level permissions
4. **Observability**: Resource ID visible in access logs without body inspection
5. **CQRS Alignment**: URL structure mirrors internal command routing to aggregates

---

## 🔧 HTTP Verbs

| Verb | Usage | Example |
|------|-------|---------|
| **POST** | Creating new aggregates or relationships | `POST /students` (create student) |
| **POST** | Triggering side effects or complex behaviors | `POST /courses/{id}/enrollment` (enroll student) |
| **PATCH** | Modifying aspects of existing aggregates | `PATCH /courses/{id}/student-limit` |
| **GET** | Querying projections or read models | `GET /students` (list students) |
| **DELETE** | Soft-deletes or deactivations | `DELETE /students/{id}` (deactivate) |

### When to use PATCH vs POST:
- **PATCH**: Updating a specific aspect of an aggregate (subscription, limit, status)
- **POST**: Creating relationships or triggering behavior with side effects (enrollment, registration)

---

## ✅ Examples from Sample App

### Commands (Write Operations)

#### Creating Aggregates
```csharp
// POST - Creating new student
POST /students
Body: { "firstName": "John", "lastName": "Doe", "email": "john@example.com" }

// POST - Creating new course
POST /courses
Body: { "name": "Mathematics", "description": "...", "maxStudentCount": 30 }
```

#### Modifying Aggregate Aspects
```csharp
// PATCH - Updating student's subscription tier
PATCH /students/{studentId}/subscription
Body: { "enrollmentTier": "Premium" }

// PATCH - Modifying course student limit
PATCH /courses/{courseId}/student-limit
Body: { "newMaxStudentCount": 50 }
```

#### Creating Relationships / Complex Behaviors
```csharp
// POST - Enrolling student in course (creates relationship)
POST /courses/{courseId}/enrollment
Body: { "studentId": "..." }
```

### Queries (Read Operations)

```csharp
// GET - List all students with projection
GET /students
Response: [{ "studentId": "...", "firstName": "...", ... }]

// GET - Get specific course details
GET /courses/{courseId}
Response: { "courseId": "...", "name": "...", "currentEnrollment": 15, ... }
```

---

## 📝 Implementation Pattern

### Command Endpoints (PATCH/POST)

```csharp
public sealed record RequestType(/* data excluding aggregate ID */);
public sealed record CommandType(Guid AggregateId, /* other properties */);
public sealed record EventType(Guid AggregateId, /* other properties */) : IEvent;

public static class Endpoint
{
    public static void Map{Feature}Endpoint(this IEndpointRouteBuilder app)
    {
        app.Map{Verb}("/{resource}/{id}/{aspect}", async (
            Guid id,                          // ✅ Aggregate ID from URL
            [FromBody] RequestType request,   // ✅ Command data from body
            [FromServices] IMediator mediator) =>
        {
            var command = new CommandType(
                AggregateId: id,              // ✅ Use URL parameter
                ...request.Properties);       // ✅ Spread request properties
            
            var result = await mediator.InvokeAsync<CommandResult>(command);
            return result.Success ? Results.Ok() : Results.BadRequest(result.ErrorMessage);
        })
        .WithName("{UniqueName}")
        .WithTags("Commands");  // or "Queries"
    }
}
```

### Handler Pattern

```csharp
public sealed class {Feature}CommandHandler()
{
    public async Task<CommandResult> HandleAsync(
        {Feature}Command command,
        IEventStore eventStore)
    {
        // 1. Validate invariants (e.g., aggregate exists)
        var existsQuery = Query.FromItems(
            new QueryItem
            {
                Tags = [new Tag { Key = "{aggregateType}Id", Value = command.AggregateId.ToString() }],
                EventTypes = [nameof({AggregateCreatedEvent})]
            });

        var events = await eventStore.ReadAsync(existsQuery, ReadOption.None);
        if (events.Length == 0)
        {
            return CommandResult.Fail($"{AggregateType} with ID {command.AggregateId} does not exist.");
        }

        // 2. Business validation
        if (/* business rule violated */)
        {
            return CommandResult.Fail("Business rule violation message");
        }

        // 3. Create and append event
        SequencedEvent sequencedEvent = new {Feature}Event(
            AggregateId: command.AggregateId,
            ...command.Properties)
            .ToDomainEvent()
            .WithTag("{aggregateType}Id", command.AggregateId.ToString())
            .WithTimestamp(DateTimeOffset.UtcNow);

        await eventStore.AppendAsync(sequencedEvent);

        return CommandResult.Ok();
    }
}
```

---

## 🚫 Anti-Patterns (Avoid)

### ❌ ID in Request Body
```csharp
// DON'T DO THIS
POST /students/subscription
Body: { "studentId": "...", "enrollmentTier": "..." }  // ❌ ID hidden in body
```

**Why avoid:**
- ID not visible in logs
- Harder to implement authorization
- Doesn't match internal aggregate routing
- Poor HTTP semantics

### ❌ Generic Action Endpoints
```csharp
// DON'T DO THIS
POST /commands
Body: { "type": "UpdateSubscription", "studentId": "...", ... }  // ❌ RPC-style
```

**Why avoid:**
- Loses RESTful benefits
- Harder to monitor and debug
- Doesn't leverage HTTP semantics
- Poor discoverability

---

## 🎯 Consistency Checklist

When implementing a new endpoint, verify:

- [ ] Aggregate ID is in the URL path, not the request body
- [ ] HTTP verb matches the operation type (POST for creation/side effects, PATCH for updates)
- [ ] Request body only contains data (not identity)
- [ ] Command receives ID from URL parameter
- [ ] Event is tagged with aggregate ID
- [ ] Endpoint name is unique (for Swagger)
- [ ] Tags are consistent ("Commands" or "Queries")

---

## 📚 References

### Industry Standards
- **Greg Young (CQRS/ES)**: Commands target aggregate instances by ID
- **RESTful API Design**: Resource-oriented URLs with HTTP semantics
- **EventStore**: `/streams/{streamId}/events` pattern

### Within Opossum
- Event streams are keyed by aggregate ID (via tags)
- Commands route to specific aggregate instances
- Projections query by aggregate ID

### Benefits for Event Sourcing
1. ✅ URL pattern matches event stream structure
2. ✅ Aggregate ID prominent for tagging
3. ✅ Clear command routing
4. ✅ Natural authorization boundaries
5. ✅ Better observability

---

## 🔄 Evolution

This pattern may evolve as the application grows. Update this document when:
- Adding new aggregate types
- Introducing API versioning
- Implementing new behavioral patterns
- Discovering better practices

**Last Updated**: Current Session (Pattern A standardization)
