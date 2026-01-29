# File System based Event Store

We are building a NuGet package called Opossum that implements an event store using the file system for storage. This document outlines the initial specification for the file system based event store.
It will also contain a basic mediator implementation to handle commands and queries.
We will consider SourceGenerator to generate boilerplate code for handling events, commands, and queries.

## Folder Structure

/ApplicationName
  /Context1
    .ledger
    /Events        
        e6ad7aad-6001-4743-a37a-dccf6fd5d31f.json
        496a5e58-a058-4df4-bbac-4c38b06ff576.json
    /Indices
        /EventType
            StudentAddedToCourse.json
            CourseMaxCapacityChanged.json
            ...
        /Tags
            StudentId:29e5091e-b021-4ee6-8827-b979384a50ba.json
            CourseId:8f4c3e2d-3c5b-4f1e-9f7d-2a5f6e8c9b12.json
            CourseType:Mathematics.json
            StudentType:FullTime.json
            ...
  /Context2
    .ledger
    /Events    
 ...

 The folder structure on disk should be initialized at startup if it does not exist yet.
 Example: we are building a university management system called "UniManage" with multiple contexts such as "CourseManagement", "StudentEnrollment", and "Billing".
 Configuration:
```csharp
builder.Services.AddOpossum(options =>
{
    options.AddContext("CourseManagement");
    options.AddContext("StudentEnrollment");
    options.AddContext("Billing");
    ...
});
```
The sample code above will create the following folder structure on disk:
/UniManage
    /CourseManagement
        .ledger
        /Events        
        /Indices
            /EventType
            /Tags
    /StudentEnrollment
        .ledger
        /Events        
        /Indices
            /EventType
            /Tags
    /Billing
        .ledger
        /Events        
        /Indices
            /EventType
            /Tags

## Event Storage
Each event is stored as a separate JSON file within the `/Events` directory of its respective context. The filename is a unique identifier (UUID) for the event, ensuring no collisions.
The .ledger file maintains a sequential log of all event IDs, preserving the order of events as they were added.
Think of Contexts as distinct modules or bounded contexts within your application, each with its own isolated event store. As if you would have separate schemas within a database for different parts of your application.

## Indices

### Event Type Indices
An index file for an Event Type contains a JSON array of event IDs corresponding to that type.
E.G.: StudentAddedToCourse.json contains:
```json
[
    "e6ad7aad-6001-4743-a37a-dccf6fd5d31f",
    "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    ...
]
```
### Tag Indices
An index file for a Tag contains a JSON array of event IDs associated with that tag.
E.G.: StudentId:29e5091e-b021-4ee6-8827-b979384a50ba.json contains:
```json
[
    "e6ad7aad-6001-4743-a37a-dccf6fd5d31f",
    "f1e2d3c4-b5a6-7890-abcd-ef0987654321",
    ...
]
```
Meaning the events with these IDs are tagged with StudentId:29e5091e-b021-4ee6-8827-b979384a50ba.

## Snapshots and Projections
Are not part of this initial specification and will be defined later.

## Querying
See the DCB-Sepcifiaction for querying capabilities and check the existing files in Opossum.Core.
We will define a Query for each incomming request to retrieve events based on Event Types, Tags, and time ranges.

