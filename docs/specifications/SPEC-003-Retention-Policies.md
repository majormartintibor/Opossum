# SPEC-003: Projection Retention Policies

**Status:** Draft  
**Created:** 2024  
**Dependencies:** SPEC-001 (Projection Metadata)  
**Blocked By:** None  
**Blocks:** SPEC-004 (Archiving requires retention decisions)

---

## Problem Statement

Projections accumulate over time without any lifecycle management:
- Old projections consume storage indefinitely
- No way to automatically clean up stale data
- Compliance requirements (GDPR, SOX) may require data deletion
- Performance degrades as projection count grows

We need configurable retention policies to automatically manage projection lifecycle.

---

## Requirements

### Functional Requirements

1. **FR-1:** Explicit "no retention" policy (keep forever)
2. **FR-2:** Time-based retention (delete/archive after N days)
3. **FR-3:** Per-projection policy configuration
4. **FR-4:** Global default policy for projections without specific policy
5. **FR-5:** Hard delete option (permanent removal)
6. **FR-6:** Archive option (compress and move to archive location)
7. **FR-7:** Tombstone creation to prevent daemon rebuild
8. **FR-8:** Manual policy execution (on-demand cleanup)
9. **FR-9:** Scheduled policy execution (background job)
10. **FR-10:** Audit trail of deletions/archival

### Non-Functional Requirements

1. **NFR-1:** Policy execution must be atomic (all-or-nothing per projection)
2. **NFR-2:** Policy execution must log all actions (audit trail)
3. **NFR-3:** Policy execution must handle failures gracefully
4. **NFR-4:** Tombstone prevents infinite rebuild loop

---

## Design

### Policy Types

```csharp
namespace Opossum.Projections;

/// <summary>
/// Defines how projections are retained or archived over time.
/// </summary>
public sealed class ProjectionRetentionPolicy
{
    /// <summary>
    /// Type of retention policy.
    /// Default: NoRetention (keep forever, explicit intent)
    /// </summary>
    public RetentionPolicyType PolicyType { get; set; } = RetentionPolicyType.NoRetention;
    
    /// <summary>
    /// Retain projections for this duration (based on LastUpdatedAt).
    /// Used when PolicyType is DeleteAfter or ArchiveAfter.
    /// Default: 3 years
    /// </summary>
    public TimeSpan RetainFor { get; set; } = TimeSpan.FromDays(365 * 3);
    
    /// <summary>
    /// Archive configuration (used when PolicyType = ArchiveAfter).
    /// Required if PolicyType is ArchiveAfter.
    /// </summary>
    public ArchiveConfiguration? ArchiveConfig { get; set; }
}

public enum RetentionPolicyType
{
    /// <summary>
    /// No retention policy - keep projections forever.
    /// This is EXPLICIT: we intentionally want to keep all data.
    /// Default for all projections.
    /// </summary>
    NoRetention = 0,
    
    /// <summary>
    /// Hard delete projections after RetainFor period.
    /// Creates tombstone but data is permanently removed.
    /// </summary>
    DeleteAfter = 1,
    
    /// <summary>
    /// Archive projections after RetainFor period.
    /// Compresses and moves to archive location, creates tombstone.
    /// Data can be restored later if needed.
    /// </summary>
    ArchiveAfter = 2
}
```

### Deletion Reason Tracking

```csharp
public enum DeletionReason
{
    Manual,              // User explicitly deleted via API
    RetentionPolicy,     // Deleted/archived by retention policy
    GDPR,                // Right to erasure request
    Superseded           // Replaced by newer version (future use)
}
```

### Tombstone Design

