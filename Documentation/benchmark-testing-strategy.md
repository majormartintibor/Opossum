# Opossum Benchmark Testing Strategy

**Document Version:** 1.0  
**Date:** December 2024  
**Project:** Opossum - File System Event Store with DCB  
**Target Framework:** .NET 10

---

## 📋 Executive Summary

This document outlines a comprehensive performance and memory allocation testing strategy for the Opossum Event Store library. The strategy combines industry best practices from .NET benchmarking with the specific requirements of a file system-based event store implementation.

**Key Objectives:**
- Establish performance baselines for core operations
- Identify memory allocation hotspots
- Validate performance at scale (10k-100k+ events)
- Guide optimization efforts with empirical data
- Prevent performance regressions during refactoring

**Scope:** Manual execution only - not part of CI/CD pipeline

---

## 🎯 Industry Best Practices for .NET Benchmarking

### 1. BenchmarkDotNet Framework

**Tool:** [BenchmarkDotNet](https://benchmarkdotnet.org/) - Industry standard for .NET performance testing

**Why BenchmarkDotNet:**
- ✅ Eliminates JIT compilation artifacts through warm-up iterations
- ✅ Statistical analysis with median, mean, standard deviation
- ✅ Memory allocation diagnostics (GC pressure analysis)
- ✅ Baseline comparisons to detect regressions
- ✅ Export results to multiple formats (HTML, CSV, Markdown, JSON)
- ✅ Cross-platform support (.NET Framework, .NET Core, .NET 5+)
- ✅ CPU instruction-level profiling integration
- ✅ Outlier detection and iteration tuning

**Configuration Recommendations:**
```csharp
[Config(typeof(BenchmarkConfig))]
public class MyBenchmark
{
    public class BenchmarkConfig : ManualConfig
    {
        public BenchmarkConfig()
        {
            // Multiple runs for statistical significance
            AddJob(Job.Default
                .WithWarmupCount(3)      // JIT warm-up iterations
                .WithIterationCount(10)   // Actual measurement iterations
                .WithInvocationCount(100) // Calls per iteration
                .WithGcMode(new GcMode
                {
                    Force = true,        // Force GC between benchmarks
                    Concurrent = true,   // Match production GC mode
                    Server = false       // Workstation GC for dev machines
                }));
            
            // Diagnostic tools
            AddDiagnoser(MemoryDiagnoser.Default); // Heap allocations
            AddDiagnoser(ThreadingDiagnoser.Default); // Thread pool stats
            
            // Export results
            AddExporter(HtmlExporter.Default);
            AddExporter(MarkdownExporter.GitHub);
            AddExporter(CsvMeasurementsExporter.Default);
        }
    }
}
```

### 2. Benchmark Classification

**Micro-Benchmarks (Unit Level):**
- Focus: Individual methods, algorithms, data structures
- Duration: Microseconds to milliseconds
- Use cases: Serialization, deserialization, index lookups, file path generation

**Macro-Benchmarks (Component Level):**
- Focus: Multi-step operations within a single component
- Duration: Milliseconds to seconds
- Use cases: Event append with indexing, batch read operations, query execution

**End-to-End Benchmarks (Integration Level):**
- Focus: Complete user workflows
- Duration: Seconds to minutes
- Use cases: Aggregate reconstruction, command processing via mediator, multi-context operations

### 3. Data Management Strategy

**Baseline Data:**
- Pre-seeded event stores with known characteristics
- Never cleaned up - serves as regression baseline
- Versioned alongside code changes

**Test Data Tiers:**
- **Tier 1 (Small):** 1,000 events - Quick validation
- **Tier 2 (Medium):** 10,000 events - Typical application scale
- **Tier 3 (Large):** 100,000 events - Enterprise scale
- **Tier 4 (Extreme):** 1,000,000+ events - Stress testing

**Data Characteristics:**
- Realistic tag distributions (1-10 tags per event)
- Multiple event types (10-50 distinct types)
- Temporal clustering (events grouped by time periods)
- Multi-context scenarios (2-5 bounded contexts)

### 4. Statistical Rigor

**Measurement Guidelines:**
- Minimum 3 warm-up iterations (JIT compilation)
- Minimum 10 measurement iterations
- Report median (more stable than mean for skewed distributions)
- Include P95/P99 percentiles for latency-sensitive operations
- Calculate coefficient of variation (CV) - warn if > 10%

**Memory Analysis:**
- Gen 0/1/2 collection counts
- Allocated bytes per operation
- Large Object Heap (LOH) allocations
- Total heap size growth over time

### 5. Comparison Baseline Strategy

**Baseline Approach:**
```csharp
[Benchmark(Baseline = true)]
public void CurrentImplementation() { /* existing code */ }

[Benchmark]
public void OptimizedImplementation() { /* new code */ }
```

**Version Tagging:**
- Tag baseline results with Git commit SHA
- Store historical results in `BenchmarkResults/` directory
- Track performance trends over time

---

## 🏗️ Opossum-Specific Benchmark Architecture

### Component Hierarchy

```
Opossum.BenchmarkTests/
├── Config/
│   └── BenchmarkConfiguration.cs          # Shared BenchmarkDotNet config
├── Infrastructure/
│   ├── BenchmarkFixture.cs                # Shared setup/teardown
│   ├── DataSeeder.cs                      # Baseline data generation
│   └── TestDataGenerators.cs             # Event/query generators
├── Benchmarks/
│   ├── 1_Serialization/
│   │   ├── JsonSerializerBenchmarks.cs
│   │   └── PolymorphicEventBenchmarks.cs
│   ├── 2_FileOperations/
│   │   ├── EventFileManagerBenchmarks.cs
│   │   └── LedgerManagerBenchmarks.cs
│   ├── 3_Indexing/
│   │   ├── EventTypeIndexBenchmarks.cs
│   │   ├── TagIndexBenchmarks.cs
│   │   └── IndexManagerBenchmarks.cs
│   ├── 4_EventStore/
│   │   ├── AppendBenchmarks.cs
│   │   ├── ReadBenchmarks.cs
│   │   └── QueryExecutionBenchmarks.cs
│   ├── 5_Mediator/
│   │   ├── HandlerDiscoveryBenchmarks.cs
│   │   └── MediatorInvocationBenchmarks.cs
│   └── 6_EndToEnd/
│       ├── AggregateReconstructionBenchmarks.cs
│       ├── CommandProcessingBenchmarks.cs
│       └── MultiContextBenchmarks.cs
├── BaselineData/
│   ├── Small/      # 1k events (committed to repo)
│   ├── Medium/     # 10k events (committed to repo)
│   ├── Large/      # 100k events (.gitignore - generated locally)
│   └── Extreme/    # 1M+ events (.gitignore - generated locally)
└── BenchmarkResults/
    └── [timestamped result files]
```

---

## 📊 Detailed Benchmark Specifications

### Category 1: Serialization Benchmarks (Micro)

**File:** `Benchmarks/1_Serialization/JsonSerializerBenchmarks.cs`

**Objectives:**
- Measure JSON serialization throughput
- Identify memory allocation patterns
- Compare polymorphic vs. non-polymorphic event serialization

**Benchmark Methods:**

```csharp
[MemoryDiagnoser]
public class JsonSerializerBenchmarks
{
    // Scenario 1: Simple event serialization
    [Benchmark]
    public string Serialize_SimpleEvent()
    
    // Scenario 2: Event with multiple tags (realistic)
    [Benchmark]
    public string Serialize_EventWith10Tags()
    
    // Scenario 3: Event with large payload (1KB, 10KB, 100KB)
    [Benchmark]
    [Arguments(1024)]
    [Arguments(10240)]
    [Arguments(102400)]
    public string Serialize_EventWithPayload(int payloadSizeBytes)
    
    // Scenario 4: Polymorphic type resolution overhead
    [Benchmark]
    public string Serialize_PolymorphicEvent()
    
    // Scenario 5: Deserialization performance
    [Benchmark]
    public SequencedEvent Deserialize_SimpleEvent()
    
    // Scenario 6: Batch serialization (100 events)
    [Benchmark]
    public string[] Serialize_100Events()
}
```

**Expected Insights:**
- Serialization throughput (MB/s)
- Memory allocations per operation
- Impact of tag count on performance
- Polymorphic converter overhead

**Success Criteria:**
- < 1ms for simple event serialization
- < 100KB allocations per event
- Linear scaling with payload size
- < 10% overhead for polymorphic events

---

### Category 2: File Operations Benchmarks (Micro/Macro)

**File:** `Benchmarks/2_FileOperations/EventFileManagerBenchmarks.cs`

**Objectives:**
- Measure file I/O throughput
- Validate atomic write performance
- Test concurrent read scenarios

**Benchmark Methods:**

```csharp
[MemoryDiagnoser]
public class EventFileManagerBenchmarks
{
    // Scenario 1: Single event write (atomic operation)
    [Benchmark]
    public Task WriteEventAsync_SingleEvent()
    
    // Scenario 2: Sequential writes (100 events)
    [Benchmark]
    public Task WriteEventAsync_100Events_Sequential()
    
    // Scenario 3: Batch read (10, 100, 1000 events)
    [Benchmark]
    [Arguments(10)]
    [Arguments(100)]
    [Arguments(1000)]
    public Task ReadEventsAsync_Batch(int eventCount)
    
    // Scenario 4: Random access pattern (1000 reads from 100k events)
    [Benchmark]
    public Task ReadEventsAsync_RandomAccess()
    
    // Scenario 5: Sequential read (scan all events)
    [Benchmark]
    public Task ReadEventsAsync_SequentialScan()
    
    // Scenario 6: File existence checks (batch of 1000)
    [Benchmark]
    public bool[] EventFileExists_Batch1000()
}
```

**File System Considerations:**
- Test on SSD vs. HDD (manual configuration)
- Test with/without OS file caching
- Test with antivirus exclusions applied

**Expected Insights:**
- I/O throughput (events/second)
- Impact of file caching
- Random vs. sequential read performance
- File system overhead (NTFS metadata)

**Success Criteria:**
- > 1000 events/sec write throughput (SSD)
- < 5ms p95 latency for single read
- Linear scaling for batch reads
- < 50% performance degradation with random access

---

### Category 3: Indexing Benchmarks (Micro/Macro)

**File:** `Benchmarks/3_Indexing/IndexManagerBenchmarks.cs`

**Objectives:**
- Measure index write performance
- Validate query execution speed
- Test index memory consumption

**Benchmark Methods:**

```csharp
[MemoryDiagnoser]
public class IndexManagerBenchmarks
{
    // Scenario 1: Add event to indices (single event)
    [Benchmark]
    public Task AddEventToIndicesAsync_SingleEvent()
    
    // Scenario 2: Bulk index build (10k events)
    [Benchmark]
    public Task BuildIndices_10kEvents()
    
    // Scenario 3: EventType index lookup (exact match)
    [Benchmark]
    public Task GetPositionsByEventTypeAsync_SingleType()
    
    // Scenario 4: EventType index lookup (union of 5 types)
    [Benchmark]
    public Task GetPositionsByEventTypesAsync_5Types()
    
    // Scenario 5: Tag index lookup (single tag)
    [Benchmark]
    public Task GetPositionsByTagAsync_SingleTag()
    
    // Scenario 6: Tag index lookup (intersection of 3 tags)
    [Benchmark]
    public Task GetPositionsByTagsAsync_3Tags()
    
    // Scenario 7: Complex query (multiple event types + tags)
    [Benchmark]
    public Task ExecuteComplexQuery()
    
    // Scenario 8: Index file persistence (save/load)
    [Benchmark]
    public Task SaveAndLoadIndices_100kEvents()
}
```

**Index Characteristics (Baseline Data):**
- Event Type Distribution: Zipf's law (80/20 rule)
- Tag Cardinality: Low (courseId), Medium (studentId), High (timestamp)
- Index File Sizes: 1MB, 10MB, 100MB

**Expected Insights:**
- Index update overhead during append
- Query execution time by complexity
- Memory footprint of in-memory indices
- Index persistence I/O cost

**Success Criteria:**
- < 1ms to add event to all indices
- < 10ms to query 100k event index
- Index file size < 5% of event data size
- < 100ms to load 100k event index

---

### Category 4: Event Store Operations (Macro)

**File:** `Benchmarks/4_EventStore/AppendBenchmarks.cs`

**Objectives:**
- Measure end-to-end append performance
- Test append condition validation cost
- Validate concurrency handling

**Benchmark Methods:**

```csharp
[MemoryDiagnoser]
public class AppendBenchmarks
{
    // Scenario 1: Single event append (no condition)
    [Benchmark]
    public Task AppendAsync_SingleEvent_NoCondition()
    
    // Scenario 2: Batch append (10, 100, 1000 events)
    [Benchmark]
    [Arguments(10)]
    [Arguments(100)]
    [Arguments(1000)]
    public Task AppendAsync_Batch(int eventCount)
    
    // Scenario 3: Append with simple condition (no match)
    [Benchmark]
    public Task AppendAsync_WithCondition_NoMatch()
    
    // Scenario 4: Append with complex condition (3 query items)
    [Benchmark]
    public Task AppendAsync_WithComplexCondition()
    
    // Scenario 5: Concurrent appends (10 tasks, 100 events each)
    [Benchmark]
    public Task AppendAsync_Concurrent10x100()
    
    // Scenario 6: Full append pipeline (serialize + write + index + ledger)
    [Benchmark]
    public Task AppendAsync_FullPipeline()
}
```

**File:** `Benchmarks/4_EventStore/ReadBenchmarks.cs`

**Benchmark Methods:**

```csharp
[MemoryDiagnoser]
public class ReadBenchmarks
{
    // Scenario 1: Read all events (no filter)
    [Benchmark]
    public Task ReadAsync_AllEvents()
    
    // Scenario 2: Read by single event type (1%, 10%, 50% selectivity)
    [Benchmark]
    [Arguments(0.01)] // 1% of events match
    [Arguments(0.10)] // 10% of events match
    [Arguments(0.50)] // 50% of events match
    public Task ReadAsync_ByEventType(double selectivity)
    
    // Scenario 3: Read by single tag
    [Benchmark]
    public Task ReadAsync_ByTag()
    
    // Scenario 4: Read by complex query (OR of 3 items)
    [Benchmark]
    public Task ReadAsync_ComplexQuery()
    
    // Scenario 5: Read with sequence position filter
    [Benchmark]
    public Task ReadAsync_AfterPosition()
    
    // Scenario 6: Streaming read (IAsyncEnumerable pattern)
    [Benchmark]
    public Task ReadAsync_StreamingWithBackpressure()
}
```

**Data Set Characteristics:**
- Pre-seeded: 1k, 10k, 100k, 1M events
- Realistic tag distributions
- Varied event type frequencies

**Expected Insights:**
- Append throughput (events/sec)
- Append condition validation overhead
- Concurrency lock contention
- Read query execution time
- Impact of result set size on performance

**Success Criteria:**
- > 500 events/sec append throughput
- < 10% overhead for append condition validation
- > 100 events/sec read throughput
- Linear scaling with result set size

---

### Category 5: Mediator Pattern Benchmarks (Micro)

**File:** `Benchmarks/5_Mediator/MediatorInvocationBenchmarks.cs`

**Objectives:**
- Measure mediator invocation overhead
- Test handler discovery performance
- Validate DI container resolution cost

**Benchmark Methods:**

```csharp
[MemoryDiagnoser]
public class MediatorInvocationBenchmarks
{
    // Scenario 1: Simple handler invocation (no DI)
    [Benchmark]
    public Task InvokeAsync_SimpleHandler()
    
    // Scenario 2: Handler with DI resolution (3 dependencies)
    [Benchmark]
    public Task InvokeAsync_HandlerWithDependencies()
    
    // Scenario 3: Async handler vs sync handler
    [Benchmark]
    public Task InvokeAsync_AsyncHandler()
    
    [Benchmark]
    public Task InvokeAsync_SyncHandler()
    
    // Scenario 4: Handler discovery (first call vs cached)
    [Benchmark]
    public Task HandlerDiscovery_FirstCall()
    
    [Benchmark]
    public Task HandlerDiscovery_Cached()
    
    // Scenario 5: High-throughput scenario (1000 invocations)
    [Benchmark]
    public Task InvokeAsync_1000Calls()
    
    // Scenario 6: With timeout configuration
    [Benchmark]
    public Task InvokeAsync_WithTimeout()
}
```

**Expected Insights:**
- Mediator overhead vs direct call
- Handler discovery cache effectiveness
- DI resolution impact
- Async state machine overhead

**Success Criteria:**
- < 100μs for cached handler invocation
- < 1ms for first-time handler discovery
- 99%+ cache hit rate in production scenarios
- < 10% overhead vs direct method call

---

### Category 6: End-to-End Benchmarks (Integration)

**File:** `Benchmarks/6_EndToEnd/AggregateReconstructionBenchmarks.cs`

**Objectives:**
- Measure real-world command processing latency
- Test aggregate rebuild performance at scale
- Validate DCB pattern overhead

**Benchmark Methods:**

```csharp
[MemoryDiagnoser]
public class AggregateReconstructionBenchmarks
{
    // Scenario 1: Rebuild aggregate from 10 events
    [Benchmark]
    public CourseEnlistmentAggregate RebuildAggregate_10Events()

    // Scenario 2: Rebuild aggregate from 100 events
    [Benchmark]
    public CourseEnlistmentAggregate RebuildAggregate_100Events()

    // Scenario 3: Rebuild aggregate from 1000 events
    [Benchmark]
    public CourseEnlistmentAggregate RebuildAggregate_1000Events()

    // Scenario 4: Full command flow (query + rebuild + validate + append)
    [Benchmark]
    public Task ProcessCommand_EnrollStudent()

    // Scenario 5: Command with append condition check
    [Benchmark]
    public Task ProcessCommand_WithDCBValidation()

    // Scenario 6: Multi-aggregate scenario (student + course)
    [Benchmark]
    public Task ProcessCommand_MultipleAggregates()
}
```

**File:** `Benchmarks/6_EndToEnd/CommandProcessingBenchmarks.cs`

**Benchmark Methods:**

```csharp
[MemoryDiagnoser]
public class CommandProcessingBenchmarks
{
    // Scenario 1: Create course command (simple)
    [Benchmark]
    public Task ProcessCommand_CreateCourse()

    // Scenario 2: Enroll student command (with DCB)
    [Benchmark]
    public Task ProcessCommand_EnrollStudent()

    // Scenario 3: Batch command processing (100 enrollments)
    [Benchmark]
    public Task ProcessCommands_100Enrollments()

    // Scenario 4: Conflicting commands (test concurrency handling)
    [Benchmark]
    public Task ProcessCommands_Concurrent_SameCourse()

    // Scenario 5: Full user workflow (create course + enroll 30 students)
    [Benchmark]
    public Task ProcessWorkflow_CourseWithEnrollments()
}
```

**File:** `Benchmarks/6_EndToEnd/MultiContextBenchmarks.cs`

**Benchmark Methods:**

```csharp
[MemoryDiagnoser]
public class MultiContextBenchmarks
{
    // Scenario 1: Append to different contexts (no contention)
    [Benchmark]
    public Task AppendAsync_3Contexts_NoContention()

    // Scenario 2: Query across multiple contexts
    [Benchmark]
    public Task ReadAsync_3Contexts_Parallel()

    // Scenario 3: Context isolation validation
    [Benchmark]
    public Task ContextIsolation_NoLeakage()
}
```

**Expected Insights:**
- Real-world command latency (P50, P95, P99)
- Aggregate rebuild scaling characteristics
- DCB validation overhead in production scenarios
- Memory allocation per command

**Success Criteria:**
- < 50ms P95 latency for simple commands
- < 200ms P95 latency for complex commands (with aggregate rebuild)
- Linear scaling of rebuild time with event count
- < 1MB heap allocations per command

---

### Category 7: Concurrency & Optimistic Locking Benchmarks (Critical)

**File:** `Benchmarks/7_Concurrency/OptimisticLockingBenchmarks.cs`

**Objectives:**
- Measure overhead of AppendCondition validation
- Test semaphore lock contention at scale
- Validate retry/backoff strategies
- Measure DCB optimistic concurrency control performance

**Benchmark Methods:**

```csharp
[MemoryDiagnoser]
public class OptimisticLockingBenchmarks
{
    // Scenario 1: Append with no condition (baseline)
    [Benchmark(Baseline = true)]
    public Task AppendAsync_NoCondition()

    // Scenario 2: Append with AfterSequencePosition check
    [Benchmark]
    public Task AppendAsync_WithAfterPosition()

    // Scenario 3: Append with FailIfEventsMatch (simple query)
    [Benchmark]
    public Task AppendAsync_WithSimpleQuery()

    // Scenario 4: Append with FailIfEventsMatch (complex query - 3 items)
    [Benchmark]
    public Task AppendAsync_WithComplexQuery()

    // Scenario 5: Sequential appends (no contention)
    [Benchmark]
    [Arguments(100)]
    public Task AppendAsync_Sequential_100Operations()

    // Scenario 6: Concurrent appends - low contention (10 parallel, different aggregates)
    [Benchmark]
    public Task AppendAsync_Concurrent_LowContention()

    // Scenario 7: Concurrent appends - high contention (20 parallel, same aggregate)
    [Benchmark]
    public Task AppendAsync_Concurrent_HighContention()

    // Scenario 8: Measure semaphore wait time under contention
    [Benchmark]
    public Task AppendAsync_MeasureLockWaitTime()
}
```

**File:** `Benchmarks/7_Concurrency/ConcurrentEnrollmentBenchmarks.cs`

**Benchmark Methods:**

```csharp
[MemoryDiagnoser]
public class ConcurrentEnrollmentBenchmarks
{
    // Scenario 1: 10 concurrent enrollments to course with 10 spots (all succeed)
    [Benchmark]
    public Task EnrollStudents_10Concurrent_NoConflict()

    // Scenario 2: 20 concurrent enrollments to course with 10 spots (50% fail)
    [Benchmark]
    public Task EnrollStudents_20Concurrent_HalfFail()

    // Scenario 3: 100 concurrent enrollments to course with 10 spots (90% fail)
    [Benchmark]
    public Task EnrollStudents_100Concurrent_HighContentionRate()

    // Scenario 4: Independent operations (different courses, no contention)
    [Benchmark]
    public Task EnrollStudents_IndependentOperations()

    // Scenario 5: Measure retry overhead (forced retries)
    [Benchmark]
    public Task EnrollStudents_WithForcedRetries()

    // Scenario 6: Throughput test - max successful enrollments per second
    [Benchmark]
    public Task EnrollStudents_ThroughputTest_1000Students()
}
```

**Concurrency Test Scenarios (Based on DCB Spec):**

1. **Independent Operations** (No Conflict)
   - `RegisterStudentCommand` → `StudentRegisteredEvent`
   - `RenameCourseCommand` → `CourseRenamedEvent`
   - Both appending simultaneously, different aggregates
   - Expected: Both succeed with minimal wait time

2. **Competing Operations** (DCB Critical Test)
   - Two `EnrollStudentToCourseCommand` for students A and B
   - Course has 1 spot left (9 enrolled, capacity 10)
   - Both handlers see "9 enrolled" and decide to append
   - Expected: One succeeds, one fails with `ConcurrencyException`
   - Metrics: Lock wait time, retry count, total latency

3. **High Contention Stress Test**
   - 100 students try to enroll in course with 10 spots
   - Simulates "hot aggregate" scenario (e.g., popular course)
   - Metrics: Success rate, avg retry count, P95 latency

**Expected Insights:**
- AppendCondition validation overhead (< 5ms target)
- Semaphore lock contention impact on throughput
- Retry/backoff effectiveness
- Optimal concurrency levels before degradation
- ConcurrencyException rate under various contention levels

**Success Criteria:**
- < 5ms overhead for AppendCondition validation
- > 80% of max throughput maintained with 10 concurrent operations
- < 50ms P95 wait time for lock acquisition under moderate contention
- Linear degradation with increasing contention (no exponential slowdown)
- < 100KB memory allocation overhead per failed operation

**Key Metrics to Track:**
- **Lock Wait Time:** Time spent waiting for `_appendLock`
- **Validation Time:** Time spent in `ValidateAppendConditionAsync`
- **Retry Rate:** Percentage of operations requiring retries
- **Throughput:** Successful operations per second
- **Fairness:** Distribution of wait times (ensure no starvation)

---

## 🔧 Infrastructure Components

### 1. BenchmarkFixture.cs

**Responsibilities:**
- Initialize OpossumOptions with benchmark-specific configuration
- Set up isolated test directories
- Provide shared event store instances
- Clean up resources after benchmark runs

**Key Features:**
```csharp
public class BenchmarkFixture : IDisposable
{
    public IEventStore EventStore { get; }
    public IMediator Mediator { get; }
    public string BenchmarkRootPath { get; }
    
    // Initialize with specific data tier
    public void InitializeWithDataTier(DataTier tier)
    
    // Get pre-seeded event store
    public IEventStore GetSeededEventStore(int eventCount)
    
    // Cleanup isolated directories
    public void Dispose()
}

public enum DataTier
{
    Small,    // 1k events
    Medium,   // 10k events
    Large,    // 100k events
    Extreme   // 1M+ events
}
```

### 2. DataSeeder.cs

**Responsibilities:**
- Generate realistic baseline data sets
- Ensure reproducible data characteristics
- Support incremental seeding

**Key Features:**
```csharp
public class DataSeeder
{
    // Seed event store with realistic data
    public async Task SeedAsync(
        IEventStore eventStore, 
        int eventCount, 
        SeedOptions options)
    
    // Generate events with specific characteristics
    public SequencedEvent[] GenerateEvents(
        int count,
        int eventTypeCount = 10,
        int avgTagsPerEvent = 5,
        double zipfAlpha = 1.0) // Zipf distribution for event types
    
    // Create baseline data files (one-time setup)
    public async Task CreateBaselineDataAsync(string outputPath)
}

public class SeedOptions
{
    public int EventTypeCount { get; set; } = 10;
    public int AverageTagsPerEvent { get; set; } = 5;
    public double ZipfAlpha { get; set; } = 1.0; // Event type distribution
    public bool IncludeTemporalClustering { get; set; } = true;
}
```

### 3. TestDataGenerators.cs

**Responsibilities:**
- Generate test events, queries, commands
- Provide realistic domain objects
- Support parameterized data generation

**Key Features:**
```csharp
public static class TestDataGenerators
{
    public static SequencedEvent CreateEvent(
        string eventType, 
        params Tag[] tags)
    
    public static Query CreateQuery(
        string[] eventTypes = null, 
        Tag[] tags = null)
    
    public static EnrollStudentToCourseCommand CreateEnrollCommand()
    
    public static CourseEnlistmentAggregate CreateAggregate(
        int eventCount)
}
```

---

## 📈 Baseline Data Management

### Directory Structure

```
tests/Opossum.BenchmarkTests/BaselineData/
├── Small/
│   ├── CourseManagement/
│   │   ├── Events/
│   │   │   ├── 0000000001.json
│   │   │   └── ... (1,000 files)
│   │   ├── Indices/
│   │   │   ├── EventTypeIndex.json
│   │   │   └── TagIndex.json
│   │   └── ledger.json
│   └── metadata.json (event count, event types, tags)
├── Medium/
│   └── ... (10,000 events)
├── Large/          # .gitignored - generated locally
│   └── ... (100,000 events)
└── Extreme/        # .gitignored - generated locally
    └── ... (1,000,000+ events)
```

### Generation Script

**File:** `tests/Opossum.BenchmarkTests/Scripts/GenerateBaselineData.ps1`

```powershell
# Generate all baseline data tiers
param(
    [ValidateSet("Small", "Medium", "Large", "Extreme", "All")]
    [string]$Tier = "All"
)

# Run data seeder
dotnet run --project Opossum.BenchmarkTests `
    --configuration Release `
    -- --seed $Tier
```

### Metadata File Format

**File:** `BaselineData/Small/metadata.json`

```json
{
  "tier": "Small",
  "eventCount": 1000,
  "eventTypes": [
    {"name": "StudentEnrolledToCourseEvent", "count": 450},
    {"name": "CourseCreated", "count": 200},
    {"name": "StudentUnenrolledFromCourseEvent", "count": 150},
    ...
  ],
  "tags": [
    {"key": "courseId", "uniqueValues": 50},
    {"key": "studentId", "uniqueValues": 300},
    ...
  ],
  "generatedAt": "2024-12-15T10:30:00Z",
  "generatorVersion": "1.0",
  "seed": 42 // Random seed for reproducibility
}
```

### Git Strategy

**.gitignore:**
```gitignore
# Include small/medium baseline data
!tests/Opossum.BenchmarkTests/BaselineData/Small/**
!tests/Opossum.BenchmarkTests/BaselineData/Medium/**

# Exclude large/extreme data (too big for repo)
tests/Opossum.BenchmarkTests/BaselineData/Large/**
tests/Opossum.BenchmarkTests/BaselineData/Extreme/**

# Exclude generated benchmark results
tests/Opossum.BenchmarkTests/BenchmarkResults/**
```

**Rationale:**
- Small/Medium: < 10MB each, useful for quick local benchmarks
- Large/Extreme: > 100MB, generated locally as needed

---

## 🚀 Execution Strategy

### 1. Running Benchmarks

**Full Suite:**
```bash
cd tests/Opossum.BenchmarkTests
dotnet run -c Release --filter "*"
```

**Single Category:**
```bash
dotnet run -c Release --filter "*AppendBenchmarks*"
```

**Specific Benchmark:**
```bash
dotnet run -c Release --filter "*AppendAsync_SingleEvent_NoCondition"
```

**With Memory Profiler:**
```bash
dotnet run -c Release --filter "*" --memory-profiler
```

### 2. Benchmark Workflow

**Pre-Benchmark Checklist:**
1. ✅ Close unnecessary applications
2. ✅ Disable antivirus real-time scanning for benchmark directory
3. ✅ Ensure power plan set to "High Performance"
4. ✅ Verify no background tasks running (Windows Update, etc.)
5. ✅ Run in Release configuration
6. ✅ Baseline data already generated

**During Execution:**
- Do not interact with the system
- Allow benchmarks to complete uninterrupted
- Monitor disk/memory utilization for anomalies

**Post-Benchmark:**
1. Review results in BenchmarkResults/ directory
2. Compare against historical baselines
3. Document any significant deviations (> 10% change)
4. Tag results with Git commit SHA
5. Export to Markdown for documentation

### 3. Regression Detection

**Automated Baseline Comparison:**
```csharp
[Benchmark(Baseline = true)]
[BenchmarkCategory("Regression")]
public void Baseline_v1_0_0() { /* previous implementation */ }

[Benchmark]
[BenchmarkCategory("Regression")]
public void Current() { /* current implementation */ }
```

**Analysis Script:**
```powershell
# Compare current results against baseline
.\Scripts\CompareBenchmarks.ps1 `
    -BaselineFile "BenchmarkResults/baseline-v1.0.0.json" `
    -CurrentFile "BenchmarkResults/current.json" `
    -ThresholdPercent 10
```

**Regression Alert Criteria:**
- > 10% increase in execution time
- > 20% increase in memory allocations
- > 50% increase in GC collections
- Any new LOH allocations

---

## 📊 Result Analysis & Reporting

### 1. Key Metrics to Track

**Performance Metrics:**
- **Throughput:** Operations per second
- **Latency:** P50, P95, P99 percentiles
- **Variance:** Coefficient of variation (CV)
- **Scaling:** Time complexity validation

**Memory Metrics:**
- **Allocations:** Bytes per operation
- **GC Pressure:** Gen 0/1/2 collection counts
- **LOH:** Large object heap allocations
- **Heap Growth:** Total managed heap size

**I/O Metrics:**
- **Disk Throughput:** MB/s read/write
- **IOPS:** Operations per second
- **Latency:** File operation latency

### 2. Result Visualization

**BenchmarkDotNet Outputs:**
- HTML Report: Interactive tables with charts
- Markdown: GitHub-friendly format
- CSV: Excel/Power BI analysis
- JSON: Programmatic analysis

**Custom Dashboards:**
- Power BI dashboard with historical trends
- Excel pivot tables for metric aggregation
- Grafana for real-time monitoring (future)

### 3. Documentation Template

**File:** `BenchmarkResults/YYYY-MM-DD_BenchmarkReport.md`

```markdown
# Benchmark Report - [Date]

## Configuration
- **Commit:** [Git SHA]
- **Framework:** .NET 10
- **OS:** Windows 11 / Ubuntu 22.04
- **Hardware:** [CPU, RAM, Disk Type]

## Summary
- Total Benchmarks: X
- Regressions Detected: Y
- Improvements: Z

## Key Findings
1. [Finding 1 with metrics]
2. [Finding 2 with metrics]

## Detailed Results
[Embed BenchmarkDotNet tables]

## Recommendations
1. [Optimization opportunity 1]
2. [Optimization opportunity 2]
```

---

## 🎯 Success Metrics

### Performance Targets (Baseline Goals)

**Serialization:**
- Simple event: < 1ms, < 100KB allocations
- Event with 10 tags: < 2ms, < 150KB allocations
- Batch 100 events: < 100ms

**File Operations:**
- Single write: < 10ms (SSD)
- Batch read 100 events: < 50ms
- Random access 1000 events from 100k: < 500ms

**Indexing:**
- Add event to indices: < 1ms
- Query 100k event index: < 10ms
- Load 100k event index: < 100ms

**Event Store:**
- Append single event: < 20ms
- Append 100 events: < 500ms
- Read with complex query: < 100ms

**Mediator:**
- Handler invocation: < 100μs
- Handler discovery (cached): < 10μs

**End-to-End:**
- Simple command: < 50ms P95
- Complex command with DCB: < 200ms P95
- Aggregate rebuild (100 events): < 50ms

### Memory Targets

- No LOH allocations in steady state
- < 1MB per command processing
- Gen 2 collections: < 1 per 10,000 operations

---

## 🔄 Iteration & Refinement

### Phase 1: Initial Baseline (Week 1)
1. Implement BenchmarkDotNet infrastructure
2. Create baseline data generators
3. Run initial benchmark suite
4. Establish baseline metrics
5. Document results

### Phase 2: Optimization Cycle (Ongoing)
1. Identify hotspots from benchmark results
2. Implement optimization
3. Re-run benchmarks
4. Compare against baseline
5. Document improvements
6. Update baseline if > 20% improvement

### Phase 3: Regression Prevention (Continuous)
1. Run critical benchmarks before major refactors
2. Compare results against historical baselines
3. Investigate any regressions > 10%
4. Update baseline after approved changes

---

## 📚 References

### .NET Benchmarking Resources

1. **BenchmarkDotNet Documentation**
   - https://benchmarkdotnet.org/articles/overview.html

2. **Microsoft Performance Guidelines**
   - https://learn.microsoft.com/en-us/dotnet/framework/performance/

3. **.NET GC Internals**
   - https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/

4. **File System Performance**
   - https://learn.microsoft.com/en-us/windows-server/administration/performance-tuning/subsystem/storage-spaces-direct/

### Benchmarking Best Practices

1. **"Systems Performance" by Brendan Gregg**
   - Methodologies: USE, TSA, Workload Characterization

2. **"Writing High-Performance .NET Code" by Ben Watson**
   - Memory management, GC tuning, async patterns

3. **BenchmarkDotNet Blog**
   - https://benchmarkdotnet.org/blog/

4. **Event Sourcing Performance**
   - Greg Young's event store optimization talks
   - Event Store DB performance documentation

---

## ✅ Implementation Checklist

### Phase 1: Infrastructure Setup
- [ ] Add BenchmarkDotNet NuGet package
- [ ] Create BenchmarkConfiguration.cs
- [ ] Implement BenchmarkFixture.cs
- [ ] Create DataSeeder.cs
- [ ] Implement TestDataGenerators.cs
- [ ] Set up baseline data directory structure
- [ ] Create PowerShell generation scripts

### Phase 2: Micro-Benchmarks
- [ ] JsonSerializerBenchmarks.cs
- [ ] EventFileManagerBenchmarks.cs
- [ ] LedgerManagerBenchmarks.cs
- [ ] EventTypeIndexBenchmarks.cs
- [ ] TagIndexBenchmarks.cs
- [ ] MediatorInvocationBenchmarks.cs

### Phase 3: Macro-Benchmarks
- [ ] IndexManagerBenchmarks.cs
- [ ] AppendBenchmarks.cs
- [ ] ReadBenchmarks.cs
- [ ] QueryExecutionBenchmarks.cs

### Phase 4: End-to-End Benchmarks
- [ ] AggregateReconstructionBenchmarks.cs
- [ ] CommandProcessingBenchmarks.cs
- [ ] MultiContextBenchmarks.cs

### Phase 5: Baseline Data
- [ ] Generate Small tier (1k events)
- [ ] Generate Medium tier (10k events)
- [ ] Generate Large tier (100k events) - local only
- [ ] Generate Extreme tier (1M events) - local only
- [ ] Create metadata.json for each tier
- [ ] Commit Small/Medium to repo

### Phase 6: Analysis & Reporting
- [ ] Create result comparison scripts
- [ ] Set up Excel/Power BI templates
- [ ] Document baseline metrics
- [ ] Create regression detection workflow
- [ ] Establish alerting thresholds

---

## 🏁 Conclusion

This benchmark testing strategy provides a comprehensive framework for measuring and improving the performance of the Opossum Event Store. By combining industry best practices with Opossum-specific scenarios, we can:

1. **Establish empirical baselines** for all critical operations
2. **Identify optimization opportunities** through data-driven analysis
3. **Prevent performance regressions** during refactoring
4. **Validate scalability** at enterprise scale (100k+ events)
5. **Guide architectural decisions** with performance metrics

The strategy is designed to be:
- **Pragmatic:** Focus on real-world scenarios
- **Reproducible:** Baseline data ensures consistency
- **Scalable:** Tiered data supports various scales
- **Maintainable:** Clear structure and documentation

**Next Steps:**
1. Review and approve this strategy
2. Begin Phase 1 implementation (infrastructure)
3. Generate initial baseline data
4. Run first benchmark suite
5. Establish baseline metrics for ongoing comparison

---

**Document Status:** ✅ COMPLETE - Ready for Review  
**Estimated Implementation Effort:** 16-20 hours  
**Priority:** Medium (run after major refactors, not in CI/CD)
