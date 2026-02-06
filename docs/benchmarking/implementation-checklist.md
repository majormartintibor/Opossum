# Opossum Benchmarking - Implementation Checklist

## Phase 1: Foundation ✅ (Current Phase)

### 1.1 Project Setup
- [ ] Update `Directory.Packages.props` with BenchmarkDotNet packages
- [ ] Update `Opossum.BenchmarkTests.csproj` configuration
- [ ] Remove test framework references (xUnit, etc.)
- [ ] Add project reference to `src/Opossum`
- [ ] Configure Release build settings (ServerGC, etc.)

### 1.2 Core Infrastructure
- [ ] Create `GlobalUsings.cs` with BenchmarkDotNet usings
- [ ] Create `Program.cs` with BenchmarkRunner
- [ ] Create `BenchmarkConfig.cs` with shared configuration
- [ ] Test basic benchmark execution

### 1.3 Helper Classes
- [ ] Create `Helpers/BenchmarkDataGenerator.cs`
  - [ ] `GenerateEvents(count, tagCount)` method
  - [ ] `GenerateQuery(...)` methods
  - [ ] `CreateTempDirectory()` method
  - [ ] `GetRandomTags()` method
- [ ] Create `Helpers/TempFileSystemHelper.cs`
  - [ ] Auto-cleanup functionality
  - [ ] Retry logic for locked files
- [ ] Create `Helpers/EventFactory.cs`
  - [ ] Small event generator (~100 bytes)
  - [ ] Medium event generator (~1KB)
  - [ ] Large event generator (~10KB)

### 1.4 First Benchmark
- [ ] Create `Core/AppendBenchmarks.cs`
- [ ] Implement `SingleEventAppend_NoFlush()` benchmark
- [ ] Implement `SingleEventAppend_WithFlush()` benchmark
- [ ] Validate benchmark runs successfully
- [ ] Document baseline results

### 1.5 Documentation
- [ ] Create `docs/benchmarking/results/` folder
- [ ] Document first benchmark results
- [ ] Update `benchmarking-strategy.md` with actual findings

---

## Phase 2: Core Operations

### 2.1 AppendBenchmarks.cs
- [ ] Single event append (baseline)
- [ ] Batch append - 2 events
- [ ] Batch append - 10 events
- [ ] Batch append - 50 events
- [ ] Batch append - 100 events
- [ ] Append with DCB validation (FailIfEventsMatch)
- [ ] Append with DCB validation (FailIfNoEventsMatch)
- [ ] Document findings

### 2.2 ReadBenchmarks.cs
- [ ] Query by single event type (100 events)
- [ ] Query by single event type (1K events)
- [ ] Query by single event type (10K events)
- [ ] Query by multiple event types (OR logic)
- [ ] Query by single tag (100 events)
- [ ] Query by single tag (1K events)
- [ ] Query by single tag (10K events)
- [ ] Query by multiple tags (AND logic)
- [ ] Query.All() - 100 events
- [ ] Query.All() - 1K events
- [ ] Query.All() - 10K events
- [ ] Document findings

### 2.3 QueryBenchmarks.cs
- [ ] Complex query: EventType + Single Tag
- [ ] Complex query: Multiple EventTypes + Multiple Tags
- [ ] Query with Descending order
- [ ] Query with high selectivity (few matches)
- [ ] Query with low selectivity (many matches)
- [ ] Multiple QueryItems (OR between items)
- [ ] Document findings

---

## Phase 3: Storage Layer

### 3.1 SerializationBenchmarks.cs
- [ ] Serialize small event (~100 bytes)
- [ ] Serialize medium event (~1KB)
- [ ] Serialize large event (~10KB)
- [ ] Deserialize small event
- [ ] Deserialize medium event
- [ ] Deserialize large event
- [ ] Serialize event with 0 tags
- [ ] Serialize event with 5 tags
- [ ] Serialize event with 10 tags
- [ ] Document findings

### 3.2 IndexBenchmarks.cs
- [ ] Add event to type index (cold)
- [ ] Add event to type index (warm)
- [ ] Add event to tag index - 1 tag
- [ ] Add event to tag index - 5 tags
- [ ] Add event to tag index - 10 tags
- [ ] Lookup by event type (100 events indexed)
- [ ] Lookup by event type (1K events indexed)
- [ ] Lookup by event type (10K events indexed)
- [ ] Lookup by tag (100 events indexed)
- [ ] Lookup by tag (1K events indexed)
- [ ] Lookup by tag (10K events indexed)
- [ ] Combined lookup (type + tags)
- [ ] Index file read performance
- [ ] Index file write performance
- [ ] Document findings

### 3.3 LedgerBenchmarks.cs
- [ ] Get next sequence position (cold start)
- [ ] Get next sequence position (warm)
- [ ] Update sequence position (no flush)
- [ ] Update sequence position (with flush)
- [ ] Read sequence position
- [ ] Sequential position allocation (10 calls)
- [ ] Sequential position allocation (100 calls)
- [ ] Document findings

