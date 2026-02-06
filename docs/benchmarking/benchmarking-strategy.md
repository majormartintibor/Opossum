# Opossum Benchmarking Strategy

## Executive Summary

This document outlines a comprehensive benchmarking strategy for Opossum, a file system-based event store library. The strategy follows industry best practices for .NET benchmarking using BenchmarkDotNet and focuses on measuring performance-critical operations.

**Status:** Planning Phase  
**Branch:** feature/benchmark  
**Target Framework:** .NET 10  

---

## 1. Benchmarking Objectives

### Primary Goals
1. **Establish Performance Baselines** - Create reference metrics for core operations
2. **Identify Bottlenecks** - Find performance hotspots in critical paths
3. **Guide Optimization** - Provide data-driven insights for improvements
4. **Prevent Regressions** - Detect performance degradation in CI/CD
5. **Inform Configuration Decisions** - Help users choose optimal settings

### Non-Goals
- Comparing Opossum to other event stores (not apples-to-apples)
- Micro-optimizing every method (focus on critical paths)
- Testing non-performance aspects (correctness is for unit/integration tests)

---

## 2. Critical Performance Areas

Based on the Opossum architecture, these are the key areas requiring benchmarking:

### 2.1 Event Store Core Operations

#### AppendAsync Performance
**Why it matters:** Write throughput is critical for event sourcing systems.

**Scenarios to benchmark:**
- Single event append (baseline)
- Batch event append (2, 10, 50, 100 events)
- Append with DCB validation (AppendCondition)
- Append with vs without flush (`FlushEventsImmediately`)
- Concurrent append operations (contention analysis)

**Metrics:**
- Throughput (events/second)
- Latency (mean, P50, P95, P99)
- Allocation (memory per operation)

#### ReadAsync Performance
**Why it matters:** Query performance affects read model responsiveness.

**Scenarios to benchmark:**
- Query by event type (single type)
- Query by event type (multiple types - OR logic)
- Query by tags (single tag)
- Query by tags (multiple tags - AND logic)
- Query by event type AND tags (combined)
- Query with parallel reads enabled/disabled
- Query.All() (full scan)
- Descending vs Ascending order

**Variables:**
- Dataset size: 100, 1K, 10K, 100K events
- Tag cardinality (unique tag values)
- Index selectivity (how many events match)

**Metrics:**
- Query latency (mean, P50, P95, P99)
- Memory allocation
- Disk I/O operations (if measurable)

### 2.2 Serialization

#### JSON Serialization Performance
**Why it matters:** Serialization is on the critical path for every read/write.

**Scenarios to benchmark:**
- Serialize small event (~100 bytes JSON)
- Serialize medium event (~1KB JSON)
- Serialize large event (~10KB JSON)
- Deserialize small/medium/large events
- Events with/without tags
- Events with complex nested objects

**Metrics:**
- Throughput (ops/second)
- Memory allocation
- CPU usage

### 2.3 Indexing

#### Index Operations Performance
**Why it matters:** Index performance affects both write and query speed.

**Scenarios to benchmark:**
- Add event to type index
- Add event to tag index (1, 5, 10 tags)
- Lookup by event type (cold cache)
- Lookup by tag (cold cache)
- Combined lookup (type + tags)
- Index rebuild from events

**Variables:**
- Index size: 100, 1K, 10K, 100K events
- Number of unique types
- Number of unique tag keys
- Tag value distribution

**Metrics:**
- Operation latency
- Memory usage
- Index file size

### 2.4 Ledger Operations

#### Sequence Position Management
**Why it matters:** Ledger operations are on the critical path for AppendAsync.

**Scenarios to benchmark:**
- Get next sequence position (cold start)
- Get next sequence position (warm)
- Update sequence position (with flush)
- Update sequence position (without flush)

**Metrics:**
- Operation latency (mean, P95, P99)
- Flush overhead

### 2.5 File System Operations

#### File I/O Performance
**Why it matters:** File system is the underlying storage mechanism.

**Scenarios to benchmark:**
- Write single event file
- Write event file with flush
- Read single event file
- Read multiple event files (sequential)
- Read multiple event files (parallel)
- Directory enumeration (index scan)

**Metrics:**
- I/O throughput
- Latency
- System call overhead

### 2.6 Projection System

#### Projection Building Performance
**Why it matters:** Projection rebuilds can be time-consuming.

