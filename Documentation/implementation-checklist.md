# 🎯 Implementation Checklist

Use this checklist to track implementation progress. Check off items as you complete them.

---

## ⚠️ IMPORTANT: Sample Project Development Constraint

**`Opossum.Samples.CourseManagement` must be written MANUALLY without AI code generation.**

Items marked with ⚠️ **MANUAL ONLY** are restricted from AI code generation:
- ✅ You may ask AI questions about the library
- ✅ AI may explain patterns and best practices
- ❌ AI may NOT generate code for sample project files
- ❌ AI may NOT create/modify files in `Samples\Opossum.Samples.CourseManagement\`

This ensures you get the full developer experience of using the Opossum library.

---

## Phase 1: Independent Components (4 hours)

These can be implemented in ANY order with ZERO dependencies:

### Configuration
- [ ] **OpossumOptions** (`src\Opossum\Configuration\OpossumOptions.cs`) ⭐ **30 min**
  - [ ] Add `RootPath` property
  - [ ] Add `Contexts` list
  - [ ] Add `AddContext()` method
  - [ ] Add validation logic
  - [ ] Add `IsValidDirectoryName()` helper

### Error Handling
- [ ] **Custom Exceptions** (`src\Opossum\Exceptions\EventStoreExceptions.cs`) **30 min**
  - [ ] Create EventStoreException base class
  - [ ] Create AppendConditionFailedException
  - [ ] Create EventNotFoundException
  - [ ] Create InvalidQueryException
  - [ ] Create ConcurrencyException
  - [ ] Create ContextNotFoundException

### Core Enhancements
- [ ] **ReadOption Enum** (`src\Opossum\Core\ReadOption.cs`) **15 min**
  - [ ] Add Descending option
  - [ ] Add Limit option
  - [ ] Add Skip option
  - [ ] Add FromPosition option
  - [ ] Create ReadOptionConfig class

### Extensions
- [ ] **EventStore Extensions** (`src\Opossum\Extensions\EventStoreExtensions.cs`) **1 hour**
  - [ ] Implement LoadAggregateAsync<T>()
  - [ ] Implement AppendAsync() single event overload
  - [ ] Implement AppendEventsAsync() helper
  - [ ] Add XML documentation

### Domain Models - Events ⚠️ MANUAL ONLY
- [ ] **Course Events** (`Samples\Opossum.Samples.CourseManagement\Domain\Events.cs`) **30 min**
  - Developer must implement manually (no AI code generation)
  - [ ] Create StudentEnlistedToCourseEvent
  - [ ] Create StudentWithdrawnFromCourseEvent
  - [ ] Create CourseReachedCapacityEvent
  - [ ] Create CourseCapacityIncreasedEvent
  - [ ] Create CourseCreatedEvent

### Domain Models - Aggregate ⚠️ MANUAL ONLY
- [ ] **CourseEnlistmentAggregate** (`Samples\...\Domain\CourseEnlistmentAggregate.cs`) **45 min**
  - Developer must implement manually (no AI code generation)
  - [ ] Define aggregate properties
  - [ ] Add IsStudentEnlisted() method
  - [ ] Add Apply(StudentEnlistedToCourseEvent) method
  - [ ] Add Apply(StudentWithdrawnFromCourseEvent) method
  - [ ] Add Apply(CourseCapacityIncreasedEvent) method
  - [ ] Add Apply(CourseCreatedEvent) method
  - [ ] Add Apply(CourseReachedCapacityEvent) method

### Domain Models - Commands ⚠️ MANUAL ONLY
- [ ] **Commands & Queries** (`Samples\...\Domain\Commands.cs`) **20 min**
  - Developer must implement manually (no AI code generation)
  - [ ] Create EnlistStudentToCourseCommand
  - [ ] Create WithdrawStudentFromCourseCommand
  - [ ] Create IncreaseCourseCapacityCommand
  - [ ] Create CreateCourseCommand
  - [ ] Create CommandResult record
  - [ ] Create CourseQueries static class with helper methods

### Domain Models - Handlers ⚠️ MANUAL ONLY
- [ ] **Command Handlers** (`Samples\...\Domain\Handlers\*.cs`) **30 min**
  - Developer must implement manually (no AI code generation)
  - [ ] Create EnlistStudentToCourseHandler
  - [ ] Implement validation logic
  - [ ] Implement event creation
  - [ ] Add proper tagging
  - [ ] Add capacity check logic

**Phase 1 Checkpoint**: ✅ ~4 hours of work completed

---

## Phase 2: Configuration System (3 hours)

These depend on OpossumOptions being complete:

### Storage Initialization
- [ ] **StorageInitializer** (`src\Opossum\Storage\FileSystem\StorageInitializer.cs`) **1 hour**
  - [ ] Create constructor accepting OpossumOptions
  - [ ] Implement Initialize() method
  - [ ] Create root directory
  - [ ] Create context directories
  - [ ] Create .ledger files
  - [ ] Create Events subdirectories
  - [ ] Create Indices/EventType subdirectories
  - [ ] Create Indices/Tags subdirectories

### Dependency Injection
- [ ] **ServiceCollectionExtensions** (`src\Opossum\DependencyInjection\ServiceCollectionExtensions.cs`) **1 hour**
  - [ ] Implement AddOpossum() method
  - [ ] Invoke configure action
  - [ ] Validate options (at least one context)
  - [ ] Register OpossumOptions as singleton
  - [ ] Call StorageInitializer.Initialize()
  - [ ] Register IEventStore as singleton
  - [ ] Return services

### Test Infrastructure
- [ ] **OpossumFixture** (`tests\Opossum.IntegrationTests\OpossumFixture.cs`) **30 min**
  - [ ] Remove TODO comments
  - [ ] Create temp storage path
  - [ ] Configure ServiceCollection
  - [ ] Call AddOpossum() with test path
  - [ ] Add contexts
  - [ ] Call AddMediator()
  - [ ] Add logging
  - [ ] Build ServiceProvider
  - [ ] Implement proper Dispose()

- [ ] **ExampleTest** (`tests\Opossum.IntegrationTests\ExampleTest.cs`) **30 min**
  - [ ] Remove TODO comments
  - [ ] Create course creation test
  - [ ] Create student enrollment test
  - [ ] Build proper queries
  - [ ] Test aggregate loading
  - [ ] Add assertions

**Phase 2 Checkpoint**: ✅ ~7 hours total work completed (60% solution complete)

---

## Phase 3: Core EventStore (8-12 hours)

**⚠️ REQUIRES DESIGN DECISIONS - Do NOT start until Phase 1 & 2 complete**

### JSON Serialization
- [ ] **Decide** on polymorphic serialization strategy
- [ ] **Implement** custom JsonConverter if needed
- [ ] **Test** round-trip serialization

### FileSystemEventStore - Read
- [ ] Implement ReadAsync() method
- [ ] Implement index reading logic
- [ ] Implement query resolution
- [ ] Implement event deserialization
- [ ] Handle empty indices gracefully

### FileSystemEventStore - Write
- [ ] Implement AppendAsync() method
- [ ] Implement sequence position assignment
- [ ] Implement event file writing
- [ ] Implement ledger updates
- [ ] Implement EventType index updates
- [ ] Implement Tag index updates

### FileSystemEventStore - Concurrency
- [ ] **Decide** on locking strategy
- [ ] Implement AppendCondition validation
- [ ] Implement atomic operations
- [ ] Add retry logic
- [ ] Test concurrent appends

**Phase 3 Checkpoint**: ✅ ~19 hours total work completed (90% solution complete)

---

## Verification Checklist

After each phase, verify:

### After Phase 1
- [ ] Solution builds successfully
- [ ] All new files have proper namespaces
- [ ] XML documentation is present
- [ ] No compilation warnings
- [ ] Domain models can be instantiated
- [ ] Extensions compile

### After Phase 2
- [ ] Solution builds successfully
- [ ] OpossumFixture can be constructed
- [ ] Directory structure is created on startup
- [ ] .ledger files are created
- [ ] Services can be resolved from DI
- [ ] Tests discover fixtures correctly

### After Phase 3
- [ ] All integration tests pass
- [ ] Events can be appended and read back
- [ ] Queries work correctly
- [ ] Aggregates can be loaded
- [ ] AppendCondition validation works
- [ ] Concurrent operations are safe

---

## Quick Reference: File Paths

### New Files to Create
```
src\Opossum\
  ├─ Exceptions\
  │  └─ EventStoreExceptions.cs
  ├─ Extensions\
  │  └─ EventStoreExtensions.cs
  └─ Storage\FileSystem\
     └─ StorageInitializer.cs

