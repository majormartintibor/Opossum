# Changelog

All notable changes to the Opossum project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed - 2024-12-XX

#### Query Model Refactoring for Full DCB Compliance

**Breaking Changes:**
- Refactored `QueryItem` from abstract base class to concrete class
- Removed `EventTypeQueryItem` class (use `QueryItem.EventTypes` instead)
- Removed `TagQueryItem` class (use `QueryItem.Tags` instead)

**New Structure:**
```csharp
public class QueryItem
{
    public List<string> EventTypes { get; set; } = [];  // OR logic
    public List<Tag> Tags { get; set; } = [];           // AND logic
}
```

**Migration Guide:**

Before:
```csharp
// Old approach - separate query items
new Query
{
    QueryItems = 
    [
        new EventTypeQueryItem { EventType = "Type1" },
        new EventTypeQueryItem { EventType = "Type2" }
    ]
}
```

After:
```csharp
// New approach - combined in single item
new Query
{
    QueryItems = 
    [
        new QueryItem { EventTypes = ["Type1", "Type2"] }
    ]
}

// Or use factory method
Query.FromEventTypes("Type1", "Type2")
```

**Benefits:**
- ✅ Full DCB specification compliance
- ✅ Support for combined type+tag queries in single QueryItem
- ✅ Cleaner query building with factory methods
- ✅ Correct OR/AND logic semantics
- ✅ More expressive query capabilities

**Added Factory Methods:**
- `Query.All()` - Returns all events
- `Query.FromItems(params QueryItem[])` - Build from items
- `Query.FromEventTypes(params string[])` - Query by types
- `Query.FromTags(params Tag[])` - Query by tags

**Documentation:**
- Added `Documentation/query-examples.md` with comprehensive usage examples
- Updated `Documentation/solution-review.md` to reflect changes
- Added XML documentation to all public APIs

**Impact:**
- No runtime performance impact
- Enables future FileSystemEventStore implementation
- Maintains backward compatibility with IEventStore interface
- Query matching logic is now DCB-compliant

---

## Version History (Completed Features)

### Mediator Pattern - Completed
- ✅ Full mediator pattern implementation
- ✅ Convention-based handler discovery
- ✅ Dependency injection support
- ✅ 41 comprehensive unit tests
- ✅ Complete documentation and examples

### Event Store Core Models - In Progress
- ✅ IEventStore interface defined
- ✅ DCB-compliant Query model
- ✅ SequencedEvent, DomainEvent, Tag models
- ✅ AppendCondition model
- ⚠️ FileSystemEventStore implementation pending

---

## Planned Changes

### Next Up
- [ ] Implement FileSystemEventStore
  - [ ] Directory structure initialization
  - [ ] Event JSON serialization
  - [ ] Ledger file management
  - [ ] Index creation and updates
  - [ ] Query execution engine
  - [ ] AppendCondition validation

- [ ] Configuration System
  - [ ] Implement OpossumOptions.AddContext()
  - [ ] Add service registration in AddOpossum()
  - [ ] Add directory path configuration

- [ ] Sample Application
  - [ ] Course enrollment domain implementation
  - [ ] API endpoints for commands
  - [ ] Integration with event store

### Future
- [ ] Aggregate loading extensions
- [ ] Source generation for command dispatchers
- [ ] Snapshot support
- [ ] Projection daemon
- [ ] Performance optimizations

---

## Breaking Changes Log

### Query Model Refactoring (Current)

**Affected Code:**
- Any code using `EventTypeQueryItem` 
- Any code using `TagQueryItem`
- Custom query building logic

**Migration Effort:** Low (simple search & replace)

**Justification:** Required for DCB specification compliance

---

## Notes

This changelog tracks only released and upcoming breaking changes. For detailed implementation status, see `Documentation/solution-review.md`.
