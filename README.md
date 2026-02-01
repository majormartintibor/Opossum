# Opossum

A file system-based Event Store implementing the Dynamic Consistency Boundary (DCB) specification with integrated mediator pattern.

---

## 📖 Overview

Opossum provides event sourcing capabilities using the file system as storage backend. It implements the DCB specification for optimistic concurrency control and includes a Wolverine-inspired mediator pattern for command/query handling.

**Status**: 🚧 In Development (42% complete)

---

## 🚀 Quick Start

### Installation (Future)

```bash
dotnet add package Opossum
```

### Configuration

```csharp
// In Program.cs
builder.Services.AddOpossum(options =>
{
    options.RootPath = "./EventStore";
    options.AddContext("CourseManagement");
    options.AddContext("Billing");
});
```

### Basic Usage

```csharp
// Append events
var events = new List<DomainEvent>
{
    new DomainEvent
    {
        EventType = "StudentEnlisted",
        Event = new StudentEnlistedEvent(courseId, studentId),
        Tags = 
        [
            new Tag { Key = "CourseId", Value = courseId.ToString() },
            new Tag { Key = "StudentId", Value = studentId.ToString() }
        ]
    }
};

await eventStore.AppendAsync("CourseManagement", events);

// Read events
var query = Query.FromTags([new Tag { Key = "CourseId", Value = courseId.ToString() }]);
var sequencedEvents = await eventStore.ReadAsync("CourseManagement", query);
```

---

## 📂 Project Structure

```
Opossum/
├── src/
│   └── Opossum/                    # Main library
│       ├── Configuration/          # ✅ COMPLETE - OpossumOptions
│       ├── Core/                   # Query model, domain types
│       ├── DependencyInjection/    # ✅ COMPLETE - ServiceCollectionExtensions
│       ├── Mediator/              # ✅ COMPLETE - Mediator pattern
│       └── Storage/
│           └── FileSystem/        # ✅ COMPLETE - StorageInitializer
│                                  # ⚠️ TODO - FileSystemEventStore
├── Samples/
│   └── Opossum.Samples.CourseManagement/  # Example course management domain
│       └── Domain/                        # Domain models and handlers
├── tests/
│   ├── Opossum.UnitTests/         # Unit tests
│   └── Opossum.IntegrationTests/  # Integration tests
├── Documentation/                  # Documentation
│   ├── PROGRESS.md                # Implementation progress
│   └── implementation-ready.md    # Component implementation guide
└── Specification/                  # Reference documentation
    ├── DCB-Specification.md
    ├── InitialSpecification.MD
    └── mediator-pattern-specification.md
```

---

## ✅ What's Complete

### Phase 2: Configuration System (100% ✅)

- **OpossumOptions** - Configuration class (19 tests passing)
- **StorageInitializer** - Directory structure creation (17 tests passing)
- **ServiceCollectionExtensions** - DI integration (19 tests passing)
- **OpossumFixture** - Integration test infrastructure (16 tests passing)

**Total**: 71 tests passing | 155 minutes invested | 15 min ahead of schedule

### Mediator Pattern (100% ✅)

- Full Wolverine-inspired mediator implementation
- Convention-based handler discovery
- Request/response pattern
- Async support with cancellation

---

## 🚧 What's Next

### Phase 1: Independent Components (~4 hours)

- Custom exception classes
- ReadOption enum enhancements  
- EventStore extension methods
- Sample domain models

### Major Work Item: FileSystemEventStore (8-12 hours)

- AppendAsync() implementation
- ReadAsync() implementation
- Ledger management
- Index management
- Concurrency control

See [Documentation/PROGRESS.md](./Documentation/PROGRESS.md) for detailed status.

---

## 📚 Documentation

### Getting Started
- **[Quick Start](./Documentation/PROJECTIONS_QUICK_START.md)** - Add projections in 3 steps
- **[Projection Architecture](./Documentation/PROJECTIONS_ARCHITECTURE.md)** - Design and patterns
- **[Test Coverage](./Documentation/PROJECTIONS_TEST_COVERAGE.md)** - 71 projection tests explained

### Reference
- **[DCB Specification](./Specification/DCB-Specification.md)** - Core concurrency model
- **[Query Examples](./Documentation/query-examples.md)** - Event query patterns

### Implementation Details
- **[Projections Implementation](./Documentation/PROJECTIONS_IMPLEMENTATION_SUMMARY.md)** - What was built
- **[Solution Review](./Documentation/solution-review.md)** - Architecture analysis
- **[Mediator Specification](./Specification/mediator-pattern-specification.md)** - Mediator design

---

## 🧪 Testing

```bash
# Run all tests
dotnet test

# Run unit tests only
dotnet test tests/Opossum.UnitTests

# Run integration tests
dotnet test tests/Opossum.IntegrationTests
```

**Current**: 112 tests passing (1 expected failure in ExampleTest - needs handler)

---

## 🏗️ Architecture

### Event Storage

Events are stored as JSON files in the following structure:

```
/RootPath
  /CourseManagement              # Bounded context
    /.ledger                     # Sequence tracking
    /Events                      # Event JSON files
      /{guid}.json
    /Indices
      /EventType                 # Event type index
        /StudentEnlisted.idx
      /Tags                      # Tag index
        /CourseId_{value}.idx
        /StudentId_{value}.idx
```

### DCB Compliance

- **Optimistic Concurrency**: AppendCondition validates queries before appending
- **Query Model**: Supports OR between QueryItems, OR between EventTypes, AND between Tags
- **Sequence Positions**: Monotonically increasing sequence per context

---

## 🤝 Contributing

Contributions are welcome! Please feel free to submit pull requests for:
- Core library features in `src/Opossum/`
- Unit and integration tests
- Documentation improvements
- Sample applications and examples
- Build and tooling enhancements

---

## 📝 License

[License information to be added]

---

## 🙏 Acknowledgments

- **Wolverine** - Inspiration for mediator pattern
- **DCB Specification** - Core concurrency model

---

## 📊 Progress

| Component | Status | Tests |
|-----------|--------|-------|
| Mediator Pattern | ✅ Complete | N/A |
| Configuration System | ✅ Complete | 71/71 |
| Domain Model | ✅ Complete | N/A |
| FileSystemEventStore | ⚠️ TODO | 0/0 |
| Sample Application | ⚠️ Manual | 0/0 |

**Overall**: 42% complete | Next milestone: FileSystemEventStore implementation

---

**Questions?** Check [Documentation/](./Documentation/) or open an issue.