```csharp
/// <summary>
/// Marker file indicating a projection was intentionally deleted or archived.
/// Prevents ProjectionDaemon from rebuilding deleted projections.
/// </summary>
public sealed record ProjectionTombstone
{
    /// <summary>
    /// When the projection was deleted/archived
    /// </summary>
    public required DateTimeOffset DeletedAt { get; init; }
    
    /// <summary>
    /// Why the projection was deleted
    /// </summary>
    public required DeletionReason Reason { get; init; }
    
    /// <summary>
    /// Last metadata before deletion (for audit)
    /// </summary>
    public ProjectionMetadata? LastMetadata { get; init; }
    
    /// <summary>
    /// If archived (not hard deleted), path to the archive file.
    /// Null if hard deleted.
    /// </summary>
    public string? ArchivePath { get; init; }
    
    /// <summary>
    /// Retention policy that triggered this deletion/archival
    /// </summary>
    public required RetentionPolicyType PolicyType { get; init; }
}
```

**File location:** `/Projections/{ProjectionName}/{key}.tombstone`

---

## API Surface

### Configuration

```csharp
// OpossumOptions.cs
public sealed class OpossumOptions
{
    /// <summary>
    /// Default retention policy for projections that don't specify one.
    /// Default: NoRetention (keep forever)
    /// </summary>
    public ProjectionRetentionPolicy DefaultRetentionPolicy { get; } = new()
    {
        PolicyType = RetentionPolicyType.NoRetention
    };
}

// ProjectionOptions.cs
public sealed class ProjectionOptions
{
    /// <summary>
    /// Retention policies per projection type.
    /// Key: Projection name (e.g., "StudentShortInfo")
    /// Value: Retention policy for that projection
    /// </summary>
    public Dictionary<string, ProjectionRetentionPolicy> RetentionPolicies { get; } = new();
    
    /// <summary>
    /// Configures retention policy for a specific projection.
    /// </summary>
    public ProjectionOptions ConfigureRetention(
        string projectionName,
        Action<ProjectionRetentionPolicy> configure)
    {
        var policy = new ProjectionRetentionPolicy();
        configure(policy);
        RetentionPolicies[projectionName] = policy;
        return this;
    }
}
```

### Execution API

```csharp
// IProjectionRetentionService.cs
public interface IProjectionRetentionService
{
    /// <summary>
    /// Execute retention policy for a specific projection type.
    /// </summary>
    Task ExecutePolicyAsync(string projectionName, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Execute retention policies for all registered projections.
    /// </summary>
    Task ExecuteAllPoliciesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get retention statistics (projections eligible for deletion/archival).
    /// </summary>
    Task<RetentionStatistics> GetStatisticsAsync(string projectionName);
}

public sealed record RetentionStatistics
{
    public int TotalProjections { get; init; }
    public int EligibleForDeletion { get; init; }
    public int EligibleForArchival { get; init; }
    public long EstimatedSpaceToReclaim { get; init; }
}
```

### Store Extension

```csharp
// FileSystemProjectionStore<TState>
public async Task DeleteAsync(
    string key,
    DeletionReason reason = DeletionReason.Manual,
    string? archivePath = null,
    CancellationToken cancellationToken = default)
{
    // Delete projection file
    // Create tombstone with reason and optional archive path
    // Remove from metadata index
    // Remove from tag indices
}
```

---

## Implementation Phases

### Phase 1: Core Infrastructure

**Files to Create:**
- `src/Opossum/Projections/ProjectionRetentionPolicy.cs`
- `src/Opossum/Projections/RetentionPolicyType.cs`
- `src/Opossum/Projections/DeletionReason.cs`
- `src/Opossum/Projections/ProjectionTombstone.cs`
- `src/Opossum/Projections/IProjectionRetentionService.cs`
- `src/Opossum/Projections/ProjectionRetentionService.cs`

**Files to Modify:**
- `src/Opossum/Configuration/OpossumOptions.cs`
- `src/Opossum/Projections/ProjectionOptions.cs`
- `src/Opossum/Projections/FileSystemProjectionStore.cs`
- `src/Opossum/Projections/ProjectionManager.cs` (check tombstone before rebuild)

