# Implementation Plan - Ready to Build Now

This document identifies components that can be implemented **immediately** based on existing specifications, without dependencies on incomplete work.

---

## ✅ Category 1: Configuration & Setup (High Priority - No Dependencies)

### 1.1 OpossumOptions Implementation
**File**: `src\Opossum\Configuration\OpossumOptions.cs`  
**Status**: Empty stub  
**Effort**: 30 minutes  
**Dependencies**: None

**What to implement**:
```csharp
public sealed class OpossumOptions
{
    public string RootPath { get; set; } = "OpossumStore";
    public List<string> Contexts { get; } = new();
    
    public OpossumOptions AddContext(string contextName)
    {
        if (string.IsNullOrWhiteSpace(contextName))
            throw new ArgumentException("Context name cannot be empty", nameof(contextName));
        
        if (!IsValidDirectoryName(contextName))
            throw new ArgumentException($"Invalid context name: {contextName}", nameof(contextName));
        
        if (Contexts.Contains(contextName))
            throw new InvalidOperationException($"Context '{contextName}' already added");
        
        Contexts.Add(contextName);
        return this;
    }
    
    private static bool IsValidDirectoryName(string name)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        return !string.IsNullOrWhiteSpace(name) && !name.Any(c => invalidChars.Contains(c));
    }
}
```

**Specification Reference**: `Specification\InitialSpecification.MD` lines 26-35

---

### 1.2 ServiceCollectionExtensions Implementation
**File**: `src\Opossum\DependencyInjection\ServiceCollectionExtensions.cs`  
**Status**: Returns services without doing anything  
**Effort**: 1 hour  
**Dependencies**: OpossumOptions, StorageInitializer (to be created)

**What to implement**:
```csharp
public static IServiceCollection AddOpossum(
    this IServiceCollection services,
    Action<OpossumOptions>? configure = null,
    bool enableProjectionDaemon = true)
{
    var options = new OpossumOptions();
    configure?.Invoke(options);
    
    // Validate options
    if (options.Contexts.Count == 0)
    {
        throw new InvalidOperationException(
            "At least one context must be configured. Use options.AddContext(\"ContextName\")");
    }
    
    // Register options
    services.AddSingleton(options);
    
    // Initialize storage structure
    var initializer = new StorageInitializer(options);
    initializer.Initialize();
    
    // Register event store
    services.AddSingleton<IEventStore, FileSystemEventStore>();
    
    return services;
}
```

**Specification Reference**: `Specification\InitialSpecification.MD` lines 23-39

---

### 1.3 StorageInitializer (New Class)
**File**: `src\Opossum\Storage\FileSystem\StorageInitializer.cs` (to create)  
**Status**: Doesn't exist  
**Effort**: 1 hour  
**Dependencies**: OpossumOptions

**What to implement**:
```csharp
internal sealed class StorageInitializer
{
    private readonly OpossumOptions _options;
    
    public StorageInitializer(OpossumOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }
    
    public void Initialize()
    {
        // Create root directory
        Directory.CreateDirectory(_options.RootPath);
        
        // Create structure for each context
        foreach (var context in _options.Contexts)
        {
            var contextPath = Path.Combine(_options.RootPath, context);
            Directory.CreateDirectory(contextPath);
            
            // Create .ledger file
            var ledgerPath = Path.Combine(contextPath, ".ledger");
            if (!File.Exists(ledgerPath))
            {
                File.WriteAllText(ledgerPath, string.Empty);
            }
            
            // Create Events directory
            var eventsPath = Path.Combine(contextPath, "Events");
            Directory.CreateDirectory(eventsPath);
            
            // Create Indices directory structure
            var indicesPath = Path.Combine(contextPath, "Indices");
            Directory.CreateDirectory(indicesPath);
            
            var eventTypeIndexPath = Path.Combine(indicesPath, "EventType");
            Directory.CreateDirectory(eventTypeIndexPath);
            
            var tagsIndexPath = Path.Combine(indicesPath, "Tags");
            Directory.CreateDirectory(tagsIndexPath);
        }
    }
}
```

**Folder Structure Reference**: `Specification\InitialSpecification.MD` lines 9-21

---

## ✅ Category 2: Error Handling & Exceptions (Low Effort - No Dependencies)

### 2.1 Custom Exception Classes
**File**: `src\Opossum\Exceptions\EventStoreExceptions.cs` (to create)  
**Status**: Doesn't exist  
**Effort**: 30 minutes  
**Dependencies**: None

