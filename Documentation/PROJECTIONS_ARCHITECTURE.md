# Opossum Projections Architecture

## Overview

Projections are materialized views of event-sourced data that provide optimized read models for query scenarios. This architecture introduces a projection system that:

1. **Maintains consistency** with the event store
2. **Optimizes read performance** by pre-computing aggregates
3. **Supports multiple projection strategies** (in-memory, file-based, database)
4. **Scales horizontally** with background processing
5. **Integrates seamlessly** with existing Opossum infrastructure

## Core Concepts

### 1. Projection Definition
A projection is defined by:
- **State Type**: The materialized view model (DTO/POCO)
- **Event Types**: Which events the projection subscribes to
- **Apply Logic**: How events transform the state (`TState Apply(TState current, IEvent event)`)
- **Key Selector**: How to identify projection instances (e.g., `courseId`, `studentId`)
- **Storage Strategy**: Where/how to persist the projection

### 2. Projection Manager
Coordinates projection lifecycle:
- **Registration**: Discovers and registers projection definitions
- **Rebuilding**: Full rebuild from event history
- **Incremental Updates**: Real-time updates as new events arrive
- **Checkpointing**: Tracks last processed event position
- **Storage**: Persists projection state

### 3. Projection Daemon (Background Service)
- **Polls** event store for new events since last checkpoint
- **Applies** events to registered projections
- **Updates** checkpoints after successful processing
- **Handles errors** with retry logic and dead-letter tracking

---

## Architecture Components

```
┌─────────────────────────────────────────────────────────────┐
│                    Application Layer                        │
│  ├─ Query Handlers (GetCoursesShortInfo, etc.)             │
│  └─ Read from IProjectionStore<TState>                     │
└─────────────────────────────────────────────────────────────┘
                              ▲
                              │ Queries
                              │
┌─────────────────────────────┼─────────────────────────────────┐
│        Projection Layer     │                                 │
│                             │                                 │
│  ┌──────────────────────────┴───────────────────────────┐   │
│  │         IProjectionStore<TState>                     │   │
│  │  - GetAsync(key)                                     │   │
│  │  - GetAllAsync()                                     │   │
│  │  - QueryAsync(predicate)                             │   │
│  └──────────────────────────────────────────────────────┘   │
│                             ▲                                 │
│                             │                                 │
│  ┌──────────────────────────┴───────────────────────────┐   │
│  │         ProjectionManager                            │   │
│  │  - RegisterProjection<TState>(definition)            │   │
│  │  - RebuildAsync(projectionName)                      │   │
│  │  - UpdateAsync(events)                               │   │
│  │  - GetCheckpoint(projectionName)                     │   │
│  │  - SaveCheckpoint(projectionName, position)          │   │
│  └──────────────────────────────────────────────────────┘   │
│                             ▲                                 │
│                             │                                 │
│  ┌──────────────────────────┴───────────────────────────┐   │
│  │      ProjectionDaemon (IHostedService)               │   │
│  │  - Polls event store every N seconds                 │   │
│  │  - Reads events since last checkpoint                │   │
│  │  - Applies events to projections                     │   │
│  │  - Updates checkpoints                               │   │
│  └──────────────────────────────────────────────────────┘   │
│                             │                                 │
└─────────────────────────────┼─────────────────────────────────┘
                              │ Reads Events
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    Event Store Layer                        │
│  └─ IEventStore.ReadAsync(query, options)                  │
└─────────────────────────────────────────────────────────────┘
```

---

## File System Structure

```
OpossumStore/
├── CourseManagement/              # Context
│   ├── Events/                    # Event files (existing)
│   ├── Ledger/                    # Ledger files (existing)
│   ├── Indices/                   # Index files (existing)
│   └── Projections/               # NEW: Projection storage
│       ├── _checkpoints/          # Checkpoint tracking per projection
│       │   ├── CourseShortInfo.checkpoint
│       │   └── StudentShortInfo.checkpoint
│       ├── CourseShortInfo/       # Projection instance files
│       │   ├── {courseId-1}.json
│       │   ├── {courseId-2}.json
│       │   └── ...
│       └── StudentShortInfo/
│           ├── {studentId-1}.json
│           ├── {studentId-2}.json
│           └── ...
```

### Checkpoint Format
```json
{
  "projectionName": "CourseShortInfo",
  "lastProcessedPosition": 15432,
  "lastUpdated": "2024-01-15T10:30:00Z",
  "totalEventsProcessed": 15432
}
```

---

## Key Interfaces

### IProjectionDefinition
```csharp
public interface IProjectionDefinition<TState> where TState : class
{
    string ProjectionName { get; }
    string[] EventTypes { get; }
    string KeySelector(SequencedEvent evt);
    TState? Apply(TState? current, IEvent evt);
}
```

### IProjectionStore
```csharp
public interface IProjectionStore<TState> where TState : class
{
    Task<TState?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TState>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TState>> QueryAsync(Func<TState, bool> predicate, CancellationToken cancellationToken = default);
    Task SaveAsync(string key, TState state, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
```