**Tasks:**
1. Create policy types and enums
2. Add configuration properties
3. Create tombstone support in `FileSystemProjectionStore`
4. Update `DeleteAsync` signature
5. Create `IProjectionRetentionService` interface
6. Implement `ProjectionRetentionService`

### Phase 2: Tombstone Integration

**Tasks:**
1. Update `ProjectionManager` to check tombstones before rebuild
2. Add tombstone creation in `DeleteAsync`
3. Test tombstone prevents rebuild loop
4. Add tombstone cleanup (optional, for very old tombstones)

### Phase 3: Policy Execution

**Tasks:**
1. Implement `ExecutePolicyAsync` for single projection
2. Implement `ExecuteAllPoliciesAsync` for all projections
3. Add logging for all deletions/archival
4. Add statistics API

### Phase 4: Testing & Documentation

**Tasks:**
1. Unit tests for policy logic
2. Integration tests for tombstone behavior
3. Test concurrent policy execution
4. Document configuration examples
5. Create migration guide

---

## Implementation Details

### ProjectionRetentionService

```csharp
internal sealed class ProjectionRetentionService : IProjectionRetentionService
{
    private readonly ILogger<ProjectionRetentionService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ProjectionOptions _projectionOptions;
    private readonly OpossumOptions _opossumOptions;
    
    public async Task ExecutePolicyAsync(
        string projectionName,
        CancellationToken cancellationToken)
    {
        // Get policy (or use default)
        if (!_projectionOptions.RetentionPolicies.TryGetValue(projectionName, out var policy))
        {
            policy = _opossumOptions.DefaultRetentionPolicy;
        }
        
        if (policy.PolicyType == RetentionPolicyType.NoRetention)
        {
            _logger.LogDebug(
                "Projection {Name} has NoRetention policy, skipping",
                projectionName);
            return;
        }
        
        // Get projection store
        var storeType = typeof(IProjectionStore<>).MakeGenericType(...);
        var store = _serviceProvider.GetRequiredService(storeType);
        
        // Load metadata index
        var metadataIndex = await store.GetMetadataIndexAsync();
        
        // Find expired projections
        var cutoffDate = DateTimeOffset.UtcNow - policy.RetainFor;
        var expired = metadataIndex
            .Where(m => m.LastUpdatedAt < cutoffDate)
            .ToList();
        
        _logger.LogInformation(
            "Executing {PolicyType} policy for {ProjectionName}: {Count} projections expired",
            policy.PolicyType, projectionName, expired.Count);
        
        // Execute policy
        foreach (var (key, metadata) in expired)
        {
            switch (policy.PolicyType)
            {
                case RetentionPolicyType.DeleteAfter:
                    await HardDeleteAsync(store, key, metadata);
                    break;
                    
                case RetentionPolicyType.ArchiveAfter:
                    // Handled by SPEC-004 (Archiving)
                    await ArchiveAsync(store, key, metadata, policy.ArchiveConfig!);
                    break;
            }
        }
    }
    
    private async Task HardDeleteAsync(
        IProjectionStore store,
        string key,
        ProjectionMetadata metadata)
    {
        _logger.LogInformation(
            "Hard deleting projection {Key} (last updated: {Date})",
            key, metadata.LastUpdatedAt);
        
        await store.DeleteAsync(
            key,
            reason: DeletionReason.RetentionPolicy,
            archivePath: null);
    }
}
```

### Tombstone Check in ProjectionManager

```csharp
// In ProjectionManager.RebuildAsync or UpdateAsync
private async Task<bool> ShouldRebuildAsync(string projectionName, string key)
{
    var store = GetStoreForProjection(projectionName);
    var tombstonePath = store.GetTombstonePath(key);
    
    if (File.Exists(tombstonePath))
    {
        _logger.LogDebug(
            "Projection {Name}/{Key} has tombstone, skipping rebuild",
            projectionName, key);
        return false; // Don't rebuild
    }
    
    return true;
}
```

---

## Testing Requirements