**What to implement**:
```csharp
namespace Opossum.Exceptions;

public class EventStoreException : Exception
{
    public EventStoreException() { }
    public EventStoreException(string message) : base(message) { }
    public EventStoreException(string message, Exception innerException) 
        : base(message, innerException) { }
}

public class AppendConditionFailedException : EventStoreException
{
    public Query FailedQuery { get; }
    
    public AppendConditionFailedException(Query query, string message) 
        : base(message)
    {
        FailedQuery = query;
    }
}

public class EventNotFoundException : EventStoreException
{
    public Guid EventId { get; }
    
    public EventNotFoundException(Guid eventId) 
        : base($"Event with ID '{eventId}' not found")
    {
        EventId = eventId;
    }
}

public class InvalidQueryException : EventStoreException
{
    public InvalidQueryException(string message) : base(message) { }
}

public class ConcurrencyException : EventStoreException
{
    public ConcurrencyException(string message) : base(message) { }
}

public class ContextNotFoundException : EventStoreException
{
    public string ContextName { get; }
    
    public ContextNotFoundException(string contextName) 
        : base($"Context '{contextName}' not found")
    {
        ContextName = contextName;
    }
}
```

**Reference**: `Documentation\solution-review.md` - Error Handling section

---

## ✅ Category 3: ReadOption Enum Enhancement (Low Effort)

### 3.1 Expand ReadOption Enum
**File**: `src\Opossum\Core\ReadOption.cs`  
**Status**: Only has "None"  
**Effort**: 15 minutes  
**Dependencies**: None

**What to implement**:
```csharp
namespace Opossum.Core;

/// <summary>
/// Options for reading events from the event store
/// </summary>
public enum ReadOption
{
    /// <summary>
    /// No special options
    /// </summary>
    None = 0,
    
    /// <summary>
    /// Read events in descending order (newest first)
    /// </summary>
    Descending = 1,
    
    /// <summary>
    /// Include only the first N events
    /// </summary>
    Limit = 2,
    
    /// <summary>
    /// Skip the first N events
    /// </summary>
    Skip = 4,
    
    /// <summary>
    /// Start reading from a specific sequence position
    /// </summary>
    FromPosition = 8
}

/// <summary>
/// Configuration for ReadOptions
/// </summary>
public class ReadOptionConfig
{
    public int? LimitCount { get; set; }
    public int? SkipCount { get; set; }
    public long? FromPosition { get; set; }
    public bool Descending { get; set; }
}
```

**DCB Reference**: `Specification\DCB-Specification.md` lines 17-20

---

## ⚠️ Category 4: Domain Sample Models - MANUAL DEVELOPMENT ONLY

> **🚫 AI IMPLEMENTATION RESTRICTED**  
> All items in this category are part of `Opossum.Samples.CourseManagement` and **must be written manually** without AI code generation. This ensures the full developer experience of using the Opossum library.
>
> - ✅ AI may be asked questions about patterns and best practices
> - ✅ AI may explain how to use Opossum features  
> - ❌ AI may NOT generate code directly into sample project files
> - ❌ AI may NOT create/modify files in `Samples\Opossum.Samples.CourseManagement\`
>
> **The code examples below are for REFERENCE ONLY to guide manual development.**

### 4.1 Course Management Events ⚠️ MANUAL ONLY
**File**: `Samples\Opossum.Samples.CourseManagement\Domain\Events.cs` (to create)  
**Status**: Doesn't exist - **Developer must create manually**  
**Effort**: 30 minutes (manual development)  
**Dependencies**: IEvent interface (exists)

**Reference example** (DO NOT auto-generate):
```csharp
namespace Opossum.Samples.CourseManagement.Domain;

public record StudentEnlistedToCourseEvent(
    Guid CourseId, 
    Guid StudentId, 
    DateTimeOffset EnrolledAt) : IEvent;

public record StudentWithdrawnFromCourseEvent(
    Guid CourseId, 
    Guid StudentId, 
    DateTimeOffset WithdrawnAt,
    string? Reason) : IEvent;

public record CourseReachedCapacityEvent(
    Guid CourseId,
    DateTimeOffset ReachedAt) : IEvent;

public record CourseCapacityIncreasedEvent(
    Guid CourseId,
    int PreviousCapacity,
    int NewCapacity,
    DateTimeOffset ChangedAt) : IEvent;