## Pseudo Code Examples
```csharp
app.MapPost("/course/{id:guid}/enlist", async (
    Guid id,
    [FromBody] EnlistStudentToCourseRequest request,
    [FromServices] IBus bus) =>
{
    var command = new EnlistStudentToCourseCommand(courseId: id, studentId: request.StudentId);
    var commandResult = await bus.InvokeAsync<Opossum.Core.CommandResult>(command);
    
    return Results.NoContent();
})
.WithName("EnlistStudentToCourse")
.WithTags("Commands");

example1:
[UsesQuery(EnlistStudentToCourseQuery)]
public record EnlistStudentToCourseCommand(Guid courseId, Guid studentId);

example2:
public record EnlistStudentToCourseCommand<EnlistStudentToCourseQuery>(Guid courseId, Guid studentId);

// This is ideally source generated
public class DispatchEnlistStudentToCourseCommand
{
    private readonly IEventStore _eventStore;
    public DispatchEnlistStudentToCourseCommand(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }
    
    public async Task<CommandResult> HandleAsync(EnlistStudentToCourseCommand command, CancellationToken cancellationToken = default)
    {
        var aggregate = await _eventStore.LoadAggregateAsync<CourseEnlistmentAggregate>(command.Query);

        var events = handler.Handle(aggregate, command);

        var result = await _eventStore.AppendAsync(events, new AppendCondtion(command.Query, aggregate.LastKnownSequencePosition));
    }
}

//this is written by the developer. This is the handler.Handle(aggregate, command);
public static class EnlistStudentToCourseCommandHandler
{
    public static IEnumerable<IEvent> Handle(CourseEnlistmentAggregate aggregate, EnlistStudentToCourseCommand command)
    {
        var events = new List<IEvent>();

        if (aggregate.IsStudentEnlisted(command.studentId))
        {
            throw new InvalidOperationException("Student is already enlisted to the course.");
        }
        if (aggregate.EnlistedStudentCount >= aggregate.MaxCapacity)
        {
            throw new InvalidOperationException("Course has reached its maximum capacity.");
        }

        // Business logic to create events
        events.Add(new StudentEnlistedToCourseEvent(command.courseId, command.studentId, DateTime.UtcNow));

        if (aggregate.EnlistedStudentCount + 1 = aggregate.MaxCapacity)
        {
            events.Add(new CourseReachedCapacityLimitEvent(command.courseId));
        }

        return events;
    }
}

```

Missing for pseudo code, but important to mention:

### Command to Query Translation
We need to translet from Command to Query. This can be done via SourceGenerator.
The EnlistStudentToCourseCommand contains the studentId and courseId. 
We need to map that to EnlistStudentToCourseQuery that will fetch all events for the given courseId and studentId from the event store.
We will have tags like CourseId:8f4c3e2d-3c5b-4f1e-9f7d-2a5f6e8c9b12 and StudentId:29e5091e-b021-4ee6-8827-b979384a50ba to facilitate this querying.

### Aggregate reconstruction

```csharp
public record CourseEnlistmentAggregate
{
    //properties like EnlistedStudents, MaxCapacity, LastKnownSequencePosition, etc.
    public int CurrentEnlistedStudentCount => EnlistedStudents.Count;

    //Apply methods for each event type
    public void Apply(StudentEnlistedToCourseEvent @event)
    {
        EnlistedStudents.Add(@event.StudentId);
        LastKnownSequencePosition = @event.SequencePosition;
        
    }
}
```

In the pseudo code examples the line
```csharp
var aggregate = await _eventStore.LoadAggregateAsync<CourseEnlistmentAggregate>(command.Query);
```
The LoadAggregateAsync method will:
1. Use the provided EnlistStudentToCourseQuery to fetch all relevant events from the event store.
2. Reconstruct the CourseEnlistmentAggregate by applying each fetched event in sequence.
3. Return the fully reconstructed aggregate to the command handler for further processing.

## Challenges and Considerations
Checkpointting and tracking of sequence positions to ensure idempotency and consistency.
We need to figure out how to properly work with sequence positions.

## Code Generation
As seen in the pseudo code examples, we will need code generation to reduce boilerplate code.
We might need to consider using Attributes to annotate either the Command or CommandHandlers so that the SourceGenerator can generate the necessary code for translating Commands to Queries and for dispatching commands to their handlers.

## Command vs Query
Commands represent actions that change state, while Queries retrieve data without modifying state.
Functionally there is no real difference yet as both will use a Query class that the event store will sue to retrieve events
and reconstruct aggregates (via the apply methods). Despite this, we want to keep the distinction between Commands and Queries for clarity and future extensibility.
Eventually Queries may return snapshots or projections instead of live rebuilding aggregates.