### Unit Tests

- [ ] `ProjectionRetentionPolicy` default values
- [ ] `RetentionPolicyType` enum values
- [ ] `DeletionReason` enum values
- [ ] `ProjectionTombstone` serialization
- [ ] Policy configuration via fluent API
- [ ] Expired projection filtering logic

### Integration Tests

- [ ] Hard delete creates tombstone
- [ ] Tombstone prevents daemon rebuild
- [ ] `ExecutePolicyAsync` deletes expired projections
- [ ] `ExecuteAllPoliciesAsync` processes all projections
- [ ] Policy respects `RetainFor` duration
- [ ] NoRetention policy keeps all projections
- [ ] Concurrent policy execution is safe

### Scenario Tests

- [ ] **Scenario:** Retention policy deletes projection → Daemon starts → Projection NOT rebuilt
- [ ] **Scenario:** Manual delete → Tombstone created → Can manually remove tombstone to allow rebuild
- [ ] **Scenario:** GDPR deletion → Tombstone created with GDPR reason

---

## Migration Strategy

No migration needed - this is a new feature.

**Default behavior:** All projections have `NoRetention` policy (keep forever).

**Enabling:** Configure retention policy per projection:
```csharp
projOptions.ConfigureRetention("StudentShortInfo", policy =>
{
    policy.PolicyType = RetentionPolicyType.DeleteAfter;
    policy.RetainFor = TimeSpan.FromYears(5);
});
```

---

## Dependencies

### This Feature Depends On:
- **SPEC-001:** Projection Metadata (needs `LastUpdatedAt` for retention decisions)

### Features That Depend On This:
- **SPEC-004:** Archiving (uses `ArchiveAfter` policy type)

---

## Open Questions

1. **Q:** Should we support custom retention logic (e.g., callbacks)?
   - **A:** Deferred. MVP uses time-based only.

2. **Q:** Should policy execution be automatic (scheduled)?
   - **A:** MVP provides manual execution API. Future: add IHostedService for scheduled execution.

3. **Q:** What if tombstone file is deleted manually?
   - **A:** Projection would rebuild on next daemon run. Document: "Don't delete tombstone files manually."

4. **Q:** Should we support "soft delete" (mark as deleted but keep file)?
   - **A:** Deferred. MVP uses hard delete or archive only.

---

## Success Criteria

- [ ] Policies can be configured per projection
- [ ] Expired projections are deleted/archived correctly
- [ ] Tombstones prevent rebuild loop
- [ ] All actions logged for audit
- [ ] All tests passing
- [ ] Documentation includes examples for common scenarios
- [ ] Zero breaking changes

---

## Configuration Examples

### Example 1: Student Data (5-Year Retention)

```csharp
projOptions.ConfigureRetention("StudentShortInfo", policy =>
{
    policy.PolicyType = RetentionPolicyType.DeleteAfter;
    policy.RetainFor = TimeSpan.FromDays(365 * 5);
    // Hard delete after 5 years (compliance requirement)
});
```

### Example 2: Audit Logs (Archive After 7 Years)

```csharp
projOptions.ConfigureRetention("AuditLog", policy =>
{
    policy.PolicyType = RetentionPolicyType.ArchiveAfter;
    policy.RetainFor = TimeSpan.FromDays(365 * 7);
    policy.ArchiveConfig = new ArchiveConfiguration
    {
        ArchivePath = "D:\\Archive\\AuditLogs"
    };
    // Archive to long-term storage after 7 years
});
```

### Example 3: No Retention (Keep Forever)

```csharp
projOptions.ConfigureRetention("CourseShortInfo", policy =>
{
    policy.PolicyType = RetentionPolicyType.NoRetention;
    // Explicit: keep instructional content forever
});
```

---

## References

- GitHub Issue: TBD
- Discussion: Retention policies brainstorming
- Related Specs: SPEC-001 (Metadata), SPEC-004 (Archiving)