**Scenarios to benchmark:**
- Build projection from 100 events
- Build projection from 1K events
- Build projection from 10K events
- Multi-stream projection building
- Projection with tag filtering
- Concurrent projection updates

**Metrics:**
- Build time
- Throughput (events/second)
- Memory usage

### 2.7 Mediator Pattern

#### Message Dispatch Performance
**Why it matters:** Mediator is used for command/query handling.

**Scenarios to benchmark:**
- Simple command dispatch (no dependencies)
- Command dispatch with event store injection
- Query dispatch
- Handler discovery overhead (cold start)

**Metrics:**
- Dispatch latency
- Handler resolution time
- Memory allocation

### 2.8 Concurrency

#### Thread Contention Analysis
**Why it matters:** Event stores must handle concurrent access efficiently.

**Scenarios to benchmark:**
- Sequential appends (baseline)
- 2 concurrent threads appending
- 4, 8, 16, 32 concurrent threads appending
- Concurrent reads (no contention)
- Concurrent reads + writes (mixed workload)
- DCB validation under contention

**Metrics:**
- Throughput degradation vs thread count
- Lock contention time
- Successful DCB validations ratio

---

## 3. Benchmark Project Structure

### 3.1 Project Setup

```
tests/Opossum.BenchmarkTests/
├── Opossum.BenchmarkTests.csproj
├── GlobalUsings.cs
├── Program.cs                        // BenchmarkRunner entry point
├── BenchmarkConfig.cs                // Shared benchmark configuration
├── Helpers/
│   ├── BenchmarkDataGenerator.cs    // Generate test events/data
│   ├── TempFileSystemHelper.cs      // Temp directory management
│   └── EventFactory.cs              // Create benchmark events
├── Core/
│   ├── AppendBenchmarks.cs          // AppendAsync scenarios
│   ├── ReadBenchmarks.cs            // ReadAsync scenarios
│   ├── QueryBenchmarks.cs           // Complex query scenarios
│   └── ConcurrencyBenchmarks.cs     // Concurrent operations
├── Storage/
│   ├── SerializationBenchmarks.cs   // JSON serialization
│   ├── IndexBenchmarks.cs           // Index operations
│   ├── LedgerBenchmarks.cs          // Ledger operations
│   └── FileSystemBenchmarks.cs      // File I/O operations
├── Projections/
│   ├── ProjectionBuildBenchmarks.cs // Projection building
│   └── ProjectionQueryBenchmarks.cs // Projection queries
└── Mediator/
    └── MediatorBenchmarks.cs        // Mediator dispatch
```

### 3.2 Required NuGet Packages

Add to `Directory.Packages.props`:
```xml
<!-- Benchmarking -->
<PackageVersion Include="BenchmarkDotNet" Version="0.14.0" />
<PackageVersion Include="BenchmarkDotNet.Diagnostics.Windows" Version="0.14.0" />
```

Update `tests/Opossum.BenchmarkTests/Opossum.BenchmarkTests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <ServerGarbageCollection>true</ServerGarbageCollection>
    <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" />
    <PackageReference Include="BenchmarkDotNet.Diagnostics.Windows" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Opossum\Opossum.csproj" />
  </ItemGroup>
</Project>
```

---

## 4. Benchmark Configuration Standards

### 4.1 Shared Configuration

```csharp
// BenchmarkConfig.cs
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;

namespace Opossum.BenchmarkTests;

public class OpossumBenchmarkConfig : ManualConfig
{
    public OpossumBenchmarkConfig()
    {
        // Job configuration
        AddJob(Job.Default
            .WithWarmupCount(3)           // 3 warmup iterations
            .WithIterationCount(10)       // 10 measurement iterations
            .WithInvocationCount(1)       // Auto-tuned
            .WithMaxIterationCount(15)    // Max iterations
            .WithToolchain(InProcessEmitToolchain.Instance)); // In-process for faster execution

        // Diagnostics
        AddDiagnoser(MemoryDiagnoser.Default);        // Memory allocations
        AddDiagnoser(ThreadingDiagnoser.Default);     // Thread pool info
        
        // Windows-specific (optional, only on Windows)
        // AddDiagnoser(new EtwProfiler());           // ETW profiling (requires admin)

        // Columns to display
        AddColumn(StatisticColumn.Mean);
        AddColumn(StatisticColumn.StdDev);
        AddColumn(StatisticColumn.Median);
        AddColumn(StatisticColumn.P95);
        AddColumn(StatisticColumn.P99);
        AddColumn(RankColumn.Arabic);
        AddColumn(BaselineColumn.Default);

        // Exporters
        AddExporter(MarkdownExporter.GitHub);         // GitHub-flavored markdown
        AddExporter(CsvMeasurementsExporter.Default); // Raw CSV data
        AddExporter(HtmlExporter.Default);            // HTML report

        // Options
        WithOptions(ConfigOptions.DisableOptimizationsValidator); // Allow debug builds for development
    }
}
```

