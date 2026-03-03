# Opossum Performance Baseline & Benchmark Results

## Latest Benchmark: 2026-03-03
## Status: ✅ Complete — 0.4.0-preview.2 Release Validated
## Version: 1.3.0

**Latest comprehensive analysis:** See `docs/benchmarking/results/20260303/ANALYSIS.md`
**Previous analysis:** See `docs/benchmarking/results/20260226/ANALYSIS.md`

---

## 📊 Executive Summary (Updated 2026-03-03)

**Opossum's file-based event store delivers excellent performance for event sourcing workloads:**

- **Write:** ~61 events/sec with full durability (fsync) on SSD; ~208 events/sec without flush
- **ReadLast:** ~800–1,100 μs for up to 10K events — **near-constant O(1) scaling** 🆕
- **Read (tag-based, high selectivity):** ~501 μs for targeted queries (**improved from ~553 μs**)
- **Read (tag-based, 1K events):** ~10.8 ms (sub-linear scaling)
- **Projections:** ~15K events/sec rebuild, ~10 μs incremental updates (500x faster than rebuild)
- **Parallel rebuilding:** ~6 % speedup with Concurrency=4 vs sequential (I/O-bound workload)
- **Descending queries:** Zero overhead — full parity with ascending (**confirmed**)
- **Complex projections:** ~125 μs

**All core operations perform within acceptable ranges for production use.**

> ⚠️ **Open item:** Incremental projection updates reintroduced ~12–16 KB allocations
> (were zero-allocation in 0.4.0-preview.1). Under investigation for 0.4.0 final.

---

## 🎯 Quick Reference

| Operation | Typical Performance | Best For |
|-----------|-------------------|----------|
| **Single Event Append** | 16.4 ms (with flush) / 4.7 ms (no flush) | CRUD operations |
| **Batch Append (5 events)** | 17.3 ms no-flush (3.5 ms/event) | Bulk imports |
| **ReadLast by event type** | ~800 μs–1.1 ms (100–10K events) | Latest-event look-ups 🆕 |
| **Query by Tag** | 10.8 ms/1K events | Filtered reads |
| **Query.All()** | 845 ms/10K events | Full scans |
| **Projection Rebuild** | 4.6 ms/50 events, 32.2 ms/500 events | Rare full rebuilds |
| **Incremental Projection** | ~10 μs/update | Real-time updates |

---

## 📈 Detailed Benchmark Results

### Phase 1: Write Performance (Append)

**Configuration:** Full durability (fsync after every event)

| Benchmark | Time | Throughput | Status |
|-----------|------|------------|--------|
| Single event (no flush) | 4.7 ms | ~213 events/sec | ✅ Good |
| Single event (with flush) | 16.4 ms | ~61 events/sec | ✅ Expected (fsync overhead) |
| Batch 5 events (no flush) | 17.3 ms total | ~289 events/sec | ✅ Good |
| Batch 10 events (with flush) | 124.2 ms total | ~81 events/sec | ✅ Expected |
| Batch 50 events (no flush) | 195.3 ms total | ~256 events/sec | ✅ Good |
| Batch 100 events (no flush) | 395.2 ms total | ~253 events/sec | ✅ Good |
| DCB validation | 3.77 ms | — | ✅ Expected |

**Key Findings:**
- Fsync overhead: ~6 ms per event (unavoidable for durability)
- Batching provides significant throughput improvement
- DCB validation adds minimal overhead

**Recommendation:** Use batch appends when possible (5-10 events optimal)

---

### Phase 2: Read Performance (Queries)

#### A. Tag-Based Queries (Most Common)

| Dataset | Query Time | Per Event | Scaling |
|---------|-----------|-----------|----------|
| 100 events | 3.73 ms | 37.3 μs | Baseline |
| 1,000 events | 10.85 ms | 10.9 μs | Sub-linear |
| 10,000 events | 86.1 ms | 8.6 μs | **Sub-linear!** ✅ |

**Result:** Tag queries scale BETTER than linear

#### B. EventType-Based Queries

