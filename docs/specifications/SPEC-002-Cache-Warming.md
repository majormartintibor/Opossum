# SPEC-002: Cache Warming (Opt-in Performance Feature)

**Status:** Draft  
**Created:** 2024  
**Dependencies:** SPEC-001 (Projection Metadata)  
**Blocked By:** None  
**Blocks:** None

---

## Problem Statement

### Cold Start Performance Issue

When Opossum starts, all event and projection files must be read from disk into OS page cache on first access:

```
Cold Start (Current):
─────────────────────────────────────
App startup:           100ms  ✅ Fast
First API request:     200ms  ❌ Slow (disk read)
Subsequent requests:   10ms   ✅ Fast (cache hit)
```

For applications where **predictable performance** is more important than **fast startup**, we want to pre-load frequently-accessed data into OS page cache during startup.

### Trade-off

```
Warm Start (Proposed):
─────────────────────────────────────
App startup:           5000ms ⚠️ Slower (pre-load)
First API request:     10ms   ✅ Fast (cache hit)
Subsequent requests:   10ms   ✅ Fast (cache hit)
```

**Use Case:** Car dealership system where users expect instant responses but application restarts are rare.

---

## Requirements

### Functional Requirements

1. **FR-1:** Opt-in feature (disabled by default)
2. **FR-2:** Warm tag/type indices (high value, small size)
3. **FR-3:** Warm recent projections (configurable time window)
4. **FR-4:** Warm recent events (optional, configurable time window)
5. **FR-5:** Configurable maximum warmup duration (safety valve)
6. **FR-6:** Configurable maximum files to warm (safety valve)
7. **FR-7:** Configurable maximum warmup size budget (in bytes)
8. **FR-8:** Log warmup statistics (files warmed, duration, size)
9. **FR-9:** Run before web server accepts requests (IHostedService)

### Non-Functional Requirements

