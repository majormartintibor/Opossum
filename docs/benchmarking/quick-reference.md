# Opossum Benchmarking - Quick Reference

## 📊 What We're Benchmarking

```
┌─────────────────────────────────────────────────────────────────┐
│                    Opossum Event Store                           │
│                                                                   │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐          │
│  │  AppendAsync │  │  ReadAsync   │  │  Projections │          │
│  │              │  │              │  │              │          │
│  │  Write Path  │  │  Query Path  │  │  Read Models │          │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘          │
│         │                  │                  │                  │
│         └──────────────────┴──────────────────┘                  │
│                            │                                     │
│  ┌─────────────────────────┴─────────────────────────┐          │
│  │            Storage Layer (File System)            │          │
│  │                                                    │          │
│  │  ├─ EventFileManager (event files)                │          │
│  │  ├─ IndexManager (type/tag indices)               │          │
│  │  ├─ LedgerManager (sequence positions)            │          │
│  │  └─ JsonEventSerializer (serialization)           │          │
│  └────────────────────────────────────────────────────┘          │
└─────────────────────────────────────────────────────────────────┘
```

---

## 🎯 Critical Performance Paths

### 1. **Write Path** (AppendAsync)
```
User → AppendAsync
  ↓
Validate Input
  ↓
Check DCB Condition (optional) ────→ Query Index ⚠️ HOT PATH
  ↓
Allocate Sequence Position ────────→ Read/Write Ledger ⚠️ HOT PATH
  ↓
Serialize Events ───────────────────→ JSON Serialization ⚠️ HOT PATH
  ↓
Write Event Files ──────────────────→ File I/O + Flush ⚠️ HOT PATH
  ↓
Update Indices ─────────────────────→ Index Write ⚠️ HOT PATH
  ↓
Update Ledger ──────────────────────→ Ledger Write + Flush ⚠️ HOT PATH
  ↓
Return Success
```

**Benchmark Priority:** 🔥 CRITICAL

**Key Metrics:**
- Latency: Mean, P95, P99
- Throughput: Events/second
- Memory: Allocations per append
- Concurrency: Lock contention

---

### 2. **Read Path** (ReadAsync)
```
User → ReadAsync(query)
  ↓
Parse Query
  ↓
For each QueryItem:
  ↓
  Get Positions by EventType ────→ Index Lookup ⚠️ HOT PATH
  ↓
  Get Positions by Tags ─────────→ Index Lookup ⚠️ HOT PATH
  ↓
  Intersect/Union positions
  ↓
Sort Positions
  ↓
Read Event Files ──────────────────→ File I/O (parallel) ⚠️ HOT PATH
  ↓
Deserialize Events ────────────────→ JSON Deserialization ⚠️ HOT PATH
  ↓
Apply ReadOptions (order)
  ↓
Return Events
```

**Benchmark Priority:** 🔥 CRITICAL

**Key Metrics:**
- Query latency vs dataset size
- Index efficiency (selectivity)
- Parallel read speedup
- Memory per query

---

### 3. **Projection Build Path**
```
ProjectionManager → BuildProjection
  ↓
Load Checkpoint
  ↓
Query Events (by tags/types) ────→ ReadAsync ⚠️ HOT PATH
  ↓
For each event:
  ↓
  Apply to projection
  ↓
  Update checkpoint
  ↓
Save Projection ────────────────────→ Serialization + File I/O
  ↓
Save Checkpoint
```

**Benchmark Priority:** 🟡 MEDIUM

**Key Metrics:**
- Build time (total)
- Throughput (events/second)
- Memory usage during build

---

## 📈 Benchmark Scenarios Matrix

| Category | Scenario | Variables | Priority |
|----------|----------|-----------|----------|
| **Append** | Single event | flush on/off | 🔥 |
| | Batch (10, 100, 1000) | flush on/off | 🔥 |
| | With DCB validation | condition complexity | 🔥 |
| | Concurrent (2, 4, 8 threads) | thread count | 🔥 |
| **Read** | By event type | dataset size (100, 1K, 10K) | 🔥 |
| | By tags (AND) | tag count (1, 3, 5) | 🔥 |
| | By tags + type | dataset size | 🔥 |
| | Query.All() | dataset size | 🟡 |
| | Parallel reads | enabled/disabled | 🔥 |
| **Serialization** | Serialize event | size (small, medium, large) | 🟡 |
| | Deserialize event | size (small, medium, large) | 🟡 |
| | With tags | tag count (0, 5, 10) | 🟡 |
| **Index** | Add to type index | index size (100, 1K, 10K) | 🟡 |
| | Add to tag index | tags per event (1, 5, 10) | 🟡 |
| | Lookup by type | index size | 🔥 |
| | Lookup by tag | index size | 🔥 |
| **Ledger** | Get next position | cold/warm | 🟡 |
| | Update position | flush on/off | 🟡 |
| **Projections** | Build projection | event count (100, 1K, 10K) | 🟡 |
| | Query projection | projection count | 🟢 |
| **Mediator** | Dispatch command | handler complexity | 🟢 |
| **Concurrency** | Parallel appends | thread count (2, 4, 8, 16) | 🔥 |
| | Mixed workload | read/write ratio | 🔥 |

