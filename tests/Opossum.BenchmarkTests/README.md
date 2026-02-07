# Opossum.BenchmarkTests

Performance benchmarking suite for the Opossum event store library using BenchmarkDotNet.

---

## 📋 Table of Contents

- [Overview](#overview)
- [Prerequisites](#prerequisites)
- [Quick Start](#quick-start)
- [Available Benchmarks](#available-benchmarks)
- [Running Benchmarks](#running-benchmarks)
- [Understanding Results](#understanding-results)
- [Contributing](#contributing)
- [Documentation](#documentation)

---

## Overview

This project provides comprehensive performance benchmarks for Opossum's core functionality:

- **Event Store Operations:** AppendAsync, ReadAsync, query performance
- **Storage Layer:** File I/O, serialization, indexing, ledger operations
- **Advanced Features:** Projections, mediator pattern, concurrency
- **Configuration Impact:** Flush behavior, parallel reads, etc.

**Goal:** Establish performance baselines, identify bottlenecks, prevent regressions.

---

## Prerequisites

- **.NET 10 SDK** or later
- **Windows/Linux/macOS** (some diagnostics Windows-only)
- **Release mode** (benchmarks must run in Release, not Debug)
- **Sufficient disk space** (temp files created during benchmarks)
- **Stable environment** (close other apps for consistent results)

---

## Quick Start

### 1. Build the Project

```bash
cd tests/Opossum.BenchmarkTests
dotnet build -c Release
```

### 2. Run All Benchmarks (⚠️ Slow - 30-60 minutes)

```bash
dotnet run -c Release
```

### 3. Run Specific Benchmark Class (Faster)

```bash
dotnet run -c Release --filter *AppendBenchmarks*
```

### 4. Dry Run (Validate Without Full Execution)

```bash
dotnet run -c Release --job dry
```

---

## Available Benchmarks

### Core Operations

| Benchmark | What It Measures | Priority |
|-----------|------------------|----------|
| `AppendBenchmarks` | AppendAsync latency, throughput, flush overhead | 🔥 Critical |
| `ReadBenchmarks` | ReadAsync query performance by size/type/tags | 🔥 Critical |
| `QueryBenchmarks` | Complex query scenarios (AND/OR, selectivity) | 🔥 Critical |
| `ConcurrencyBenchmarks` | Thread contention, concurrent append/read | 🔥 Critical |

### Storage Layer

| Benchmark | What It Measures | Priority |
|-----------|------------------|----------|
| `SerializationBenchmarks` | JSON serialize/deserialize performance | 🟡 Medium |
| `IndexBenchmarks` | Tag/type index operations, lookups | 🟡 Medium |
| `LedgerBenchmarks` | Sequence position management | 🟡 Medium |
| `FileSystemBenchmarks` | File I/O operations, directory enumeration | 🟡 Medium |

### Advanced Features

| Benchmark | What It Measures | Priority |
|-----------|------------------|----------|
| `ProjectionBuildBenchmarks` | Projection rebuild performance | 🟡 Medium |
| `ProjectionQueryBenchmarks` | Projection query latency | 🟢 Low |
| `MediatorBenchmarks` | Command/query dispatch overhead | 🟢 Low |

**Legend:**  
🔥 Critical - Run on every performance-related PR  
🟡 Medium - Run weekly or before release  
🟢 Low - Run for completeness  

---

## Running Benchmarks

### Basic Commands

```bash
# Run all benchmarks
dotnet run -c Release

# Run specific class
dotnet run -c Release --filter *AppendBenchmarks*

# Run specific method
dotnet run -c Release --filter *AppendBenchmarks.SingleEventAppend*

# List all available benchmarks
dotnet run -c Release --list flat

# Run with specific job (short/medium/long)
dotnet run -c Release --job short
```

### Advanced Options

```bash
# Export to specific formats
dotnet run -c Release --exporters json,html,csv

# Run with memory profiler
dotnet run -c Release --memory

# Run with specific runtime
dotnet run -c Release --runtimes net10.0

# Join multiple filters
dotnet run -c Release --filter *Append* *Read*
```

### Available Jobs

| Job | Warmup | Iterations | Use Case |
|-----|--------|------------|----------|
| `dry` | 1 | 1 | Validate benchmarks compile/run |
| `short` | 3 | 3 | Quick testing during development |
| `medium` | 3 | 5 | Default (good balance) |
| `long` | 5 | 10 | High precision for CI/release |

**Example:**
```bash
dotnet run -c Release --job short --filter *AppendBenchmarks*
```

---

## Understanding Results

### Console Output

```
BenchmarkDotNet v0.14.0, Windows 11
Intel Core i7-12700K CPU 3.60GHz, 1 CPU, 20 logical and 12 physical cores
.NET SDK 10.0.0

| Method            | Mean     | StdDev   | P95      | Allocated |
|------------------ |---------:|---------:|---------:|----------:|
| SingleEventAppend | 6.234 ms | 0.312 ms | 6.789 ms | 2.1 KB    |
```

### Key Metrics

- **Mean:** Average execution time
- **StdDev:** Consistency (lower is better)
- **P95:** 95th percentile (good SLA target)
- **P99:** 99th percentile (outlier detection)
- **Allocated:** Memory allocated per operation
- **Gen0/1/2:** Garbage collection frequency

### Output Files

Results are saved to `BenchmarkDotNet.Artifacts/results/`:

```
BenchmarkDotNet.Artifacts/
├── results/
│   ├── AppendBenchmarks-report.html    ← Visual report
│   ├── AppendBenchmarks-report.md      ← GitHub-friendly
│   ├── AppendBenchmarks-report.csv     ← Raw data
│   └── AppendBenchmarks-report.json    ← Programmatic access
└── logs/
    └── AppendBenchmarks.log            ← Detailed logs
```

### Interpreting Results

**Good Performance:**
- Low StdDev (consistent results)
- P95/P99 close to Mean (few outliers)
- Low memory allocation
- Few Gen1/Gen2 collections

**Performance Issues:**
- High StdDev (inconsistent)
- P95/P99 >> Mean (many outliers)
- High memory allocation (GC pressure)
- Many Gen1/Gen2 collections (memory leaks?)

---

## Contributing

### Adding a New Benchmark

1. **Decide Category:** Core, Storage, or Advanced?
2. **Create Class:** Use naming pattern `[Category]Benchmarks.cs`
3. **Follow Template:** See `docs/benchmarking/quick-reference.md`
4. **Add [Config]:** Use `OpossumBenchmarkConfig`
5. **Add [MemoryDiagnoser]:** Always measure memory
6. **Test:** Run with `--job dry` first
7. **Document:** Add summary XML comments

**Example:**

```csharp
using Opossum.Core;
using BenchmarkDotNet.Attributes;

namespace Opossum.BenchmarkTests.Core;

/// <summary>
/// Benchmarks for [describe what you're measuring].
/// 
/// Scenarios:
/// - [List scenarios]
/// 
/// Last Updated: 2025-01-28
/// </summary>
[Config(typeof(OpossumBenchmarkConfig))]
[MemoryDiagnoser]
public class MyNewBenchmarks
{
    [GlobalSetup]
    public void Setup()
    {
        // Expensive setup here
    }

    [Benchmark]
    public void MyBenchmark()
    {
        // Only the operation to measure
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // Clean up resources
    }
}
```

### Best Practices

✅ **DO:**
- Run in Release mode
- Use `[GlobalSetup]` for expensive initialization
- Return or consume benchmark results (avoid dead code elimination)
- Clean up temp files in `[GlobalCleanup]`
- Use realistic test data
- Document findings in comments

❌ **DON'T:**
- Include setup logic in benchmark methods
- Block on async with `.Wait()` or `.Result`
- Use hardcoded paths (use temp directories)
- Forget to clean up temp files
- Optimize before measuring

---

## Project Structure

```
tests/Opossum.BenchmarkTests/
├── Opossum.BenchmarkTests.csproj    # Project file
├── Program.cs                        # Entry point
├── GlobalUsings.cs                   # Global using directives
├── BenchmarkConfig.cs                # Shared configuration
├── Helpers/
│   ├── BenchmarkDataGenerator.cs    # Test data generation
│   ├── TempFileSystemHelper.cs      # Temp directory management
│   └── EventFactory.cs              # Event creation
├── Core/
│   ├── AppendBenchmarks.cs          # AppendAsync scenarios
│   ├── ReadBenchmarks.cs            # ReadAsync scenarios
│   ├── QueryBenchmarks.cs           # Complex queries
│   └── ConcurrencyBenchmarks.cs     # Concurrent operations
├── Storage/
│   ├── SerializationBenchmarks.cs   # JSON performance
│   ├── IndexBenchmarks.cs           # Index operations
│   ├── LedgerBenchmarks.cs          # Ledger operations
│   └── FileSystemBenchmarks.cs      # File I/O
├── Projections/
│   ├── ProjectionBuildBenchmarks.cs
│   └── ProjectionQueryBenchmarks.cs
└── Mediator/
    └── MediatorBenchmarks.cs
```

---

## Documentation

Comprehensive documentation is available in `docs/benchmarking/`:

| Document | Purpose |
|----------|---------|
| `benchmarking-strategy.md` | Complete benchmarking plan and methodology |
| `implementation-checklist.md` | Step-by-step implementation guide |
| `quick-reference.md` | Patterns, examples, common pitfalls |
| `why-benchmarking-matters.md` | Context and rationale |
| `results/` | Historical benchmark results |
| `baseline-results/` | Baseline metrics for regression detection |

---

## CI/CD Integration

Benchmarks are **not** run on every commit (too slow).

**When benchmarks run:**
- ✅ Manual workflow trigger
- ✅ Scheduled (nightly)
- ✅ Performance-related PRs (tagged)
- ❌ Every commit (too slow)

**GitHub Actions Workflow:**

```yaml
name: Performance Benchmarks
on:
  workflow_dispatch:
  schedule:
    - cron: '0 2 * * *' # 2 AM daily
jobs:
  benchmark:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
      - run: dotnet run -c Release --project tests/Opossum.BenchmarkTests
      - uses: actions/upload-artifact@v4
        with:
          name: benchmark-results
          path: BenchmarkDotNet.Artifacts/results/
```

---

## Troubleshooting

### "Benchmarks run slowly"

✅ **Solution:** Ensure you're in Release mode:
```bash
dotnet run -c Release  # ← Release, not Debug!
```

### "Results are inconsistent"

✅ **Solutions:**
- Close other applications
- Run on AC power (not battery)
- Disable Windows Defender temporarily
- Use `--job long` for more iterations

### "Out of disk space"

✅ **Solution:** Benchmarks create temp files. Check cleanup methods:
```csharp
[GlobalCleanup]
public void Cleanup()
{
    if (Directory.Exists(_tempPath))
    {
        Directory.Delete(_tempPath, recursive: true);
    }
}
```

### "File locked" errors

✅ **Solution:** Add retry logic to cleanup:
```csharp
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
```

---

## Performance Targets (Reference)

Initial targets (to be validated with actual benchmarks):

| Operation | Target P95 | Notes |
|-----------|------------|-------|
| Single append (no flush) | < 1ms | In-memory operations |
| Single append (flush) | < 10ms | SSD flush overhead |
| Batch 100 (flush) | < 50ms | ~0.5ms per event |
| Query 1K events (tag) | < 5ms | Index lookup + reads |
| Query 10K events (tag) | < 50ms | Linear scaling |
| Projection build (10K) | < 1s | Reasonable rebuild |

---

## License

Same as Opossum project (see root LICENSE file).

---

## Support

- **Issues:** Report benchmark issues on GitHub
- **Discussions:** Ask questions in GitHub Discussions
- **Documentation:** See `docs/benchmarking/` for detailed guides

---

**Last Updated:** 2025-01-28  
**Status:** Ready for Implementation  
**Next Steps:** Follow `implementation-checklist.md` Phase 1