public record CourseCreatedEvent(
    Guid CourseId,
    string CourseName,
    string CourseCode,
    int MaxCapacity,
    DateTimeOffset CreatedAt) : IEvent;
```

**Specification Reference**: `Specification\InitialSpecification.MD` lines 126-143

---

### 4.2 Course Management Aggregate ⚠️ MANUAL ONLY
**File**: `Samples\Opossum.Samples.CourseManagement\Domain\CourseEnlistmentAggregate.cs` (to create)  
**Status**: Doesn't exist - **Developer must create manually**  
**Effort**: 45 minutes (manual development)  
**Dependencies**: Event classes (above)

**Reference example** (DO NOT auto-generate):
```csharp
namespace Opossum.Samples.CourseManagement.Domain;

public record CourseEnlistmentAggregate
{
    public Guid CourseId { get; init; }
    public string CourseName { get; init; } = string.Empty;
    public string CourseCode { get; init; } = string.Empty;
    public HashSet<Guid> EnlistedStudents { get; init; } = new();
    public int MaxCapacity { get; init; } = 100;
    public long LastKnownSequencePosition { get; init; } = 0;
    public bool IsAtCapacity => EnlistedStudents.Count >= MaxCapacity;
    
    public int CurrentEnlistedStudentCount => EnlistedStudents.Count;
    
    public bool IsStudentEnlisted(Guid studentId) => EnlistedStudents.Contains(studentId);
    
    // Apply methods for event sourcing
    public CourseEnlistmentAggregate Apply(StudentEnlistedToCourseEvent @event)
    {
        return this with
        {
            CourseId = @event.CourseId,
            EnlistedStudents = new HashSet<Guid>(EnlistedStudents) { @event.StudentId }
        };
    }
    
    public CourseEnlistmentAggregate Apply(StudentWithdrawnFromCourseEvent @event)
    {
        var students = new HashSet<Guid>(EnlistedStudents);
        students.Remove(@event.StudentId);
        
        return this with
        {
            EnlistedStudents = students
        };
    }
    
    public CourseEnlistmentAggregate Apply(CourseCapacityIncreasedEvent @event)
    {
        return this with
        {
            MaxCapacity = @event.NewCapacity
        };
    }
    
    public CourseEnlistmentAggregate Apply(CourseCreatedEvent @event)
    {
        return this with
        {
            CourseId = @event.CourseId,
            CourseName = @event.CourseName,
            CourseCode = @event.CourseCode,
            MaxCapacity = @event.MaxCapacity
        };
    }
    
    public CourseEnlistmentAggregate Apply(CourseReachedCapacityEvent @event)
    {
        // No state change, just an event marker
        return this;
    }
}
```

**Specification Reference**: `Specification\InitialSpecification.MD` lines 172-184

---

### 4.3 Commands and Queries ⚠️ MANUAL ONLY
**File**: `Samples\Opossum.Samples.CourseManagement\Domain\Commands.cs` (to create)  
**Status**: Partially exists in test file - **Developer must complete manually**  
**Effort**: 20 minutes (manual development)  
**Dependencies**: None

**Reference example** (DO NOT auto-generate):
```csharp
namespace Opossum.Samples.CourseManagement.Domain;

// Commands
public record EnlistStudentToCourseCommand(Guid CourseId, Guid StudentId);

public record WithdrawStudentFromCourseCommand(Guid CourseId, Guid StudentId, string? Reason);

public record IncreaseCourseCapacityCommand(Guid CourseId, int NewCapacity);

public record CreateCourseCommand(string CourseName, string CourseCode, int MaxCapacity);

// Command Results
public record CommandResult(bool Success, string? Message = null, Guid? EntityId = null);

// Queries (for building aggregates)
public static class CourseQueries
{
    public static Query ForCourse(Guid courseId)
    {
        return new Query
        {
            QueryItems =
            [
                new QueryItem
                {
                    Tags = [new Tag { Key = "CourseId", Value = courseId.ToString() }]
                }
            ]
        };
    }
    
    public static Query ForStudent(Guid studentId)
    {
        return new Query
        {
            QueryItems =
            [
                new QueryItem
                {
                    Tags = [new Tag { Key = "StudentId", Value = studentId.ToString() }]
                }
            ]
        };
    }
    