**Legend:**  
🔥 Critical - Always benchmark  
🟡 Medium - Benchmark for optimization  
🟢 Low - Benchmark for completeness  

---

## 🎭 Sample Benchmark Patterns

### Pattern 1: Simple Operation Benchmark
```csharp
[Config(typeof(OpossumBenchmarkConfig))]
[MemoryDiagnoser]
public class AppendBenchmarks
{
    private IEventStore _eventStore = null!;
    private SequencedEvent[] _events = null!;

    [GlobalSetup]
    public void Setup()
    {
        // One-time expensive setup
        _eventStore = CreateEventStore();
        _events = GenerateEvents(100);
    }

    [Benchmark]
    public async Task SingleEventAppend()
    {
        // Only measure this operation
        await _eventStore.AppendAsync([_events[0]], null);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // Clean up resources
        DeleteTempFiles();
    }
}
```

### Pattern 2: Parameterized Benchmark
```csharp
[Config(typeof(OpossumBenchmarkConfig))]
[MemoryDiagnoser]
public class ReadBenchmarks
{
    [Params(100, 1_000, 10_000)]
    public int EventCount;

    [Params(true, false)]
    public bool ParallelReads;

    private IEventStore _eventStore = null!;
    private Query _query = null!;

    [GlobalSetup]
    public void Setup()
    {
        _eventStore = CreateEventStore();
        SeedEvents(EventCount);
        _query = CreateQuery();
    }

    [Benchmark]
    public async Task<SequencedEvent[]> QueryByTag()
    {
        return await _eventStore.ReadAsync(_query, null);
    }
}
```

### Pattern 3: Baseline Comparison
```csharp
[Config(typeof(OpossumBenchmarkConfig))]
[MemoryDiagnoser]
public class FlushBenchmarks
{
    private IEventStore _noFlush = null!;
    private IEventStore _withFlush = null!;
    private SequencedEvent[] _events = null!;

    [GlobalSetup]
    public void Setup()
    {
        _noFlush = CreateEventStore(flush: false);
        _withFlush = CreateEventStore(flush: true);
        _events = GenerateEvents(1);
    }

    [Benchmark(Baseline = true)]
    public async Task AppendNoFlush()
    {
        await _noFlush.AppendAsync(_events, null);
    }

    [Benchmark]
    public async Task AppendWithFlush()
    {
        await _withFlush.AppendAsync(_events, null);
    }
}
```

### Pattern 4: Concurrency Benchmark
```csharp
[Config(typeof(OpossumBenchmarkConfig))]
[MemoryDiagnoser]
[ThreadingDiagnoser]
public class ConcurrencyBenchmarks
{
    [Params(1, 2, 4, 8)]
    public int ThreadCount;

    private IEventStore _eventStore = null!;

    [GlobalSetup]
    public void Setup()
    {
        _eventStore = CreateEventStore();
    }

    [Benchmark]
    public async Task ConcurrentAppends()
    {
        var tasks = new Task[ThreadCount];
        for (int i = 0; i < ThreadCount; i++)
        {
            var events = GenerateEvents(10);
            tasks[i] = _eventStore.AppendAsync(events, null);
        }
        await Task.WhenAll(tasks);
    }
}
```

---

## 📊 Expected Output Format

### Console Output (Summary)
```
BenchmarkDotNet v0.14.0, Windows 11
Intel Core i7-12700K CPU 3.60GHz, 1 CPU, 20 logical and 12 physical cores
.NET SDK 10.0.0

| Method               | EventCount | Mean       | StdDev   | P95        | Allocated |
|--------------------- |----------- |-----------:|---------:|-----------:|----------:|
| SingleEventAppend    | 1          | 0.823 ms   | 0.045 ms | 0.901 ms   | 2.1 KB    |
| BatchAppend          | 10         | 6.234 ms   | 0.312 ms | 6.789 ms   | 18.7 KB   |
| BatchAppend          | 100        | 58.123 ms  | 2.451 ms | 62.345 ms  | 172.3 KB  |
```

### Exported Files
```
BenchmarkDotNet.Artifacts/
├── results/
│   ├── AppendBenchmarks-report.html
│   ├── AppendBenchmarks-report.md
│   ├── AppendBenchmarks-measurements.csv
│   └── AppendBenchmarks-report.json
└── logs/
    └── AppendBenchmarks.log
```