### 4.2 Benchmark Attributes

Standard attributes to use:

```csharp
[Config(typeof(OpossumBenchmarkConfig))]
[MemoryDiagnoser]
[ThreadingDiagnoser]
public class MyBenchmarks
{
    // Use [Params] for scenario variations
    [Params(1, 10, 100)]
    public int EventCount;

    [Params(true, false)]
    public bool FlushImmediately;

    // Use [GlobalSetup] for expensive one-time setup
    [GlobalSetup]
    public void Setup()
    {
        // Create temp directories, seed data, etc.
    }

    // Use [IterationSetup] for per-iteration setup (included in measurements)
    [IterationSetup]
    public void IterationSetup()
    {
        // Reset state between iterations
    }

    // Use [Benchmark] for the operation to measure
    [Benchmark(Baseline = true)]
    public void BaselineOperation()
    {
        // Reference implementation
    }

    [Benchmark]
    public void OptimizedOperation()
    {
        // Optimized version
    }

    // Use [IterationCleanup] for cleanup after each iteration
    [IterationCleanup]
    public void IterationCleanup()
    {
        // Clean up temp files, etc.
    }

    // Use [GlobalCleanup] for final cleanup
    [GlobalCleanup]
    public void Cleanup()
    {
        // Delete temp directories, dispose resources
    }
}
```

---

## 5. Best Practices

### 5.1 Benchmark Writing Guidelines

1. **Isolate What You Measure**
   - Setup should NOT be in the benchmark method
   - Use `[GlobalSetup]` for expensive initialization
   - Use `[IterationSetup]` only if state must be reset per iteration

2. **Avoid Dead Code Elimination**
   ```csharp
   // ❌ Wrong - compiler might optimize away
   [Benchmark]
   public void Wrong()
   {
       var result = _eventStore.ReadAsync(query, null);
   }

   // ✅ Correct - return or consume the result
   [Benchmark]
   public async Task<SequencedEvent[]> Correct()
   {
       return await _eventStore.ReadAsync(query, null);
   }
   ```

3. **Use Realistic Data**
   - Events should resemble production data
   - Use realistic tag distributions
   - Include various event sizes

4. **Test on Real File System**
   - Use actual temp directories, not in-memory mocks
   - Clean up properly to avoid pollution
   - Consider SSD vs HDD differences

5. **Handle Async Properly**
   ```csharp
   // ✅ Correct - return Task
   [Benchmark]
   public Task BenchmarkAsync()
   {
       return _eventStore.AppendAsync(events, null);
   }

   // ❌ Wrong - blocking on async
   [Benchmark]
   public void WrongAsync()
   {
       _eventStore.AppendAsync(events, null).GetAwaiter().GetResult();
   }
   ```

6. **Control Parallelism**
   - Benchmark sequential operations separately from parallel
   - Use `Parallel.For` or `Task.WhenAll` for concurrency tests
   - Document thread counts clearly

7. **Memory Allocation Matters**
   - Always use `[MemoryDiagnoser]`
   - Reduce allocations in hot paths
   - Consider object pooling if allocations are high

### 5.2 Data Generation Standards

```csharp
public static class BenchmarkDataGenerator
{
    public static SequencedEvent[] GenerateEvents(int count, int tagCount = 3)
    {
        var events = new SequencedEvent[count];
        for (int i = 0; i < count; i++)
        {
            events[i] = new StudentRegisteredEvent(
                    Guid.NewGuid(),
                    $"FirstName{i}",
                    $"LastName{i}",
                    $"user{i}@example.com")
                .ToDomainEvent()
                .WithTag("studentId", Guid.NewGuid().ToString())
                .WithTag("batch", (i / 100).ToString())
                .WithTimestamp(DateTimeOffset.UtcNow);
        }
        return events;
    }

    public static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"OpossumBenchmark_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
```