### 3.4 FileSystemBenchmarks.cs
- [ ] Write single event file (no flush)
- [ ] Write single event file (with flush)
- [ ] Read single event file
- [ ] Read 10 event files sequentially
- [ ] Read 10 event files in parallel
- [ ] Read 100 event files sequentially
- [ ] Read 100 event files in parallel
- [ ] Directory enumeration (100 files)
- [ ] Directory enumeration (1K files)
- [ ] Directory enumeration (10K files)
- [ ] Document findings

---

## Phase 4: Advanced Features

### 4.1 ProjectionBuildBenchmarks.cs
- [ ] Build projection from 100 events
- [ ] Build projection from 1K events
- [ ] Build projection from 10K events
- [ ] Build multi-stream projection (100 events)
- [ ] Build multi-stream projection (1K events)
- [ ] Build projection with tag filtering
- [ ] Projection checkpoint save
- [ ] Projection checkpoint load
- [ ] Document findings

### 4.2 ProjectionQueryBenchmarks.cs
- [ ] Query projection by ID (100 projections)
- [ ] Query projection by ID (1K projections)
- [ ] Query projection by tag (100 projections)
- [ ] Query projection by tag (1K projections)
- [ ] List all projections (100 projections)
- [ ] List all projections (1K projections)
- [ ] Document findings

### 4.3 MediatorBenchmarks.cs
- [ ] Dispatch simple command (no dependencies)
- [ ] Dispatch command with IEventStore injection
- [ ] Dispatch query
- [ ] Handler discovery (cold start)
- [ ] Handler discovery (cached)
- [ ] Sequential dispatch (10 commands)
- [ ] Sequential dispatch (100 commands)
- [ ] Document findings

### 4.4 ConcurrencyBenchmarks.cs
- [ ] Sequential appends (1 thread, baseline)
- [ ] Concurrent appends (2 threads)
- [ ] Concurrent appends (4 threads)
- [ ] Concurrent appends (8 threads)
- [ ] Concurrent appends (16 threads)
- [ ] Concurrent reads (2 threads, no writes)
- [ ] Concurrent reads (4 threads, no writes)
- [ ] Mixed workload (50% reads, 50% writes, 4 threads)
- [ ] DCB validation under contention (2 threads)
- [ ] DCB validation under contention (4 threads)
- [ ] Lock contention analysis
- [ ] Document findings

---

## Phase 5: Analysis & Optimization

### 5.1 Analysis
- [ ] Identify top 3 bottlenecks
- [ ] Calculate throughput metrics (ops/sec)
- [ ] Analyze memory allocation patterns
- [ ] Compare flush vs no-flush overhead
- [ ] Analyze index efficiency
- [ ] Review query performance scaling

### 5.2 Optimization Candidates
- [ ] Batch write optimization
- [ ] Index caching strategy
- [ ] Parallel read optimization
- [ ] Serialization optimization
- [ ] Lock granularity improvement
- [ ] File I/O optimization

### 5.3 Re-Benchmarking
- [ ] Re-run benchmarks after each optimization
- [ ] Compare before/after results
- [ ] Document performance improvements
- [ ] Update baseline metrics

### 5.4 Documentation
- [ ] Final performance report
- [ ] Optimization recommendations
- [ ] Configuration guidance for users
- [ ] Known performance limitations

---

## Phase 6: CI/CD Integration

### 6.1 GitHub Actions
- [ ] Create `.github/workflows/benchmarks.yml`
- [ ] Configure manual trigger
- [ ] Configure scheduled runs (nightly)
- [ ] Add artifact upload for results
- [ ] Test workflow execution

### 6.2 Result Tracking
- [ ] Create `docs/benchmarking/baseline-results/` folder
- [ ] Commit baseline results
- [ ] Create result comparison script (optional)
- [ ] Document regression detection process

---

## Validation Checklist

Before marking a phase complete:

- [ ] All benchmarks compile without errors
- [ ] All benchmarks run successfully in Release mode
- [ ] Results are documented in `docs/benchmarking/results/`
- [ ] Key findings are summarized
- [ ] Code follows copilot-instructions (usings, namespaces, etc.)
- [ ] Cleanup methods properly delete temp files
- [ ] Memory diagnoser shows reasonable allocation
- [ ] No false positives (dead code elimination)

---

## Quick Commands Reference

```bash
# Run all benchmarks
dotnet run -c Release --project tests/Opossum.BenchmarkTests

# Run specific class
dotnet run -c Release --project tests/Opossum.BenchmarkTests --filter *AppendBenchmarks*

# Run specific method
dotnet run -c Release --project tests/Opossum.BenchmarkTests --filter *AppendBenchmarks.SingleEventAppend*

# Dry run (validate)
dotnet run -c Release --project tests/Opossum.BenchmarkTests --job dry

# Short run (quick test)
dotnet run -c Release --project tests/Opossum.BenchmarkTests --job short

# Export to specific format
dotnet run -c Release --project tests/Opossum.BenchmarkTests --exporters json,html,csv
```

---

## Notes

- Always run benchmarks in **Release** mode
- Close unnecessary applications before benchmarking
- Run on AC power (not battery) for consistent results
- Document hardware specs with each result set
- Warm up the SSD before critical benchmarks
- Use consistent temp directory locations

---

**Status:** Ready to start Phase 1  
**Next Action:** Update project files and create infrastructure  
**Estimated Time:** Phase 1 = 1-2 days, Total = 4-6 weeks