### IProjectionManager
```csharp
public interface IProjectionManager
{
    void RegisterProjection<TState>(IProjectionDefinition<TState> definition) where TState : class;
    Task RebuildAsync(string projectionName, CancellationToken cancellationToken = default);
    Task UpdateAsync(SequencedEvent[] events, CancellationToken cancellationToken = default);
    Task<long> GetCheckpointAsync(string projectionName, CancellationToken cancellationToken = default);
    Task SaveCheckpointAsync(string projectionName, long position, CancellationToken cancellationToken = default);
}
```

---

## Registration & Discovery

### 1. Attribute-Based Registration
```csharp
[ProjectionDefinition("CourseShortInfo")]
public sealed class CourseShortInfoProjection : IProjectionDefinition<CourseShortInfo>
{
    public string ProjectionName => "CourseShortInfo";
    
    public string[] EventTypes => new[]
    {
        nameof(CourseCreatedEvent),
        nameof(CourseStudentLimitModifiedEvent),
        nameof(StudentEnrolledToCourseEvent)
    };
    
    public string KeySelector(SequencedEvent evt)
    {
        var courseIdTag = evt.Event.Tags.FirstOrDefault(t => t.Key == "courseId");
        return courseIdTag?.Value ?? throw new InvalidOperationException("Missing courseId tag");
    }
    
    public CourseShortInfo? Apply(CourseShortInfo? current, IEvent evt)
    {
        return evt switch
        {
            CourseCreatedEvent created => new CourseShortInfo(
                CourseId: created.CourseId,
                Name: created.Name,
                MaxStudentCount: created.MaxStudentCount,
                CurrentEnrollmentCount: 0),
            
            CourseStudentLimitModifiedEvent limitModified when current != null =>
                current with { MaxStudentCount = limitModified.NewMaxStudentCount },
            
            StudentEnrolledToCourseEvent enrolled when current != null =>
                current with { CurrentEnrollmentCount = current.CurrentEnrollmentCount + 1 },
            
            _ => current
        };
    }
}
```

### 2. Service Registration
```csharp
services.AddOpossum(options =>
{
    options.AddContext("CourseManagement");
}, enableProjections: true);

// Auto-discovers projection definitions in assemblies
services.AddProjections(options =>
{
    options.ScanAssembly(typeof(CourseShortInfoProjection).Assembly);
    options.PollingInterval = TimeSpan.FromSeconds(5);
    options.BatchSize = 1000; // Events per batch
});
```

---

## Implementation Strategy

### Phase 1: Core Projection Infrastructure
1. **Create projection interfaces** (`IProjectionDefinition`, `IProjectionStore`, `IProjectionManager`)
2. **Implement FileSystemProjectionStore** (JSON-based storage per projection instance)
3. **Implement ProjectionManager** (registration, checkpoint tracking, apply logic)
4. **Implement checkpoint persistence** (file-based checkpoint storage)

### Phase 2: Background Processing
1. **Create ProjectionDaemon** (IHostedService)
2. **Implement polling logic** (read events since checkpoint)
3. **Batch event processing** (configurable batch size)
4. **Error handling** (retry logic, dead-letter queue)

### Phase 3: Projection Definitions
1. **Create CourseShortInfoProjection**
2. **Create StudentShortInfoProjection**
3. **Update query handlers** to use `IProjectionStore<T>`

### Phase 4: Advanced Features (Future)
1. **In-memory projection store** (for high-performance scenarios)
2. **Projection rebuilding UI/API**
3. **Projection health monitoring**
4. **Multi-tenant projection support**

---

## Performance Benefits

### Before (Event Replay on Every Query)
- **List 1000 courses**: Read ~10,000 events (course + enrollment events)
- **Query time**: 500ms - 2000ms
- **File I/O**: ~10,000 file reads

### After (Projection-Based)
- **List 1000 courses**: Read 1000 projection files
- **Query time**: 50ms - 200ms
- **File I/O**: ~1000 file reads
- **Background daemon**: Keeps projections up-to-date incrementally

---

## Migration Path

1. **Keep existing query handlers** working (backward compatibility)
2. **Deploy projection system** (background daemon starts building projections)
3. **Monitor projection lag** (time between event and projection update)
4. **Switch query handlers** to use projections once caught up
5. **Remove old event replay logic** (cleanup)

---

## Configuration Options

```csharp
public class ProjectionOptions
{
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);
    public int BatchSize { get; set; } = 1000;
    public bool EnableAutoRebuild { get; set; } = true;
    public List<Assembly> ScanAssemblies { get; set; } = new();
}
```

---

## Error Handling

1. **Poison Event Handling**
   - Log event that caused error
   - Skip and continue (with flag for manual review)
   - Alert administrators

2. **Checkpoint Recovery**
   - Atomic checkpoint updates
   - Checkpoint validation on startup
   - Rebuild from last valid checkpoint

3. **Projection Consistency**
   - Version projection schemas
   - Handle schema migrations
   - Support side-by-side projection versions

---

## Testing Strategy

1. **Unit Tests**
   - Projection apply logic
   - Checkpoint persistence
   - Key selector logic

2. **Integration Tests**
   - End-to-end projection building
   - Daemon polling and updates
   - Query handler integration

3. **Performance Tests**
   - Benchmark projection vs. event replay
   - Measure projection lag under load
   - Test with large datasets (100k+ events)