### 5.3 Cleanup Standards

```csharp
[GlobalCleanup]
public void Cleanup()
{
    try
    {
        if (Directory.Exists(_tempPath))
        {
            // Retry deletion (files might be locked)
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    Directory.Delete(_tempPath, recursive: true);
                    break;
                }
                catch (IOException)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }
    catch
    {
        // Log but don't fail benchmark
        Console.WriteLine($"Warning: Could not clean up {_tempPath}");
    }
}
```

---

## 6. Execution Strategy

### 6.1 Development Workflow

```bash
# Run all benchmarks (slow - for CI/release)
dotnet run -c Release --project tests/Opossum.BenchmarkTests

# Run specific benchmark class (faster)
dotnet run -c Release --project tests/Opossum.BenchmarkTests --filter *AppendBenchmarks*

# Run specific method
dotnet run -c Release --project tests/Opossum.BenchmarkTests --filter *AppendBenchmarks.SingleEventAppend*

# Dry run (validate without full execution)
dotnet run -c Release --project tests/Opossum.BenchmarkTests --job dry

# Quick run (1 warmup, 1 iteration - for testing)
dotnet run -c Release --project tests/Opossum.BenchmarkTests --job short
```

### 6.2 CI/CD Integration

**Do NOT run full benchmarks on every commit** - they are too slow.

**Recommended approach:**
- Run benchmarks on:
  - Release branches
  - Manual trigger
  - Scheduled nightly builds
  - Performance-related PRs (tagged)

**GitHub Actions example:**
```yaml
name: Performance Benchmarks

on:
  workflow_dispatch: # Manual trigger
  schedule:
    - cron: '0 2 * * *' # 2 AM daily

jobs:
  benchmark:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - name: Run Benchmarks
        run: dotnet run -c Release --project tests/Opossum.BenchmarkTests
      - name: Upload Results
        uses: actions/upload-artifact@v4
        with:
          name: benchmark-results
          path: BenchmarkDotNet.Artifacts/results/
```

### 6.3 Result Storage

- Commit baseline results to `docs/benchmarking/baseline-results/`
- Track trends over time
- Compare before/after optimization PRs

---

## 7. Performance Targets

### 7.1 Initial Targets (To Be Validated)

These are educated guesses - real benchmarks will establish actual baselines.

| Operation | Target | Rationale |
|-----------|--------|-----------|
| Single event append (no flush) | < 1ms P95 | In-memory operations should be fast |
| Single event append (with flush) | < 10ms P95 | SSD flush overhead ~5ms |
| Batch append (100 events, no flush) | < 50ms P95 | ~0.5ms per event |
| Query by tag (1K events) | < 5ms P95 | Index lookup + file reads |
| Query by tag (10K events) | < 50ms P95 | Scales linearly |
| Projection build (10K events) | < 1s | Reasonable rebuild time |
| Mediator dispatch | < 100μs P95 | Should be negligible overhead |

### 7.2 Regression Detection

**Consider it a regression if:**
- Latency increases by > 10% for core operations
- Memory allocation increases by > 20%
- Throughput decreases by > 15%

**Exception:** Intentional trade-offs (e.g., adding flush for durability)

---

## 8. Documentation Requirements

### 8.1 Benchmark Code Comments

Every benchmark class should have:
```csharp
/// <summary>
/// Benchmarks for [Component Name].
/// 
/// Measures:
/// - [Metric 1]
/// - [Metric 2]
/// 
/// Scenarios:
/// - [Scenario description]
/// 
/// Key Findings (from latest run):
/// - [Performance insight]
/// 
/// Last Updated: [Date]
/// </summary>
[Config(typeof(OpossumBenchmarkConfig))]
public class MyBenchmarks
{
    // ...
}
```

### 8.2 Result Documentation

After each benchmark run, document in `docs/benchmarking/results/`:

```markdown
# Benchmark Results - [Date]

## Environment
- OS: Windows 11
- CPU: Intel i7-12700K
- RAM: 32GB DDR4
- Storage: Samsung 980 Pro NVMe SSD
- .NET: 10.0.x

## Key Findings

### AppendAsync Performance
- Single event (no flush): **0.8ms** (P95)
- Single event (with flush): **6.2ms** (P95)
- **Insight:** Flush adds ~5ms overhead as expected for SSD

### ReadAsync Performance
- Query 1K events by tag: **4.1ms** (P95)
- Query 10K events by tag: **42ms** (P95)
- **Insight:** Scales linearly, index is efficient

## Recommendations
1. Default FlushImmediately=true is acceptable for most use cases
2. Consider batching for high-throughput scenarios
3. Tag indexing performs well up to 10K events
```

---

## 9. Implementation Phases

### Phase 1: Foundation (Week 1)
- ✅ Set up BenchmarkDotNet project
- ✅ Create shared configuration
- ✅ Implement data generators
- ✅ Implement temp file system helpers
- ✅ Write first benchmark (AppendAsync baseline)
- ✅ Validate benchmark execution
- ✅ Document baseline results

### Phase 2: Core Operations (Week 2)
- Implement AppendBenchmarks (all scenarios)
- Implement ReadBenchmarks (all scenarios)
- Implement QueryBenchmarks (complex queries)
- Document findings

### Phase 3: Storage Layer (Week 3)
- Implement SerializationBenchmarks
- Implement IndexBenchmarks
- Implement LedgerBenchmarks
- Implement FileSystemBenchmarks
- Document findings

### Phase 4: Advanced Features (Week 4)
- Implement ProjectionBuildBenchmarks
- Implement ProjectionQueryBenchmarks
- Implement MediatorBenchmarks
- Implement ConcurrencyBenchmarks
- Document findings

### Phase 5: Analysis & Optimization (Week 5+)
- Analyze results
- Identify optimization opportunities
- Implement optimizations
- Re-benchmark
- Update documentation

---

## 10. Success Criteria

✅ **Benchmarking strategy is successful when:**

1. All critical operations have baseline metrics
2. Performance bottlenecks are identified
3. Results are reproducible (±5% variance)
4. Documentation is comprehensive
5. Benchmarks run in CI/CD
6. Team uses benchmarks to guide optimization decisions

---

## 11. References

### BenchmarkDotNet Resources
- [Official Documentation](https://benchmarkdotnet.org/)
- [Best Practices](https://benchmarkdotnet.org/articles/guides/good-practices.html)
- [Memory Diagnoser Guide](https://benchmarkdotnet.org/articles/features/memory-diagnoser.html)

### .NET Performance Resources
- [Performance Best Practices](https://learn.microsoft.com/en-us/dotnet/core/performance/)
- [Async Programming](https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/)
- [ConfigureAwait FAQ](https://devblogs.microsoft.com/dotnet/configureawait-faq/)

### Event Sourcing Performance
- [Event Store Performance](https://www.eventstore.com/blog/performance)
- [LMAX Disruptor Pattern](https://lmax-exchange.github.io/disruptor/)

---

## Appendix A: Sample Benchmark Template

```csharp
using Opossum.Core;
using BenchmarkDotNet.Attributes;

namespace Opossum.BenchmarkTests.Core;

/// <summary>
/// Template for creating new benchmarks.
/// Copy this file and customize for your scenario.
/// </summary>
[Config(typeof(OpossumBenchmarkConfig))]
[MemoryDiagnoser]
public class TemplateBenchmarks
{
    private IEventStore _eventStore = null!;
    private string _tempPath = null!;
    private SequencedEvent[] _events = null!;

    [Params(1, 10, 100)]
    public int EventCount;

    [GlobalSetup]
    public void Setup()
    {
        _tempPath = BenchmarkDataGenerator.CreateTempDirectory();
        
        var options = new OpossumOptions
        {
            RootPath = _tempPath,
            FlushEventsImmediately = false
        };
        options.AddContext("BenchmarkContext");

        _eventStore = new FileSystemEventStore(options);
        _events = BenchmarkDataGenerator.GenerateEvents(EventCount);
    }

    [Benchmark(Baseline = true)]
    public async Task BaselineOperation()
    {
        await _eventStore.AppendAsync(_events, null).ConfigureAwait(false);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempPath))
        {
            Directory.Delete(_tempPath, recursive: true);
        }
    }
}
```

---

**Document Owner:** AI Assistant  
**Last Updated:** 2025-01-28  
**Status:** Planning Phase  
**Next Review:** After Phase 1 Implementation