1. **NFR-1:** Warmup must be non-blocking (doesn't hold application startup)
2. **NFR-2:** Warmup must respect timeout limits (graceful degradation)
3. **NFR-3:** Warmup failure should not crash application
4. **NFR-4:** Observable progress (logging at INFO level)

---

## Design

### Configuration

```csharp
namespace Opossum.Configuration;

public sealed class CacheWarmingOptions
{
    /// <summary>
    /// Enable cache warming on application startup.
    /// Default: false (opt-in)
    /// </summary>
    public bool Enabled { get; set; } = false;
    
    /// <summary>
    /// Warm projections updated since this date.
    /// Default: Last 30 days
    /// </summary>
    public DateTime? WarmProjectionsSince { get; set; } = DateTime.UtcNow.AddDays(-30);
    
    /// <summary>
    /// Warm events appended since this date.
    /// Default: null (don't warm events, only projections)
    /// </summary>
    public DateTime? WarmEventsSince { get; set; } = null;
    
    /// <summary>
    /// Maximum warmup duration before aborting (safety valve).
    /// Default: 30 seconds
    /// </summary>
    public TimeSpan MaxWarmupDuration { get; set; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// Maximum number of files to warm (safety valve).
    /// Default: 10,000 files
    /// </summary>
    public int MaxFilesToWarm { get; set; } = 10_000;
    
    /// <summary>
    /// Maximum total size to warm in bytes (safety valve).
    /// Default: 1 GB
    /// </summary>
    public long MaxWarmupSizeBytes { get; set; } = 1_000_000_000; // 1 GB
    
    /// <summary>
    /// Always warm tag/type indices (cheap, high value).
    /// Default: true
    /// </summary>
    public bool AlwaysWarmIndices { get; set; } = true;
}
```

### OpossumOptions Integration

```csharp
namespace Opossum.Configuration;

public sealed class OpossumOptions
{
    // Existing properties...
    
    /// <summary>
    /// Cache warming configuration.
    /// </summary>
    public CacheWarmingOptions CacheWarming { get; } = new();
}
```

### Warmup Strategy

```
Warmup Priority (in order):
1. Tag indices (small, frequently accessed)
2. Type indices (small, frequently accessed)
3. Projection metadata index (small, needed for queries)
4. Recent projections (configurable window)
5. Recent events (optional, if configured)
```

---

## API Surface

### New Classes

```csharp
// CacheWarmingOptions.cs
namespace Opossum.Configuration;
public sealed class CacheWarmingOptions { ... }

// CacheWarmingService.cs
namespace Opossum.Projections;
internal sealed class CacheWarmingService : IHostedService
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

// CacheWarmer.cs
namespace Opossum.Projections;
internal sealed class CacheWarmer
{
    Task WarmIndicesAsync(string contextPath);
    Task WarmProjectionsAsync(string projectionPath, DateTime since, WarmupBudget budget);
    Task WarmEventsAsync(string eventPath, DateTime since, WarmupBudget budget);
}

// WarmupBudget.cs
internal sealed class WarmupBudget
{
    int MaxFiles { get; }
    long MaxSizeBytes { get; }
    TimeSpan MaxDuration { get; }
    bool IsExhausted();
}
```

### Configuration Example

```csharp
services.AddOpossum(options =>
{
    options.RootPath = "D:\\Database";
    options.AddContext("DealershipApp");
    
    // Enable cache warming
    options.CacheWarming.Enabled = true;
    options.CacheWarming.WarmProjectionsSince = DateTime.UtcNow.AddMonths(-3);
    options.CacheWarming.MaxWarmupDuration = TimeSpan.FromSeconds(10);
});
```

---

## Implementation Phases

### Phase 1: Core Infrastructure

**Files to Create:**
- `src/Opossum/Configuration/CacheWarmingOptions.cs`
- `src/Opossum/Projections/CacheWarmingService.cs` (IHostedService)
- `src/Opossum/Projections/CacheWarmer.cs` (internal)

**Files to Modify:**
- `src/Opossum/Configuration/OpossumOptions.cs`
- `src/Opossum/DependencyInjection/ServiceCollectionExtensions.cs`

**Tasks:**
1. Create `CacheWarmingOptions` class
2. Add property to `OpossumOptions`
3. Create `CacheWarmingService` (IHostedService)
4. Create `CacheWarmer` helper class
5. Register service in DI if enabled

### Phase 2: Warmup Logic

**Tasks:**
1. Implement index warming (tag + type indices)
2. Implement projection warming (using metadata index)
3. Implement event warming (optional)
4. Add budget tracking (max files, size, duration)
5. Add logging (INFO level statistics)

### Phase 3: Testing & Documentation

**Tasks:**
1. Add unit tests for warmup logic
2. Add integration tests for IHostedService
3. Test timeout handling
4. Document configuration examples
5. Update README with performance guidance

---

## Implementation Details

### CacheWarmingService (IHostedService)

```csharp
internal sealed class CacheWarmingService : IHostedService
{
    private readonly OpossumOptions _options;
    private readonly CacheWarmer _warmer;
    private readonly ILogger<CacheWarmingService> _logger;
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.CacheWarming.Enabled)
        {
            _logger.LogDebug("Cache warming disabled, skipping");
            return;
        }
        
        _logger.LogInformation("Starting cache warming...");
        var sw = Stopwatch.StartNew();
        
        var budget = new WarmupBudget(
            _options.CacheWarming.MaxFilesToWarm,
            _options.CacheWarming.MaxWarmupSizeBytes,
            _options.CacheWarming.MaxWarmupDuration);
        
        int filesWarmed = 0;
        
        // Phase 1: Warm indices (always, cheap)
        if (_options.CacheWarming.AlwaysWarmIndices)
        {
            filesWarmed += await _warmer.WarmIndicesAsync(_options.GetContextPath());
        }
        
        // Phase 2: Warm projections
        if (_options.CacheWarming.WarmProjectionsSince.HasValue && !budget.IsExhausted())
        {
            filesWarmed += await _warmer.WarmProjectionsAsync(
                _options.GetProjectionsPath(),
                _options.CacheWarming.WarmProjectionsSince.Value,
                budget);
        }
        
        // Phase 3: Warm events (optional)
        if (_options.CacheWarming.WarmEventsSince.HasValue && !budget.IsExhausted())
        {
            filesWarmed += await _warmer.WarmEventsAsync(
                _options.GetEventsPath(),
                _options.CacheWarming.WarmEventsSince.Value,
                budget);
        }
        
        sw.Stop();
        _logger.LogInformation(
            "Cache warming complete: {Files} files in {Duration}ms",
            filesWarmed,
            sw.ElapsedMilliseconds);
    }
    
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

### CacheWarmer

```csharp
internal sealed class CacheWarmer
{
    public async Task<int> WarmProjectionsAsync(
        string projectionsPath,
        DateTime since,
        WarmupBudget budget)
    {
        int warmed = 0;
        
        // Load metadata index to find recent projections
        var metadataIndex = await LoadMetadataIndexAsync(projectionsPath);
        
        var candidates = metadataIndex
            .Where(m => m.LastUpdatedAt >= since)
            .OrderByDescending(m => m.LastUpdatedAt) // Newest first
            .ToList();
        
        foreach (var (key, metadata) in candidates)
        {
            if (budget.IsExhausted())
                break;
            
            var filePath = GetProjectionFilePath(projectionsPath, key);
            
            // Just read file into memory (OS caches it)
            _ = await File.ReadAllBytesAsync(filePath);
            
            budget.ConsumeFile(metadata.SizeInBytes);
            warmed++;
        }
        
        return warmed;
    }
}
```

---

## Testing Requirements

### Unit Tests

- [ ] `CacheWarmingOptions` default values
- [ ] `WarmupBudget` tracks files/size/duration correctly
- [ ] `WarmupBudget.IsExhausted()` logic
- [ ] `CacheWarmer.WarmIndicesAsync` reads all index files
- [ ] `CacheWarmer.WarmProjectionsAsync` filters by date
- [ ] Budget respected (stops when limit reached)

### Integration Tests

- [ ] IHostedService runs before web server starts
- [ ] Warmup completes within MaxWarmupDuration
- [ ] Warmup stops at MaxFilesToWarm limit
- [ ] Warmup stops at MaxWarmupSizeBytes limit
- [ ] Warmup logs statistics correctly
- [ ] Warmup failure doesn't crash app
- [ ] Subsequent queries faster after warmup

### Performance Tests

- [ ] Warmup reduces first query latency by 50%+
- [ ] Warmup adds < 10 seconds to startup for typical datasets
- [ ] Warmup overhead < 10% CPU during startup

---

## Migration Strategy

No migration needed - this is a new opt-in feature.

**Default behavior:** `Enabled = false`, no change to existing behavior.

**Enabling:** Set `options.CacheWarming.Enabled = true` in configuration.

---

## Dependencies

### This Feature Depends On:
- **SPEC-001:** Projection Metadata (needs `LastUpdatedAt` to filter recent projections)

### Features That Depend On This:
- None (standalone performance optimization)

---

## Open Questions

1. **Q:** Should we warm projection tag indices?
   - **A:** Yes, covered by "AlwaysWarmIndices" option.

2. **Q:** Should warmup be async (non-blocking startup)?
   - **A:** IHostedService waits for warmup to complete before accepting requests. This ensures first request is fast.

3. **Q:** What if warmup takes > MaxWarmupDuration?
   - **A:** Stop gracefully, log warning, continue startup. Partial warmup is better than no warmup.

4. **Q:** Should we support custom warmup strategies?
   - **A:** Deferred to future. MVP uses time-based filtering only.

---

## Success Criteria

- [ ] Warmup reduces first query latency by measurable amount
- [ ] Warmup respects all safety limits (time, files, size)
- [ ] Warmup failure doesn't crash application
- [ ] Warmup statistics logged clearly
- [ ] All tests passing
- [ ] Documentation includes performance benchmarks
- [ ] Zero breaking changes

---

## Configuration Examples

### Example 1: Car Dealership (Fast Queries > Fast Startup)

```csharp
services.AddOpossum(options =>
{
    options.CacheWarming.Enabled = true;
    options.CacheWarming.WarmProjectionsSince = DateTime.UtcNow.AddMonths(-3);
    options.CacheWarming.MaxWarmupDuration = TimeSpan.FromSeconds(5);
    // Warm last 3 months of sales data
    // Accept 5-second startup delay for instant queries
});
```

### Example 2: Factory (Recent Data Only)

```csharp
services.AddOpossum(options =>
{
    options.CacheWarming.Enabled = true;
    options.CacheWarming.WarmProjectionsSince = DateTime.UtcNow.AddDays(-1);
    options.CacheWarming.MaxWarmupDuration = TimeSpan.FromSeconds(1);
    // Only warm today's production data
    // Minimal startup impact
});
```

### Example 3: Disabled (Default)

```csharp
services.AddOpossum(options =>
{
    // CacheWarming.Enabled defaults to false
    // No warmup, fast startup, cold first queries
});
```

---

## References

- GitHub Issue: TBD
- Discussion: Cache warming brainstorming session
- Related Specs: SPEC-001 (Projection Metadata)
