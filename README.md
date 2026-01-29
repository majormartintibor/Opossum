# Opossum

A file system-based Event Store implementing the Dynamic Consistency Boundary (DCB) specification with integrated mediator pattern.

---

## 📖 Overview

Opossum provides event sourcing capabilities using the file system as storage backend. It implements the DCB specification for optimistic concurrency control and includes a Wolverine-inspired mediator pattern for command/query handling.

**Status**: 🚧 In Development (42% complete)

---

## ⚠️ IMPORTANT: Development Constraints

**The sample project (`Opossum.Samples.CourseManagement`) must be written MANUALLY without AI code generation.**

This constraint ensures the full developer experience of using the library and helps identify usability issues early.

### What This Means:

- ✅ AI can implement library code in `src\Opossum\`
- ✅ AI can create tests in `tests\` directories
- ✅ AI can generate documentation
- ❌ AI **cannot** generate code for `Samples\Opossum.Samples.CourseManagement\`
- ❌ AI **cannot** create sample domain events, aggregates, commands, or handlers

**See [Documentation/AI_CONSTRAINTS.md](./Documentation/AI_CONSTRAINTS.md) for complete details.**

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
│   └── Opossum/                    # Main library (✅ AI can implement)
│       ├── Configuration/          # ✅ COMPLETE - OpossumOptions
│       ├── Core/                   # Query model, domain types
│       ├── DependencyInjection/    # ✅ COMPLETE - ServiceCollectionExtensions
│       ├── Mediator/              # ✅ COMPLETE - Mediator pattern
│       └── Storage/
│           └── FileSystem/        # ✅ COMPLETE - StorageInitializer
│                                  # ⚠️ TODO - FileSystemEventStore
├── Samples/
│   └── Opossum.Samples.CourseManagement/  # ⚠️ MANUAL ONLY - NO AI CODE GEN
│       └── Domain/                        # Developer must write manually
├── tests/
│   ├── Opossum.UnitTests/         # ✅ AI can create tests
│   └── Opossum.IntegrationTests/  # ✅ AI can create tests
├── Documentation/                  # ✅ AI can generate docs
│   ├── AI_CONSTRAINTS.md          # 🚫 AI implementation rules
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
- **Sample domain models (MANUAL ONLY - no AI)**

### Major Work Item: FileSystemEventStore (8-12 hours)

- AppendAsync() implementation
- ReadAsync() implementation
- Ledger management
- Index management
- Concurrency control

See [Documentation/PROGRESS.md](./Documentation/PROGRESS.md) for detailed status.

---

## 📚 Documentation

- **[Solution Review](./Documentation/solution-review.md)** - Comprehensive analysis
- **[What to Build Now](./Documentation/what-to-build-now.md)** - Ready-to-implement items
- **[Implementation Ready](./Documentation/implementation-ready.md)** - Detailed implementation guide
- **[AI Constraints](./Documentation/AI_CONSTRAINTS.md)** - AI code generation rules
- **[DCB Specification](./Specification/DCB-Specification.md)** - Core specification
- **[Query Examples](./Documentation/query-examples.md)** - DCB query patterns

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

### For Library Development (AI Assisted)

Work on these can use AI assistance:
- Core library features in `src/Opossum/`
- Unit and integration tests
- Documentation
- Build scripts

### For Sample Application (Manual Only)

The sample project must be written manually:
- Domain events, aggregates, commands
- Command handlers
- Application logic

This ensures real usability testing of the library.

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