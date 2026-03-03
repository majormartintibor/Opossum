# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Performance
- **Benchmark run 2026-03-03 (0.4.0-preview.2 baseline)** — Full benchmark suite executed
  against the `release/0.4.0-preview.2` branch. Key results vs 0.4.0-preview.1 (20260226):
  - ✅ **Large-batch no-flush regression resolved**: batch 50 −13 %, batch 100 −12 %
    (open item from 20260226 analysis fully closed)
  - ✅ **ReadLast benchmarks added**: new `ReadLastBenchmarks` suite confirms near-O(1)
    scaling (798 μs → 1,105 μs for 100 → 10,000 events; 192× faster than full `Read`)
  - ✅ Flush scenarios slightly improved: single-event −5 %, batch-10 −3 %
  - ✅ Incremental projection updates 5–15 % faster
  - ✅ **Incremental projection allocations**: **VERIFIED FIXED** in rerun1 — zero
    allocations confirmed (0 B / 3.68 μs for 1 event, 0 B / 4.52 μs for 10 events).
    Root cause: dead `GetCheckpointAsync` + missing cache (see Fixed section).
    Rerun shows 55–65 % speedup and zero-allocation hot path restored ✅
  - ℹ️ Parallel rebuild baseline corrected: 20260226 had wide error bands (StdDev 35–47 ms
    on 3-iteration benchmark); 20260303 is stable at ~356–381 ms (StdDev 1.7–5.3 ms)
  - Full analysis: `docs/benchmarking/results/20260303/ANALYSIS.md` (includes rerun1 verification)
