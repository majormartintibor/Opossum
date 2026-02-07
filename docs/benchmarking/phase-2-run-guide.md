# Phase 2 Benchmark Run Guide

## Quick Start

### 1. Validate Everything Works (5 minutes)
```bash
cd tests/Opossum.BenchmarkTests
dotnet run -c Release --job dry
```
**Expected:** All 32 benchmarks run successfully

### 2. Run by Priority

**Option A: All at Once (~60 minutes)**
```bash
dotnet run -c Release
```

**Option B: By Category (Recommended)**
```bash
# Step 1: AppendBenchmarks (~15-20 minutes)
dotnet run -c Release --filter *AppendBenchmarks*

# Step 2: ReadBenchmarks (~20-25 minutes)
dotnet run -c Release --filter *ReadBenchmarks*

# Step 3: QueryBenchmarks (~15-20 minutes)
dotnet run -c Release --filter *QueryBenchmarks*
```

---

## What Each Benchmark Tests

### AppendBenchmarks (13 benchmarks)
```
Finding optimal batch size:
├─ Single event (baseline)
├─ Batch 2 events
├─ Batch 5 events
├─ Batch 10 events ← Phase 1 winner (32% gain)
├─ Batch 20 events
├─ Batch 50 events
├─ Batch 100 events
├─ With flush variations
└─ DCB validation overhead
```

### ReadBenchmarks (11 benchmarks)
```
Query scaling:
├─ EventType queries (100 → 10K events)
├─ Tag queries (100 → 10K events)
└─ Query.All() (100 → 10K events)

Goal: Understand if queries scale linearly
```

### QueryBenchmarks (8 benchmarks)
```
Complex query patterns:
├─ EventType + Tag combinations
├─ High vs Low selectivity
├─ Descending order
└─ Real-world scenarios
```

---

## Expected Results

### AppendBenchmarks
**From Phase 1:**
- Single: 5.3 ms
- Batch 10: 3.6 ms per event (32% faster!)

**Phase 2 Questions:**
- Is batch 20-50 even better?
- Where do diminishing returns start?
- What's the DCB overhead?

### ReadBenchmarks
**Scaling Patterns:**
- **Linear:** 10x events = 10x time (baseline)
- **Sub-linear:** 10x events = 5x time (good - indices work!)
- **Super-linear:** 10x events = 20x time (bad - optimization needed)

**Target:** Sub-linear scaling

### QueryBenchmarks
**Complexity Overhead:**
- Simple query: Baseline
- Complex query: 2-3x slower (acceptable)
- Complex query: 5-10x slower (needs optimization)

**Target:** < 3x overhead for complex queries

---

## How to Read Results

### Batch Efficiency Chart (AppendBenchmarks)
```
Batch Size | Time per Event | Efficiency Gain
-----------+----------------+----------------
1          | 5.3 ms         | 0% (baseline)
2          | ??? ms         | ???%
5          | ??? ms         | ???%
10         | 3.6 ms         | 32% ✅
20         | ??? ms         | ???%
50         | ??? ms         | ???%
100        | ??? ms         | ???%
```

**Look for:** Peak efficiency (best gain) vs diminishing returns

### Query Scaling Chart (ReadBenchmarks)
```
Event Count | EventType Query | Tag Query | Query.All()
------------+-----------------+-----------+------------
100         | ??? ms          | ??? ms    | ??? ms
1,000       | ??? ms          | ??? ms    | ??? ms
10,000      | ??? ms          | ??? ms    | ??? ms
```

**Look for:** Sub-linear scaling (time doesn't 10x with 10x events)

---

## Interpreting Results

### Good Signs ✅
- Batch efficiency continues to improve up to 50-100 events
- Queries scale sub-linearly (indices are working)
- Complex queries < 3x overhead
- DCB overhead < 50%

### Warning Signs ⚠️
- Batch efficiency peaks early (< 10 events)
- Queries scale linearly or worse
- Complex queries > 5x overhead
- DCB overhead > 100%

### Critical Issues ❌
- Batch efficiency decreases with size
- Queries scale super-linearly (> 10x)
- Complex queries > 10x overhead
- DCB overhead > 200%

---

## After the Run

### 1. Check Results Location
```
tests/Opossum.BenchmarkTests/BenchmarkDotNet.Artifacts/results/
├── *-report.md    ← Markdown reports
├── *-report.csv   ← CSV data
└── *-report.html  ← HTML reports
```

### 2. Review Key Metrics
**From Markdown reports, look for:**
- Mean time
- Ratio to baseline
- Memory allocations
- Standard deviation

### 3. Create Summary
**Questions to answer:**
- What's the optimal batch size?
- How do queries scale?
- Are complex queries acceptable?
- Where are the bottlenecks?

---

## Decision Tree

```
After Phase 2 Results:

Are queries sub-linear?
├─ YES → ✅ Indices working well
└─ NO → ⚠️ Optimization needed

Is optimal batch size > 10?
├─ YES → ✅ Batching scales well
└─ NO → ⚠️ Investigate why

Complex queries < 3x overhead?
├─ YES → ✅ Acceptable
└─ NO → ⚠️ May need optimization

DCB overhead < 50%?
├─ YES → ✅ Acceptable for production
└─ NO → ⚠️ Consider alternatives

All ✅ → Continue to Phase 3
Any ⚠️ → Consider optimizations first
Multiple ❌ → Optimization required
```

---

## Common Issues & Solutions

### Issue: ReadBenchmarks Takes Too Long
**Cause:** Creating 10K events is slow  
**Solution:** Let it run once, results are pre-populated

### Issue: Out of Memory
**Cause:** 10K events in memory  
**Solution:** Increase heap size or reduce max events

### Issue: Inconsistent Results
**Cause:** Background processes  
**Solution:** Close apps, run on AC power, retry

---

## Quick Commands

### Validate
```bash
dotnet run -c Release --job dry
```

### Run All
```bash
dotnet run -c Release
```

### Run by Category
```bash
dotnet run -c Release --filter *AppendBenchmarks*
dotnet run -c Release --filter *ReadBenchmarks*
dotnet run -c Release --filter *QueryBenchmarks*
```

### Export Options
```bash
dotnet run -c Release --exporters markdown,csv,html
```

---

## What to Do with Results

### Immediate
1. ✅ Review markdown reports
2. ✅ Note surprising results
3. ✅ Identify bottlenecks

### Analysis
1. Create batch efficiency chart
2. Plot query scaling curves
3. Compare to Phase 1 baseline

### Documentation
1. Document optimal batch size
2. Document query scaling patterns
3. Update performance guide

### Decision
1. If acceptable → Continue to Phase 3
2. If issues → Plan optimizations
3. If critical → Optimize immediately

---

**Ready to run!** 🚀  
**Start with:** `dotnet run -c Release --job dry`