| Dataset | Query Time | Per Event | Scaling |
|---------|-----------|-----------|----------|
| 100 events | 3.68 ms | 36.8 μs | Baseline |
| 1,000 events | 23.7 ms | 23.7 μs | Sub-linear |
| 10,000 events | 212.3 ms | 21.2 μs | **Sub-linear!** ✅ |

**Result:** EventType queries also scale better than linear

#### C. Query.All() Performance

| Dataset | Time | Per Event | Status |
|---------|------|-----------|--------|
| 100 events | 10.3 ms | 103 μs | ✅ Fast |
| 1,000 events | 92.3 ms | 92.3 μs | ✅ Good |
| 10,000 events | 845 ms | 84.5 μs | ✅ Acceptable |

**Result:** Near-linear scaling, acceptable for full scans

#### D. ReadLast Performance 🆕

| Dataset | Time | Ratio |
|---------|------|-------|
| 100 events (event type) | 798.9 μs | baseline |
| 1,000 events (event type) | 848.0 μs | 1.06× |
| 10,000 events (event type) | 1,105.1 μs | 1.38× |
| 1,000 events (tag) | 825.4 μs | 1.03× |

**Result:** Near-constant O(1) scaling — 192× faster than full `Read` at 10K events

#### E. Selective Query Performance

| Query Type | Time | vs Baseline |
|-----------|------|------------|
| High selectivity (few matches) | 500.9 μs | **10.62x faster** than EventType+Tag baseline |
| Low selectivity (many matches) | 98,930 μs | 18.60x slower |
| Multiple QueryItems (OR logic) | 9,527 μs | 1.79x slower |
| Real-world: Payment events | 4,283 μs | 1.24x faster |
| Real-world: Orders in state | 1,144 μs | 4.65x faster |

#### E. Descending Order Performance

| Configuration | Time (isolated, 1K events) | Status |
|--------------|---------------------------|--------|
| Ascending order | 41.27 ms | ✅ Baseline |
| Descending order | 41.39 ms | ✅ **Full parity** |
| Ratio | **1.003×** | 🚀 |

**Result:** Descending queries have identical performance to ascending

---

### Phase 3: Projection Performance

#### A. Projection Rebuild (Full Reconstruction)

| Dataset | Time | Per Event | Scaling |
|---------|------|-----------|---------|
| 50 events | 4.62 ms | 92.4 μs | Baseline |
| 250 events | 16.6 ms | 66.4 μs | 3.6x (linear) |
| 500 events | 32.2 ms | 64.5 μs | 7.0x (linear) |

**Result:** Perfect linear scaling for projection rebuilds

#### B. Incremental Updates (Real-Time)

| Update Size | Time | vs Full Rebuild |
|-------------|------|----------------|
| +1 event | **10.4 μs** | **~444x faster** than 50-event rebuild |
| +10 events | **10.0 μs** | **~461x faster** than 50-event rebuild |

> ⚠️ **Note:** Incremental updates currently allocate ~12–16 KB per call (regression from
> 0.4.0-preview.1 which had zero allocations). Under investigation for 0.4.0 final.

**Result:** Incremental updates are VASTLY faster than full rebuilds

**Recommendation:**
- ✅ Use incremental for <50 new events
- ✅ Use full rebuild for >50 events or stale projections

#### C. Complex Projections (Multiple Event Types)

| Scenario | Time | Status |
|----------|------|--------|
| Multi-type aggregation | **125 μs** | ✅ Fast |

---

## 🏆 Performance Characteristics

### Strengths

1. **Sub-Linear Read Scaling** ✅
   - Tag queries: 10x events = 2.8x time
   - EventType queries: 10x events = 3.1x time
   - Excellent for growing datasets

2. **Predictable Write Performance** ✅
   - ~4.7 ms per event (no flush), ~16.4 ms (with fsync)
   - Scales linearly with batch size
   - No surprises

3. **Blazing Fast Incremental Projections** ✅
   - Microsecond-level updates
   - ~444–461x faster than full rebuild
   - Real-time friendly

4. **ReadLast: Near-Constant O(1) Scaling** ✅ 🆕
   - ~800 μs for 100 events, ~1,100 μs for 10,000 events
   - 192× faster than full `Read` at 10K events
   - Ideal for aggregate reconstruction, idempotency checks, projection state look-ups