    public static Query ForCourseAndStudent(Guid courseId, Guid studentId)
    {
        return new Query
        {
            QueryItems =
            [
                new QueryItem
                {
                    Tags = 
                    [
                        new Tag { Key = "CourseId", Value = courseId.ToString() },
                        new Tag { Key = "StudentId", Value = studentId.ToString() }
                    ]
                }
            ]
        };
    }
}
```

**Specification Reference**: `Specification\InitialSpecification.MD` lines 95-102

---

### 4.4 Command Handlers ⚠️ MANUAL ONLY
**File**: `Samples\Opossum.Samples.CourseManagement\Domain\Handlers\EnlistStudentToCourseHandler.cs` (to create)  
**Status**: Doesn't exist - **Developer must create manually**  
**Effort**: 30 minutes (manual development)  
**Dependencies**: Events, Aggregate, Commands

**Reference example** (DO NOT auto-generate):
```csharp
namespace Opossum.Samples.CourseManagement.Domain.Handlers;

public static class EnlistStudentToCourseHandler
{
    public static List<DomainEvent> Handle(
        CourseEnlistmentAggregate aggregate, 
        EnlistStudentToCourseCommand command)
    {
        var events = new List<DomainEvent>();
        
        // Validation
        if (aggregate.IsStudentEnlisted(command.StudentId))
        {
            throw new InvalidOperationException(
                $"Student {command.StudentId} is already enlisted in course {command.CourseId}");
        }
        
        if (aggregate.IsAtCapacity)
        {
            throw new InvalidOperationException(
                $"Course {command.CourseId} has reached its maximum capacity of {aggregate.MaxCapacity}");
        }
        
        // Create enrollment event
        events.Add(new DomainEvent
        {
            EventType = nameof(StudentEnlistedToCourseEvent),
            Event = new StudentEnlistedToCourseEvent(
                command.CourseId,
                command.StudentId,
                DateTimeOffset.UtcNow),
            Tags = new List<Tag>
            {
                new() { Key = "CourseId", Value = command.CourseId.ToString() },
                new() { Key = "StudentId", Value = command.StudentId.ToString() }
            }
        });
        
        // Check if this enrollment fills the course
        if (aggregate.CurrentEnlistedStudentCount + 1 == aggregate.MaxCapacity)
        {
            events.Add(new DomainEvent
            {
                EventType = nameof(CourseReachedCapacityEvent),
                Event = new CourseReachedCapacityEvent(
                    command.CourseId,
                    DateTimeOffset.UtcNow),
                Tags = new List<Tag>
                {
                    new() { Key = "CourseId", Value = command.CourseId.ToString() }
                }
            });
        }
        
        return events;
    }
}
```

**Specification Reference**: `Specification\InitialSpecification.MD` lines 145-169

---

## ✅ Category 5: Helper Extensions (Can Implement Partially)

### 5.1 EventStore Extensions (without implementation)
**File**: `src\Opossum\Extensions\EventStoreExtensions.cs` (to create)  
**Status**: Doesn't exist  
**Effort**: 1 hour  
**Dependencies**: None (interface-based)

**What to implement**:
```csharp
namespace Opossum.Extensions;

public static class EventStoreExtensions
{
    /// <summary>
    /// Loads and reconstructs an aggregate from events
    /// </summary>
    public static async Task<T> LoadAggregateAsync<T>(
        this IEventStore eventStore,
        Query query,
        CancellationToken cancellationToken = default) where T : new()
    {
        var events = await eventStore.ReadAsync(query, null);
        
        var aggregate = new T();
        var aggregateType = typeof(T);
        
        foreach (var sequencedEvent in events.OrderBy(e => e.Position))
        {
            var eventType = sequencedEvent.Event.Event.GetType();
            
            // Find Apply method for this event type
            var applyMethod = aggregateType.GetMethod(
                "Apply",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { eventType },
                null);
            
            if (applyMethod != null)
            {
                var result = applyMethod.Invoke(aggregate, new[] { sequencedEvent.Event.Event });
                if (result is T typedResult)
                {
                    aggregate = typedResult;
                }
            }
        }
        
        return aggregate;
    }
    
    /// <summary>
    /// Appends a single event to the store
    /// </summary>
    public static Task AppendAsync(
        this IEventStore eventStore,
        SequencedEvent @event,
        AppendCondition? condition = null,
        CancellationToken cancellationToken = default)
    {
        return eventStore.AppendAsync(new[] { @event }, condition);
    }
    