---

## 🔍 Key Metrics Explained

### Latency Metrics
- **Mean:** Average time per operation
- **StdDev:** Standard deviation (consistency)
- **Median (P50):** 50% of operations finish faster
- **P95:** 95% of operations finish faster (good SLA target)
- **P99:** 99% of operations finish faster (outlier detection)

### Throughput Metrics
- **ops/sec:** Operations per second
- **events/sec:** Events processed per second (for batch operations)

### Memory Metrics
- **Allocated:** Total memory allocated per operation
- **Gen0/1/2:** Garbage collection impact

### Threading Metrics
- **Completed Work Items:** Work done by thread pool
- **Lock Contentions:** How often threads waited for locks

---

## ⚠️ Common Pitfalls

### ❌ DON'T
```csharp
// 1. Dead code elimination
[Benchmark]
public void Wrong()
{
    var result = ExpensiveOperation(); // Result unused - may be optimized away!
}

// 2. Including setup in benchmark
[Benchmark]
public void Wrong2()
{
    var eventStore = CreateEventStore(); // ❌ Should be in [GlobalSetup]
    eventStore.AppendAsync(events, null);
}

// 3. Blocking async
[Benchmark]
public void Wrong3()
{
    _eventStore.AppendAsync(events, null).Wait(); // ❌ Use Task return type
}
```

### ✅ DO
```csharp
// 1. Return or consume result
[Benchmark]
public SequencedEvent[] Correct()
{
    return ExpensiveOperation(); // ✅ Result is used
}

// 2. Setup in GlobalSetup
[GlobalSetup]
public void Setup()
{
    _eventStore = CreateEventStore(); // ✅
}

[Benchmark]
public Task Correct2()
{
    return _eventStore.AppendAsync(_events, null); // ✅
}
```

---

## 🚀 Quick Start Commands

```bash
# 1. Navigate to benchmark project
cd tests/Opossum.BenchmarkTests

# 2. Build in Release mode (required!)
dotnet build -c Release

# 3. Run all benchmarks (slow - grab coffee ☕)
dotnet run -c Release

# 4. Run single benchmark class (faster)
dotnet run -c Release --filter *AppendBenchmarks*

# 5. Dry run to validate (fast)
dotnet run -c Release --job dry

# 6. List available benchmarks
dotnet run -c Release --list flat
```

---

## 📚 Decision Tree: Which Benchmark Pattern?

```
Is it a core IEventStore operation?
  ├─ Yes → Create dedicated benchmark class
  │         Example: AppendBenchmarks, ReadBenchmarks
  │
  └─ No → Is it a helper/utility component?
      ├─ Yes → Group with related benchmarks
      │         Example: SerializationBenchmarks for JsonEventSerializer
      │
      └─ No → Is it testing configuration impact?
          ├─ Yes → Use parameterized benchmark with [Params]
          │         Example: [Params(true, false)] for FlushImmediately
          │
          └─ No → Is it testing concurrency?
              ├─ Yes → Use ThreadingDiagnoser + parallel Tasks
              │         Example: ConcurrencyBenchmarks
              │
              └─ Complex scenario → Create scenario-specific benchmark
                    Example: ProjectionBuildBenchmarks
```

---

## 📝 Results Documentation Template

```markdown
# Benchmark Results - [Date]

## Environment
- **OS:** Windows 11 Pro 23H2
- **CPU:** Intel Core i7-12700K @ 3.60GHz (12C/20T)
- **RAM:** 32GB DDR4-3200
- **Storage:** Samsung 980 Pro 1TB NVMe SSD
- **.NET:** 10.0.0

## Summary

[Brief overview of what was benchmarked and key findings]

## AppendAsync Performance

| Scenario | Mean | P95 | P99 | Allocated |
|----------|------|-----|-----|-----------|
| Single (no flush) | 0.8ms | 1.2ms | 1.5ms | 2.1KB |
| Single (flush) | 6.2ms | 7.8ms | 9.1ms | 2.1KB |
| Batch 100 (flush) | 58ms | 65ms | 72ms | 172KB |

### Key Insights
1. **Flush overhead:** ~5ms per event on NVMe SSD
2. **Batching efficiency:** 100 events take 58ms = 0.58ms/event (better than single)
3. **Memory:** Linear scaling with event count

## ReadAsync Performance

[Similar table and insights]

## Recommendations

1. **For high throughput:** Disable flush or use batching
2. **For durability:** Keep flush enabled (default)
3. **For queries:** Tag indices perform well up to 10K events

## Detailed Results

See attached:
- `AppendBenchmarks-report.html`
- `AppendBenchmarks-measurements.csv`
```

---

**Last Updated:** 2025-01-28  
**Status:** Ready for Implementation  
**Next Steps:** Follow implementation-checklist.md Phase 1