5. **Efficient Indexing** ✅
   - Tag-based queries very fast
   - EventType queries optimized
   - Parallel file reads

6. **Descending Order at Full Parity** ✅
   - Zero overhead vs ascending
   - In-place position reversal (no extra I/O)

### Trade-offs

1. **Fsync Overhead** ⚠️
   - ~6 ms per event for durability
   - Unavoidable for crash safety
   - Can disable for testing (not recommended for production)

2. **Query.All() Slower** ⚠️
   - 845 ms for 10K events
   - Acceptable for rare full scans
   - Use filtered queries when possible

3. **File-Per-Event Architecture** ℹ️
   - Many small files (not one big file)
   - Good: Simple, debuggable
   - Trade-off: Can't batch fsyncs across files

---

## 🚫 What We Tried and Removed

### Batched Fsyncs (FAILED)

**Goal:** Batch multiple events → single fsync → 40-60% improvement

**Result:** **2-2.3x SLOWER** (100-130% worse!)

**Why it failed:**
- Lock contention serialized parallel writes
- File-per-event architecture can't batch fsyncs (each file needs separate syscall)
- Expert advice was for database-style WAL (single file), not file-per-event

**Decision:** Removed entirely

**Lessons Learned:**
1. Expert advice must match YOUR architecture
2. Locks kill concurrent performance
3. Benchmark realistic scenarios
4. Be willing to remove failed features

**Documentation:** See `docs/lessons-learned/batched-flush-failure.md`

**Future:** If >200 events/sec needed, implement WAL-style architecture (see `docs/future-plans/batched-flush-redesign-plan.md`)

---

## 🎯 Production Recommendations

### For Write-Heavy Workloads

**Best Practices:**
1. ✅ Use batch appends (5-10 events optimal)
2. ✅ Keep fsync enabled (durability matters)
3. ✅ Use DCB validation for consistency
4. ❌ Don't disable fsync in production

**Expected Throughput:** 94-156 events/sec

### For Read-Heavy Workloads

**Best Practices:**
1. ✅ Use tag-based queries when possible (fastest)
2. ✅ Use EventType queries for type filtering
3. ✅ Avoid Query.All() on large datasets
4. ✅ Use projections for complex read models

**Expected Query Time:** <1ms for filtered queries, <100ms for Query.All()

### For Real-Time Projections

**Best Practices:**
1. ✅ Use incremental updates (<50 events)
2. ✅ Rebuild projections overnight (background jobs)
3. ✅ Cache projection results
4. ❌ Don't rebuild on every event

**Expected Update Time:** 10-11 μs per event

---

## 📊 Scaling Characteristics

### How Performance Scales with Dataset Size

| Metric | 10x More Events | Explanation |
|--------|-----------------|-------------|
| Tag Query | **2.9x slower** | Sub-linear (excellent!) |
| EventType Query | **3.1x slower** | Sub-linear (excellent!) |
| ReadLast | **1.4x slower** | Near-constant O(1) 🆕 |
| Query.All() | **~9x slower** | Near-linear (expected) |
| Projection Rebuild | **7x slower** | Linear (expected) |
| Incremental Update | **Same** | Constant time ✅ |

**Conclusion:** Opossum scales well for filtered queries, linear for full scans

---

## 🔧 Configuration for Different Scenarios

### Low-Latency (Default)

```csharp
builder.Services.AddOpossum(options =>
{
    options.FlushEventsImmediately = true; // 10ms latency
});
```

**Best for:** CRUD APIs, real-time event processing
**Throughput:** 94 events/sec
**Latency:** ~10ms per event

### Testing (Fast, No Durability)

```csharp
builder.Services.AddOpossum(options =>
{
    options.FlushEventsImmediately = false; // ~3ms latency
});
```