    /// <summary>
    /// Appends domain events to the store
    /// </summary>
    public static Task AppendEventsAsync(
        this IEventStore eventStore,
        IEnumerable<DomainEvent> events,
        AppendCondition? condition = null,
        CancellationToken cancellationToken = default)
    {
        var sequencedEvents = events.Select(e => new SequencedEvent
        {
            Event = e,
            Position = 0, // Will be assigned by store
            Metadata = new Metadata
            {
                Timestamp = DateTimeOffset.UtcNow
            }
        }).ToArray();
        
        return eventStore.AppendAsync(sequencedEvents, condition);
    }
}
```

**Specification Reference**: `Specification\InitialSpecification.MD` lines 186-198

---

## ✅ Category 6: Test Infrastructure Updates

### 6.1 Update OpossumFixture
**File**: `tests\Opossum.IntegrationTests\OpossumFixture.cs`  
**Status**: Has TODOs  
**Effort**: 30 minutes  
**Dependencies**: OpossumOptions, AddOpossum (once implemented)

**What to implement**:
```csharp
namespace Opossum.IntegrationTests;

public class OpossumFixture : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string _testStoragePath;
    
    public IMediator Mediator { get; }
    public IEventStore EventStore { get; }
    
    public OpossumFixture()
    {
        // Create unique temp directory for this test run
        _testStoragePath = Path.Combine(
            Path.GetTempPath(), 
            "OpossumTests", 
            Guid.NewGuid().ToString());
        
        var services = new ServiceCollection();
        
        // Configure Opossum with test storage path
        services.AddOpossum(options =>
        {
            options.RootPath = _testStoragePath;
            options.AddContext("CourseManagement");
            options.AddContext("TestContext");
        });
        
        // Add mediator
        services.AddMediator();
        
        // Add logging for tests
        services.AddLogging(builder => 
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        
        _serviceProvider = services.BuildServiceProvider();
        
        Mediator = _serviceProvider.GetRequiredService<IMediator>();
        EventStore = _serviceProvider.GetRequiredService<IEventStore>();
    }
    
    public void Dispose()
    {
        _serviceProvider?.Dispose();
        
        // Clean up test storage
        if (Directory.Exists(_testStoragePath))
        {
            try
            {
                Directory.Delete(_testStoragePath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }
}
```

---

### 6.2 Update ExampleTest
**File**: `tests\Opossum.IntegrationTests\ExampleTest.cs`  
**Status**: Has TODOs and incomplete logic  
**Effort**: 30 minutes  
**Dependencies**: OpossumFixture, domain models

**What to implement**:
```csharp
namespace Opossum.IntegrationTests;

public class ExampleTest(OpossumFixture fixture) : IClassFixture<OpossumFixture>
{
    private readonly IMediator _mediator = fixture.Mediator;
    private readonly IEventStore _eventStore = fixture.EventStore;

    [Fact]
    public async Task EnlistStudentToCourse_Success()
    {
        // Arrange
        var studentId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        
        // Create course first
        var createCourseEvents = new[]
        {
            new SequencedEvent
            {
                Event = new DomainEvent
                {
                    EventType = "CourseCreatedEvent",
                    Event = new CourseCreatedEvent(
                        courseId,
                        "Introduction to Computer Science",
                        "CS101",
                        100,
                        DateTimeOffset.UtcNow),
                    Tags =
                    [
                        new Tag { Key = "CourseId", Value = courseId.ToString() }
                    ]
                },
                Metadata = new Metadata { Timestamp = DateTimeOffset.UtcNow }
            }
        };
        
        await _eventStore.AppendAsync(createCourseEvents, null);
        
        // Build query for course events
        var courseQuery = new Query
        {
            QueryItems =
            [
                new QueryItem
                {
                    Tags = [new Tag { Key = "CourseId", Value = courseId.ToString() }]
                }
            ]
        };
        
        // Act - Enlist student
        var enrollmentEvents = new[]
        {
            new SequencedEvent
            {
                Event = new DomainEvent
                {
                    EventType = "StudentEnlistedToCourseEvent",
                    Event = new StudentEnlistedToCourseEvent(
                        courseId,
                        studentId,
                        DateTimeOffset.UtcNow),
                    Tags =
                    [
                        new Tag { Key = "CourseId", Value = courseId.ToString() },
                        new Tag { Key = "StudentId", Value = studentId.ToString() }
                    ]
                },
                Metadata = new Metadata { Timestamp = DateTimeOffset.UtcNow }
            }
        };
        
        await _eventStore.AppendAsync(enrollmentEvents, null);
        
        // Assert - Read events back
        var events = await _eventStore.ReadAsync(courseQuery, null);
        Assert.NotEmpty(events);
        Assert.Equal(2, events.Length); // Course created + student enlisted
        
        // Verify event types
        Assert.Contains(events, e => e.Event.EventType == "CourseCreatedEvent");
        Assert.Contains(events, e => e.Event.EventType == "StudentEnlistedToCourseEvent");
        
        // Build aggregate and verify state
        var aggregate = await _eventStore.LoadAggregateAsync<CourseEnlistmentAggregate>(courseQuery);
        Assert.Equal(courseId, aggregate.CourseId);
        Assert.Contains(studentId, aggregate.EnlistedStudents);
        Assert.Equal(1, aggregate.CurrentEnlistedStudentCount);
    }
}

// Test domain models (can be in separate file)
public record CourseCreatedEvent(
    Guid CourseId,
    string CourseName,
    string CourseCode,
    int MaxCapacity,
    DateTimeOffset CreatedAt) : IEvent;

public record StudentEnlistedToCourseEvent(
    Guid CourseId,
    Guid StudentId,
    DateTimeOffset EnrolledAt) : IEvent;
```

---

## 📋 Implementation Priority Summary

### Can Implement **RIGHT NOW** (No Blockers):

1. **OpossumOptions** ✅ (30 min)
2. **Custom Exceptions** ✅ (30 min)
3. **ReadOption Enum** ✅ (15 min)
4. **Domain Events** ✅ (30 min)
5. **Domain Aggregate** ✅ (45 min)
6. **Commands & Queries** ✅ (20 min)
7. **Command Handlers** ✅ (30 min)
8. **EventStore Extensions** ✅ (1 hour)

**Total Effort**: ~4 hours of independent work

---

### Can Implement **AFTER Config** (Small Dependencies):

9. **StorageInitializer** (needs OpossumOptions) - 1 hour
10. **ServiceCollectionExtensions** (needs OpossumOptions + StorageInitializer) - 1 hour
11. **OpossumFixture** (needs ServiceCollectionExtensions) - 30 min
12. **ExampleTest** (needs fixture) - 30 min

**Total Additional Effort**: ~3 hours

---

### **CANNOT Implement Yet** (Blocked):

- ❌ **FileSystemEventStore** - Core implementation requires:
  - JSON serialization strategy
  - Concurrency design decisions
  - Index management algorithm
  - Complex append/read logic
  
- ❌ **Sample Web API** - Needs FileSystemEventStore working

- ❌ **Source Generation** - Future feature

---

## 🎯 Recommended Implementation Order

### Phase 1: Foundation (4 hours)
1. OpossumOptions
2. Custom Exceptions  
3. ReadOption Enum
4. EventStore Extensions

### Phase 2: Domain Layer (2.5 hours)
5. Domain Events
6. Domain Aggregate
7. Commands & Queries
8. Command Handlers

### Phase 3: Configuration (3 hours)
9. StorageInitializer
10. ServiceCollectionExtensions
11. OpossumFixture
12. Update ExampleTest

### Phase 4: Core Event Store (8-12 hours)
13. FileSystemEventStore implementation
14. JSON serialization
15. Index management
16. Query execution

**Total to Working MVP**: ~17-21 hours

---

## 📊 Current Completeness After Implementation

| Component | Current | After Phase 1-3 | After Phase 4 |
|-----------|---------|-----------------|---------------|
| Mediator | 100% | 100% | 100% |
| Configuration | 5% | 90% | 90% |
| Domain Models | 0% | 95% | 95% |
| Event Store Interface | 90% | 95% | 100% |
| Event Store Implementation | 0% | 0% | 85% |
| Tests | 30% | 70% | 90% |
| **Overall** | **30%** | **60%** | **90%** |

---

## ✅ Action Items

**START IMMEDIATELY**:
- [ ] Implement OpossumOptions (blocking others)
- [ ] Create custom exception classes
- [ ] Enhance ReadOption enum
- [ ] Create domain event classes

**AFTER OPOSSUMOPTIONS**:
- [ ] Implement StorageInitializer
- [ ] Complete ServiceCollectionExtensions
- [ ] Update test fixtures

**CONCURRENT WORK**:
- [ ] Domain aggregate implementation
- [ ] Command/query definitions
- [ ] Command handler logic
- [ ] Extension method utilities

---

This plan provides **~7-9 hours of work that can START TODAY** with zero dependencies!
