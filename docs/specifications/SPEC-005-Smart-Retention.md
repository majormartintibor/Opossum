# SPEC-005: Smart Retention (Access Frequency-Based)

**Status:** Draft (Future Enhancement)  
**Created:** 2024  
**Dependencies:** SPEC-001 (Metadata), SPEC-003 (Retention Policies)  
**Blocked By:** None (can be implemented after SPEC-003)  
**Blocks:** None

---

## Problem Statement

Time-based retention (SPEC-003) has a fundamental limitation: **it doesn't consider data usage patterns**.

### Real-World Problem

```
Car Dealership Scenario:
────────────────────────────────────────────────────────
Salesperson A (retired 3 years ago):
  - Last sale: 3 years ago ← OLD
  - Accessed: DAILY (commission audit reports) ← HOT
  → Time-based policy deletes → Users angry! ❌

Salesperson B (just hired, quit immediately):
  - Last sale: Yesterday ← NEW
  - Accessed: NEVER (they left the company) ← COLD
  → Time-based policy keeps → Wasting space! ❌
```

**The issue:** Age doesn't equal value. Old data that's frequently accessed is more valuable than new data that's never used.

---

## Requirements

### Functional Requirements

1. **FR-1:** Track last access time for projections (opt-in, adds overhead)
2. **FR-2:** Hybrid retention policy (age + inactivity)
3. **FR-3:** Configurable inactivity threshold (e.g., "not accessed in 90 days")
4. **FR-4:** Safety minimum age (don't archive data < N days old, even if cold)
5. **FR-5:** Hot/cold classification for cache warming prioritization
6. **FR-6:** Access tracking must be lightweight (minimal read overhead)
7. **FR-7:** Configurable tracking granularity (per-hour, per-day, per-week)
8. **FR-8:** Access statistics API (most/least accessed projections)

### Non-Functional Requirements

1. **NFR-1:** Access tracking overhead < 5% on read operations
2. **NFR-2:** Access tracking must not block reads (async update)
3. **NFR-3:** Access metadata persisted reliably (survives restarts)
4. **NFR-4:** Backward compatible (works with existing projections)
5. **NFR-5:** Opt-in feature (disabled by default, no overhead if not used)

---

## Design

### Access Tracking Configuration

```csharp
namespace Opossum.Projections;

/// <summary>
/// Configuration for tracking projection access patterns.
/// Disabled by default to avoid read overhead.
/// </summary>
public sealed class AccessTrackingOptions
{
    /// <summary>
    /// Enable access tracking.
    /// Default: false (no overhead)
    /// </summary>
    public bool Enabled { get; set; } = false;
    
    /// <summary>
    /// How frequently to update LastAccessedAt.
    /// Prevents excessive metadata writes on every read.
    /// Default: Once per day per projection
    /// </summary>
    public TimeSpan UpdateGranularity { get; set; } = TimeSpan.FromDays(1);
    
    /// <summary>
    /// Batch access updates to reduce I/O.
    /// Updates written asynchronously in background.
    /// Default: true
    /// </summary>
    public bool BatchUpdates { get; set; } = true;
    
    /// <summary>
    /// Batch flush interval (if BatchUpdates = true).
    /// Default: 60 seconds
    /// </summary>
    public TimeSpan BatchFlushInterval { get; set; } = TimeSpan.FromSeconds(60);
}
```

### Enhanced ProjectionMetadata

```csharp
namespace Opossum.Projections;

public sealed record ProjectionMetadata
{
    // Existing properties from SPEC-001
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset LastUpdatedAt { get; init; }
    public required long Version { get; init; }
    public required long SizeInBytes { get; init; }
    
    /// <summary>
    /// When this projection was last accessed (read).
    /// Only tracked if AccessTrackingOptions.Enabled = true.
    /// Null if tracking disabled or never accessed.
    /// </summary>
    public DateTimeOffset? LastAccessedAt { get; init; }
    
    /// <summary>
    /// Total number of times this projection has been accessed.
    /// Only tracked if AccessTrackingOptions.Enabled = true.
    /// </summary>
    public long AccessCount { get; init; }
}
```

### Smart Retention Policy

```csharp
namespace Opossum.Projections;

public enum RetentionPolicyType
{
    NoRetention = 0,        // Keep forever (SPEC-003)
    DeleteAfter = 1,        // Time-based deletion (SPEC-003)
    ArchiveAfter = 2,       // Time-based archival (SPEC-003)
    SmartRetention = 3      // NEW: Hybrid (age + access frequency)
}

public sealed class ProjectionRetentionPolicy
{
    public RetentionPolicyType PolicyType { get; set; } = RetentionPolicyType.NoRetention;
    
    /// <summary>
    /// For TimeBasedRetention: Simple age threshold.
    /// For SmartRetention: Minimum age before considering for archival.
    /// </summary>
    public TimeSpan RetainFor { get; set; } = TimeSpan.FromYears(3);
    
    /// <summary>
    /// For SmartRetention: Archive if not accessed in this period.
    /// Example: If MinimumAge = 1 year AND InactivityThreshold = 90 days,
    ///   then archive projections that are:
    ///   - At least 1 year old (safety threshold)
    ///   - AND not accessed in last 90 days (cold data)
    /// </summary>
    public TimeSpan? InactivityThreshold { get; set; }
    
    public ArchiveConfiguration? ArchiveConfig { get; set; }
}
```

---

## API Surface

### Configuration Integration

```csharp
// OpossumOptions.cs
public sealed class OpossumOptions
{
    /// <summary>
    /// Access tracking configuration (opt-in).
    /// Required for SmartRetention policies.
    /// </summary>
    public AccessTrackingOptions AccessTracking { get; } = new();
}

// ProjectionOptions.cs
public sealed class ProjectionOptions
{
    // Existing: RetentionPolicies dictionary
    
    /// <summary>
    /// Configure smart retention for a projection.
    /// Requires AccessTracking.Enabled = true.
    /// </summary>
    public ProjectionOptions ConfigureSmartRetention(
        string projectionName,
        TimeSpan minimumAge,
        TimeSpan inactivityThreshold,
        Action<ArchiveConfiguration>? configureArchive = null)
    {
        var policy = new ProjectionRetentionPolicy
        {
            PolicyType = RetentionPolicyType.SmartRetention,
            RetainFor = minimumAge,
            InactivityThreshold = inactivityThreshold
        };
        
        if (configureArchive != null)
        {
            policy.ArchiveConfig = new ArchiveConfiguration();
            configureArchive(policy.ArchiveConfig);
        }
        
        RetentionPolicies[projectionName] = policy;
        return this;
    }
}
```

### Access Statistics API

```csharp
namespace Opossum.Projections;

public interface IProjectionAccessStatistics
{
    /// <summary>
    /// Get most frequently accessed projections (hot data).
    /// </summary>
    Task<IReadOnlyList<ProjectionAccessInfo>> GetMostAccessedAsync(
        string projectionName,
        int count = 100);
    
    /// <summary>
    /// Get least recently accessed projections (cold data).
    /// </summary>
    Task<IReadOnlyList<ProjectionAccessInfo>> GetLeastRecentlyAccessedAsync(
        string projectionName,
        int count = 100);
    
    /// <summary>
    /// Get projections eligible for smart archival.
    /// </summary>
    Task<IReadOnlyList<ProjectionAccessInfo>> GetEligibleForArchivalAsync(
        string projectionName,
        ProjectionRetentionPolicy policy);
}

public sealed record ProjectionAccessInfo
{
    public required string Key { get; init; }
    public required DateTimeOffset LastAccessedAt { get; init; }
    public required long AccessCount { get; init; }
    public required long SizeInBytes { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
```

---

## Implementation Phases

### Phase 1: Access Tracking Infrastructure

**Files to Create:**
- `src/Opossum/Projections/AccessTrackingOptions.cs`
- `src/Opossum/Projections/IProjectionAccessStatistics.cs`
- `src/Opossum/Projections/ProjectionAccessStatistics.cs` (internal)
- `src/Opossum/Projections/AccessTracker.cs` (internal)

**Files to Modify:**
- `src/Opossum/Projections/ProjectionMetadata.cs` (add LastAccessedAt, AccessCount)
- `src/Opossum/Configuration/OpossumOptions.cs` (add AccessTracking property)
- `src/Opossum/Projections/FileSystemProjectionStore.cs` (track access on read)

**Tasks:**
1. Add `LastAccessedAt` and `AccessCount` to `ProjectionMetadata`
2. Create `AccessTrackingOptions` configuration
3. Create `AccessTracker` class (batched updates)
4. Update `FileSystemProjectionStore.GetAsync` to track access
5. Implement background flush of access updates

### Phase 2: Smart Retention Logic

**Files to Modify:**
- `src/Opossum/Projections/RetentionPolicyType.cs` (add SmartRetention)
- `src/Opossum/Projections/ProjectionRetentionPolicy.cs` (add InactivityThreshold)
- `src/Opossum/Projections/ProjectionRetentionService.cs` (implement smart logic)

**Tasks:**
1. Add `SmartRetention` to `RetentionPolicyType` enum
2. Add `InactivityThreshold` to `ProjectionRetentionPolicy`
3. Implement smart retention evaluation logic
4. Test hybrid policy (age + inactivity)

### Phase 3: Statistics & Observability

**Tasks:**
1. Implement `IProjectionAccessStatistics`
2. Add hot/cold classification helpers
3. Create access pattern visualization (optional)
4. Document access tracking overhead

### Phase 4: Testing & Optimization

**Tasks:**
1. Measure read overhead with tracking enabled
2. Optimize batch updates
3. Test concurrent access tracking
4. Performance benchmarks

---

## Implementation Details

### Access Tracking (Lightweight)

```csharp
// In FileSystemProjectionStore<TState>
private readonly AccessTracker? _accessTracker;
private readonly AccessTrackingOptions _trackingOptions;

public async Task<TState?> GetAsync(string key, CancellationToken cancellationToken)
{
    var state = await LoadProjectionAsync(key, cancellationToken);
    
    // Track access (if enabled)
    if (_trackingOptions.Enabled && state != null)
    {
        // Non-blocking: queues update for background processing
        _accessTracker?.RecordAccess(key);
    }
    
    return state;
}
```

### AccessTracker (Batched Updates)

```csharp
internal sealed class AccessTracker : IDisposable
{
    private readonly ConcurrentDictionary<string, AccessRecord> _pendingUpdates = new();
    private readonly Timer _flushTimer;
    private readonly ProjectionMetadataIndex _metadataIndex;
    private readonly AccessTrackingOptions _options;
    
    public void RecordAccess(string key)
    {
        var now = DateTimeOffset.UtcNow;
        
        _pendingUpdates.AddOrUpdate(
            key,
            _ => new AccessRecord(now, 1),
            (_, existing) =>
            {
                // Only update if > UpdateGranularity since last update
                if (now - existing.LastAccessedAt > _options.UpdateGranularity)
                {
                    return new AccessRecord(now, existing.AccessCount + 1);
                }
                return existing; // Skip update (too soon)
            });
    }
    
    private async Task FlushAsync()
    {
        if (_pendingUpdates.IsEmpty)
            return;
        
        // Drain pending updates
        var updates = _pendingUpdates.ToArray();
        _pendingUpdates.Clear();
        
        // Batch write to metadata index
        foreach (var (key, record) in updates)
        {
            var metadata = await _metadataIndex.GetAsync(key);
            if (metadata != null)
            {
                var updated = metadata with
                {
                    LastAccessedAt = record.LastAccessedAt,
                    AccessCount = record.AccessCount
                };
                await _metadataIndex.SaveAsync(key, updated);
            }
        }
        
        _logger.LogDebug("Flushed {Count} access updates", updates.Length);
    }
    
    private record AccessRecord(DateTimeOffset LastAccessedAt, long AccessCount);
}
```

### Smart Retention Evaluation

```csharp
// In ProjectionRetentionService
public async Task<IEnumerable<string>> GetProjectionsToArchiveAsync(
    ProjectionMetadataIndex metadataIndex,
    ProjectionRetentionPolicy policy)
{
    if (policy.PolicyType == RetentionPolicyType.NoRetention)
        return Enumerable.Empty<string>();
    
    var now = DateTimeOffset.UtcNow;
    var candidates = new List<string>();
    
    foreach (var (key, metadata) in metadataIndex.GetAll())
    {
        bool shouldArchive = policy.PolicyType switch
        {
            // Time-based: Simple age check (SPEC-003)
            RetentionPolicyType.DeleteAfter or RetentionPolicyType.ArchiveAfter => 
                now - metadata.LastUpdatedAt > policy.RetainFor,
            
            // Smart: Age + inactivity (NEW)
            RetentionPolicyType.SmartRetention =>
                EvaluateSmartRetention(metadata, policy, now),
            
            _ => false
        };
        
        if (shouldArchive)
            candidates.Add(key);
    }
    
    return candidates;
}

private bool EvaluateSmartRetention(
    ProjectionMetadata metadata,
    ProjectionRetentionPolicy policy,
    DateTimeOffset now)
{
    // Safety check: Must be at least MinimumAge old
    if (now - metadata.CreatedAt < policy.RetainFor)
        return false; // Too new, keep regardless of access
    
    // If never accessed (tracking not enabled or never read)
    if (!metadata.LastAccessedAt.HasValue)
    {
        // Fall back to LastUpdatedAt for inactivity
        return now - metadata.LastUpdatedAt > (policy.InactivityThreshold ?? policy.RetainFor);
    }
    
    // Hybrid logic: Old enough AND inactive
    var isOldEnough = now - metadata.CreatedAt > policy.RetainFor;
    var isInactive = now - metadata.LastAccessedAt.Value > 
        (policy.InactivityThreshold ?? TimeSpan.FromDays(90));
    
    return isOldEnough && isInactive;
}
```

---

## Testing Requirements

### Unit Tests

- [ ] `AccessTrackingOptions` default values
- [ ] `AccessTracker.RecordAccess` batching logic
- [ ] `AccessTracker.RecordAccess` respects UpdateGranularity
- [ ] Smart retention evaluation logic (age + inactivity)
- [ ] Concurrent access tracking (thread safety)

### Integration Tests

- [ ] Access tracking updates metadata correctly
- [ ] Batch flush interval works as configured
- [ ] Smart retention archives inactive projections
- [ ] Smart retention keeps frequently accessed old data
- [ ] Access tracking disabled = no overhead
- [ ] Access statistics API returns correct data

### Performance Tests

- [ ] Read overhead with tracking < 5%
- [ ] Batch updates reduce I/O significantly
- [ ] Access tracking scales to 100k+ projections

### Scenario Tests

**Scenario 1: Hot Old Data (Keep)**
```
Projection:
  - Created: 5 years ago
  - Last updated: 4 years ago
  - Last accessed: Yesterday
  
Policy:
  - MinimumAge: 3 years
  - InactivityThreshold: 90 days
  
Result: KEEP (accessed recently, still hot) ✅
```

**Scenario 2: Cold Recent Data (Archive)**
```
Projection:
  - Created: 2 years ago
  - Last updated: 1.5 years ago
  - Last accessed: Never (or > 90 days ago)
  
Policy:
  - MinimumAge: 1 year
  - InactivityThreshold: 90 days
  
Result: ARCHIVE (old enough + inactive) ✅
```

**Scenario 3: Very New Data (Safety Threshold)**
```
Projection:
  - Created: 6 months ago
  - Last updated: 6 months ago
  - Last accessed: Never
  
Policy:
  - MinimumAge: 1 year
  - InactivityThreshold: 90 days
  
Result: KEEP (too new, safety threshold) ✅
```

---

## Migration Strategy

### Enabling Access Tracking

**Step 1: Enable globally**
```csharp
services.AddOpossum(options =>
{
    options.AccessTracking.Enabled = true;
    options.AccessTracking.UpdateGranularity = TimeSpan.FromHours(1);
    options.AccessTracking.BatchFlushInterval = TimeSpan.FromMinutes(5);
});
```

**Step 2: Configure smart retention**
```csharp
services.AddProjections(projOptions =>
{
    projOptions.ConfigureSmartRetention(
        "StudentShortInfo",
        minimumAge: TimeSpan.FromYears(1),
        inactivityThreshold: TimeSpan.FromDays(90),
        configureArchive: config =>
        {
            config.ArchivePath = "D:\\Archive\\Students";
            config.Compression = CompressionLevel.Optimal;
        });
});
```

### Backward Compatibility

**Existing projections without LastAccessedAt:**
- `LastAccessedAt` defaults to `null`
- Smart retention falls back to `LastUpdatedAt` for inactivity check
- Gradually populated as projections are accessed

**Disabling tracking later:**
- Set `AccessTracking.Enabled = false`
- Existing `LastAccessedAt` values preserved
- No new updates written

---

## Dependencies

### This Feature Depends On:
- **SPEC-001:** Projection Metadata (extends ProjectionMetadata)
- **SPEC-003:** Retention Policies (adds SmartRetention policy type)

### Features That Depend On This:
- None (terminal enhancement)

---

## Open Questions

1. **Q:** Should we track read vs write access separately?
   - **A:** Deferred. MVP tracks any access (read). Future: differentiate read/write.

2. **Q:** Should access tracking be per-projection or global?
   - **A:** Global enable, but can filter statistics per projection.

3. **Q:** What if AccessTracking is enabled mid-lifecycle?
   - **A:** Existing projections have `LastAccessedAt = null`. Populated on first access.

4. **Q:** Should we support access quotas (e.g., "archive if accessed < 10 times/month")?
   - **A:** Deferred. MVP uses time-based inactivity only.

---

## Success Criteria

- [ ] Access tracking overhead < 5% on reads
- [ ] Smart retention keeps hot old data
- [ ] Smart retention archives cold new data
- [ ] Batch updates reduce I/O by 90%+
- [ ] All tests passing
- [ ] Documentation includes access pattern analysis guide
- [ ] Zero breaking changes to existing code

---

## Performance Overhead Analysis

### Without Access Tracking (Baseline)

```csharp
// Read operation
var projection = await store.GetAsync(key);
// Time: ~2ms (disk read + deserialize)
```

### With Access Tracking (Optimized)

```csharp
// Read operation
var projection = await store.GetAsync(key);
// Time: ~2.1ms (baseline + non-blocking queue operation)
// Overhead: 0.1ms (5%)

// Background (every 60 seconds):
// Flush batch: ~10ms for 1000 updates
// Amortized: 0.01ms per read
```

**Total overhead: ~0.1ms per read (5%)**

---

## Configuration Examples

### Example 1: Aggressive Archival (Cold Data)

```csharp
projOptions.ConfigureSmartRetention(
    "StudentShortInfo",
    minimumAge: TimeSpan.FromDays(180),  // 6 months minimum
    inactivityThreshold: TimeSpan.FromDays(30),  // Not accessed in 30 days
    configureArchive: config =>
    {
        config.ArchivePath = "D:\\Archive\\ColdData";
        config.Compression = CompressionLevel.SmallestSize; // Max compression
    });

// Result: Archives 6+ month old projections not accessed in 30 days
```

### Example 2: Conservative (Only Very Old + Inactive)

```csharp
projOptions.ConfigureSmartRetention(
    "AuditLog",
    minimumAge: TimeSpan.FromYears(5),  // Must be 5+ years old
    inactivityThreshold: TimeSpan.FromYears(1),  // Not accessed in 1 year
    configureArchive: config =>
    {
        config.ArchivePath = "D:\\Compliance\\Archive";
    });

// Result: Only archives truly stale audit logs
```

### Example 3: Hot Data Analysis

```csharp
// Find most accessed projections
var statistics = serviceProvider.GetRequiredService<IProjectionAccessStatistics>();

var hotData = await statistics.GetMostAccessedAsync("StudentShortInfo", count: 100);

foreach (var info in hotData)
{
    Console.WriteLine($"{info.Key}: {info.AccessCount} accesses, " +
                      $"last accessed {info.LastAccessedAt}");
}

// Use this to optimize cache warming strategy
```

---

## Future Enhancements (Not in MVP)

1. **Access Quotas:** "Archive if accessed < 10 times in last year"
2. **Access Patterns:** Track hourly/daily access patterns
3. **Predictive Archival:** ML model to predict future access
4. **Tiered Storage:** Hot → Warm → Cold → Archive tiers
5. **Access Heatmaps:** Visualize access patterns over time

---

## References

- GitHub Issue: TBD
- Discussion: Smart retention brainstorming
- Related Specs: SPEC-001 (Metadata), SPEC-003 (Retention), SPEC-002 (Cache Warming)
- Research: [LRU Cache Algorithms](https://en.wikipedia.org/wiki/Cache_replacement_policies#Least_recently_used_(LRU))