**Best for:** Unit tests, integration tests
**Throughput:** ~300 events/sec
**⚠️ Risk:** Data loss on crash (don't use in production!)

---

## 📈 Benchmark Methodology

### Tools & Configuration

**Framework:** BenchmarkDotNet 0.14.0
**Runtime:** .NET 10.0.2 (X64 RyuJIT AVX2)
**Platform:** Windows 11
**Hardware:** Modern SSD

**Benchmark Config:**
- InvocationCount: 1
- UnrollFactor: 1
- Memory Diagnoser: Enabled
- Job: Default (auto-tuned iterations)

### Dataset Sizes

**Write Benchmarks:** 1, 5, 10 events
**Read Benchmarks:** 100, 1K, 10K events
**Projection Benchmarks:** 50, 250, 500 events

**Why these sizes?**
- Representative of real-world workloads
- Demonstrate scaling characteristics
- Avoid memory exhaustion during benchmark runs

### Benchmark Design Principles

1. **Isolate what you measure**
   - Setup in `[IterationSetup]` (not measured)
   - Only benchmark the operation itself

2. **Use realistic scenarios**
   - Test concurrent operations (not just sequential)
   - Include typical query patterns

3. **Measure consistently**
   - Fresh state each iteration
   - No event accumulation across runs

---

## 🎓 Lessons Learned

### What Worked

1. **Sub-linear read scaling** - Parallel reads + efficient indexing
2. **Incremental projections** - 500x faster than full rebuild
3. **Descending order fix** - Simple Array.Reverse() before read = 12x faster

### What Failed

1. **Batched fsyncs** - Lock contention made it 2x SLOWER
2. **Expert advice without validation** - Database patterns don't fit file-per-event

### Key Takeaways

1. ✅ **Validate expert advice** against YOUR architecture
2. ✅ **Benchmark realistic scenarios** (concurrent, not isolated)
3. ✅ **Locks kill concurrency** (avoid in hot paths)
4. ✅ **Be willing to remove failed features** (sunk cost fallacy is real)
5. ✅ **Simple often wins** (Array.Reverse() > complex algorithms)

---

## 🔮 Future Optimizations

### If We Need >200 Events/Sec

**Option:** WAL-Style Architecture
- Switch from file-per-event to append-only log
- Batch events in single file → single fsync
- Expected: 2-3x throughput improvement

**Effort:** 2-3 weeks
**Risk:** Medium (architecture change)

**See:** `docs/future-plans/batched-flush-redesign-plan.md`

### Query Optimizations (Low Priority)

**Query.All() could be faster with:**
- Memory-mapped file reads
- Compressed event storage
- Event caching

**Current performance (826ms/10K) is acceptable for rare full scans**

---

## 📚 Related Documentation

### Latest Benchmark Results (2026-02-12)
- **`docs/benchmarking/results/20260212/ANALYSIS.md`** - Comprehensive benchmark analysis
  - Append performance: ~100 events/sec with flush
  - Tag query (high selectivity): ~500μs
  - Projection rebuild: ~15K events/sec
  - Parallel rebuilding: 2x speedup with 4 cores
  - Descending order: Zero overhead (optimized!)
  - DCB append: Actually faster than regular append

### Historical Benchmarks
- `docs/lessons-learned/batched-flush-failure.md` - What we tried and why it failed
- `docs/future-plans/batched-flush-redesign-plan.md` - How to implement batching properly

### Implementation Details
- `docs/implementation/durability-guarantees.md` - Fsync and crash safety
- `docs/features/ledger.md` - Sequence position management

### Specifications
- `Specification/DCB-Specification.md` - Dynamic Consistency Boundaries

---

## ✅ Verification Checklist

**Before claiming good performance:**
- [x] All benchmarks run successfully
- [x] Results are consistent (±5% variance)
- [x] Memory usage reasonable (<500MB for benchmarks)
- [x] Scaling characteristics understood
- [x] Production recommendations documented

---

## 🎯 Summary

**Opossum delivers:**
- ✅ Good write throughput (94-156 events/sec)
- ✅ Excellent read performance (sub-linear scaling)
- ✅ Blazing fast incremental projections (10 μs)
- ✅ Predictable, production-ready performance

**Current performance is sufficient for most event sourcing workloads!**

**Don't optimize prematurely. The baseline is good enough. 🎉**

---

**Baseline Established:** 2025-01-28
**Status:** Production Ready  
**Next:** Monitor production workloads, optimize if needed