Samples\Opossum.Samples.CourseManagement\
  └─ Domain\
     ├─ Events.cs
     ├─ CourseEnlistmentAggregate.cs
     ├─ Commands.cs
     └─ Handlers\
        └─ EnlistStudentToCourseHandler.cs
```

### Files to Modify
```
src\Opossum\
  ├─ Configuration\
  │  └─ OpossumOptions.cs (empty → full implementation)
  ├─ DependencyInjection\
  │  └─ ServiceCollectionExtensions.cs (stub → full)
  ├─ Core\
  │  └─ ReadOption.cs (add enum values)
  └─ Storage\FileSystem\
     └─ FileSystemEventStore.cs (NotImplementedException → full)

tests\Opossum.IntegrationTests\
  ├─ OpossumFixture.cs (TODOs → implementation)
  └─ ExampleTest.cs (TODOs → real test)
```

---

## Time Tracking

| Phase | Estimated | Actual | Notes |
|-------|-----------|--------|-------|
| Phase 1 Items 1-8 | 4h | __ | |
| Phase 2 Items 9-12 | 3h | __ | |
| Phase 3 EventStore | 8-12h | __ | |
| **Total** | **15-19h** | **__** | |

---

## 🎯 Current Status

**Overall Progress**: __ / 19 hours (__ %)

**Phase 1**: __ / 8 items  
**Phase 2**: __ / 4 items  
**Phase 3**: __ / 12 items

**Last Updated**: _____________

---

## 📝 Notes & Decisions

Use this space to track decisions made during implementation:

```
Date: _______
Decision: _______________________________________
Rationale: ____________________________________
Impact: ________________________________________

Date: _______
Decision: _______________________________________
Rationale: ____________________________________
Impact: ________________________________________
```

---

## ✅ Done!

When all items are checked:
- [ ] Run full solution build
- [ ] Run all tests
- [ ] Update CHANGELOG.md
- [ ] Update solution-review.md with new completion %
- [ ] Commit changes
- [ ] Create PR or push to main
- [ ] Celebrate! 🎉
