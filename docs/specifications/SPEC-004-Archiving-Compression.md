# SPEC-004: Projection Archiving & Compression

**Status:** Draft  
**Created:** 2024  
**Dependencies:** SPEC-001 (Metadata), SPEC-003 (Retention Policies)  
**Blocked By:** None  
**Blocks:** None

---

## Problem Statement

Long-term data storage presents challenges:
- Old projections consume expensive fast storage (SSD)
- Compliance may require data retention beyond active use
- Storage costs grow linearly with data volume
- No built-in way to move data to cheaper storage tiers

We need archiving to move old projections to compressed storage while:
- Maintaining audit trail (tombstone with archive path)
- Enabling restore if needed
- Preventing daemon from rebuilding archived data

---

## Requirements

### Functional Requirements

1. **FR-1:** Compress projection to ZIP format
2. **FR-2:** Move compressed archive to configurable path
3. **FR-3:** Create tombstone with archive location
4. **FR-4:** Support restore from archive
5. **FR-5:** Configurable compression level
6. **FR-6:** Include metadata in archive (audit trail)
7. **FR-7:** Use built-in .NET compression (no external dependencies)
8. **FR-8:** Configurable archive path per projection
9. **FR-9:** Global default archive path
10. **FR-10:** Archive folder structure by date (year/month)

### Non-Functional Requirements

1. **NFR-1:** Compression ratio > 70% for typical JSON data
2. **NFR-2:** Archive operation is atomic (all-or-nothing)
3. **NFR-3:** Archive failure leaves projection intact
4. **NFR-4:** Cross-platform (Windows, Linux, macOS)
5. **NFR-5:** No external dependencies (use System.IO.Compression)

---

## Design

### Archive Configuration

```csharp
namespace Opossum.Projections;

/// <summary>
/// Configuration for projection archival.
/// </summary>
public sealed class ArchiveConfiguration
{
    /// <summary>
    /// Root path where archived projections are stored.
    /// Structure: {ArchivePath}/{ProjectionName}/{Year}/{Month}/{key}.zip
    /// Example: D:\Archive\Opossum\StudentShortInfo\2024\03\guid-123.zip
    /// </summary>
    public required string ArchivePath { get; set; }
    
    /// <summary>
    /// Compression level.
    /// Uses built-in System.IO.Compression.CompressionLevel enum.
    /// Default: Optimal (balanced speed/compression)
    /// </summary>
    public CompressionLevel Compression { get; set; } = CompressionLevel.Optimal;
    
    /// <summary>
    /// Include metadata in archive (for audit trail).
    /// Default: true
    /// </summary>
    public bool IncludeMetadata { get; set; } = true;
    
    /// <summary>
    /// Archive filename pattern. Variables: {key}, {projectionName}, {date}
    /// Default: "{key}" → produces "guid-123.zip"
    /// </summary>
    public string ArchiveNamePattern { get; set; } = "{key}";
}
```

### Integration with OpossumOptions

```csharp
namespace Opossum.Configuration;

public sealed class OpossumOptions
{
    /// <summary>
    /// Global archive path for all projections (can be overridden per projection).
    /// Example: "D:\\Archive\\Opossum" or "/mnt/archive/opossum"
    /// </summary>
    public string? GlobalArchivePath { get; set; }
}
```

### Archive Folder Structure

```
{ArchivePath}/
  ├── {ProjectionName}/
  │   ├── {Year}/
  │   │   ├── {Month}/
  │   │   │   ├── guid-123.zip
  │   │   │   ├── guid-456.zip
  │   │   │   └── ...

Example:
D:\Archive\Opossum\
  ├── StudentShortInfo/
  │   ├── 2024/
  │   │   ├── 01/
  │   │   │   ├── student-001.zip
  │   │   │   └── student-002.zip
  │   │   ├── 02/
  │   │   │   └── student-003.zip
  │   └── 2023/
  │       └── 12/
  │           └── student-004.zip
```

### Archive Contents (ZIP)

```
{key}.zip
├── projection.json      ← Projection data
└── metadata.json        ← Metadata (if IncludeMetadata = true)
```

---

## API Surface

### New Classes

```csharp
// IProjectionArchiver.cs
namespace Opossum.Projections;

public interface IProjectionArchiver
{
    /// <summary>
    /// Archive a projection to compressed storage.
    /// </summary>
    Task<string> ArchiveAsync(
        string projectionPath,
        string key,
        ProjectionMetadata metadata,
        ArchiveConfiguration config,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Restore a projection from archive.
    /// </summary>
    Task RestoreAsync(
        string archivePath,
        string projectionPath,
        string key,
        CancellationToken cancellationToken = default);
}

// ProjectionArchiver.cs (internal implementation)
internal sealed class ProjectionArchiver : IProjectionArchiver
{
    // Implementation using System.IO.Compression.ZipFile
}
```

### Updated Tombstone

```csharp
public sealed record ProjectionTombstone
{
    public required DateTimeOffset DeletedAt { get; init; }
    public required DeletionReason Reason { get; init; }
    public ProjectionMetadata? LastMetadata { get; init; }
    
    /// <summary>
    /// Path to archive file (null if hard deleted).
    /// Example: "D:\Archive\Opossum\StudentShortInfo\2024\03\guid-123.zip"
    /// </summary>
    public string? ArchivePath { get; init; }
    
    public required RetentionPolicyType PolicyType { get; init; }
}
```

### Store Extension

```csharp
// IProjectionStore<TState> extension
public static class ProjectionStoreArchiveExtensions
{
    /// <summary>
    /// Restore a projection from archive (removes tombstone).
    /// </summary>
    public static Task RestoreFromArchiveAsync<TState>(
        this IProjectionStore<TState> store,
        string key,
        CancellationToken cancellationToken = default);
}
```

---

## Implementation Phases

### Phase 1: Core Archive Infrastructure

**Files to Create:**
- `src/Opossum/Projections/ArchiveConfiguration.cs`
- `src/Opossum/Projections/IProjectionArchiver.cs`
- `src/Opossum/Projections/ProjectionArchiver.cs`

**Files to Modify:**
- `src/Opossum/Configuration/OpossumOptions.cs`
- `src/Opossum/Projections/ProjectionTombstone.cs` (add ArchivePath)

**Tasks:**
1. Create `ArchiveConfiguration` class
2. Create `IProjectionArchiver` interface
3. Implement `ProjectionArchiver` using `System.IO.Compression.ZipFile`
4. Add `GlobalArchivePath` to `OpossumOptions`
5. Update `ProjectionTombstone` to include archive path

### Phase 2: Retention Policy Integration

**Files to Modify:**
- `src/Opossum/Projections/ProjectionRetentionService.cs`
- `src/Opossum/Projections/FileSystemProjectionStore.cs`

**Tasks:**
1. Implement `ArchiveAfter` policy execution
2. Call archiver before deletion
3. Create tombstone with archive path
4. Test archive + tombstone creation

### Phase 3: Restore Capability

**Files to Create:**
- `src/Opossum/Projections/ProjectionStoreArchiveExtensions.cs`

**Tasks:**
1. Implement `RestoreFromArchiveAsync`
2. Extract from ZIP
3. Remove tombstone
4. Rebuild metadata and indices
5. Test full archive → restore cycle

### Phase 4: Testing & Documentation

**Tasks:**
1. Unit tests for archiver
2. Integration tests for archive/restore
3. Test compression ratios
4. Document configuration examples
5. Performance benchmarks

---

## Implementation Details

### ProjectionArchiver

```csharp
using System.IO.Compression;

internal sealed class ProjectionArchiver : IProjectionArchiver
{
    private readonly ILogger<ProjectionArchiver> _logger;
    
    public async Task<string> ArchiveAsync(
        string projectionPath,
        string key,
        ProjectionMetadata metadata,
        ArchiveConfiguration config,
        CancellationToken cancellationToken)
    {
        // Build archive directory structure
        var projectionName = Path.GetFileName(Path.GetDirectoryName(projectionPath)!);
        var archiveDir = Path.Combine(
            config.ArchivePath,
            projectionName,
            metadata.LastUpdatedAt.Year.ToString(),
            metadata.LastUpdatedAt.Month.ToString("D2"));
        
        Directory.CreateDirectory(archiveDir);
        
        // Build archive filename
        var archiveName = config.ArchiveNamePattern
            .Replace("{key}", key)
            .Replace("{projectionName}", projectionName)
            .Replace("{date}", metadata.LastUpdatedAt.ToString("yyyy-MM-dd"));
        
        var archivePath = Path.Combine(archiveDir, $"{archiveName}.zip");
        
        // Create ZIP archive
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            // Add projection data
            var projectionFile = Path.Combine(projectionPath, $"{key}.json");
            
            archive.CreateEntryFromFile(
                projectionFile,
                "projection.json",
                config.Compression);
            
            // Optionally include metadata
            if (config.IncludeMetadata)
            {
                var metadataJson = JsonSerializer.Serialize(metadata);
                var metadataEntry = archive.CreateEntry("metadata.json", config.Compression);
                
                using (var writer = new StreamWriter(metadataEntry.Open()))
                {
                    await writer.WriteAsync(metadataJson);
                }
            }
        }
        
        _logger.LogInformation(
            "Archived projection {Key} to {Path} (compression: {Level})",
            key, archivePath, config.Compression);
        
        return archivePath;
    }
    
    public async Task RestoreAsync(
        string archivePath,
        string projectionPath,
        string key,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException($"Archive not found: {archivePath}");
        
        // Extract projection from ZIP
        var projectionFile = Path.Combine(projectionPath, $"{key}.json");
        
        using (var archive = ZipFile.OpenRead(archivePath))
        {
            var entry = archive.GetEntry("projection.json")
                ?? throw new InvalidOperationException("Archive missing projection.json");
            
            entry.ExtractToFile(projectionFile, overwrite: true);
        }
        
        _logger.LogInformation(
            "Restored projection {Key} from archive {Path}",
            key, archivePath);
    }
}
```

### Retention Service Integration

```csharp
// In ProjectionRetentionService
private async Task ArchiveAsync(
    IProjectionStore store,
    string key,
    ProjectionMetadata metadata,
    ArchiveConfiguration config)
{
    _logger.LogInformation(
        "Archiving projection {Key} (last updated: {Date})",
        key, metadata.LastUpdatedAt);
    
    // Archive projection
    var archivePath = await _archiver.ArchiveAsync(
        store.ProjectionPath,
        key,
        metadata,
        config);
    
    // Delete original with tombstone referencing archive
    await store.DeleteAsync(
        key,
        reason: DeletionReason.RetentionPolicy,
        archivePath: archivePath);
    
    _logger.LogInformation(
        "Successfully archived {Key} to {Archive}",
        key, archivePath);
}
```

### Restore Extension

```csharp
public static async Task RestoreFromArchiveAsync<TState>(
    this IProjectionStore<TState> store,
    string key,
    CancellationToken cancellationToken = default)
{
    var tombstonePath = store.GetTombstonePath(key);
    
    if (!File.Exists(tombstonePath))
        throw new InvalidOperationException($"No tombstone found for {key}");
    
    var json = await File.ReadAllTextAsync(tombstonePath, cancellationToken);
    var tombstone = JsonSerializer.Deserialize<ProjectionTombstone>(json)!;
    
    if (tombstone.ArchivePath == null)
        throw new InvalidOperationException($"Projection {key} was hard deleted, cannot restore");
    
    // Restore from archive
    await _archiver.RestoreAsync(
        tombstone.ArchivePath,
        store.ProjectionPath,
        key,
        cancellationToken);
    
    // Remove tombstone
    File.Delete(tombstonePath);
    
    // Projection will be re-indexed on next read
    _logger.LogInformation("Restored projection {Key} from archive", key);
}
```

---

## Testing Requirements

### Unit Tests

- [ ] `ArchiveConfiguration` default values
- [ ] Archive path structure generation
- [ ] Archive filename pattern substitution
- [ ] Compression level validation

### Integration Tests

- [ ] Archive creates ZIP with projection data
- [ ] Archive includes metadata if configured
- [ ] Archive uses correct compression level
- [ ] Restore extracts projection correctly
- [ ] Restore removes tombstone
- [ ] Archive → Restore → Projection readable
- [ ] Tombstone prevents rebuild after archive

### Compression Tests

- [ ] Measure compression ratio for typical JSON (expect > 70%)
- [ ] Verify CompressionLevel.Optimal vs SmallestSize difference
- [ ] Test archive sizes for various projection sizes

---

## Migration Strategy

No migration needed - this is a new feature.

**Enabling archival:**
```csharp
projOptions.ConfigureRetention("StudentShortInfo", policy =>
{
    policy.PolicyType = RetentionPolicyType.ArchiveAfter;
    policy.RetainFor = TimeSpan.FromYears(3);
    policy.ArchiveConfig = new ArchiveConfiguration
    {
        ArchivePath = "D:\\Archive\\Students"
    };
});
```

---

## Dependencies

### This Feature Depends On:
- **SPEC-001:** Projection Metadata (for archive metadata)
- **SPEC-003:** Retention Policies (defines `ArchiveAfter` policy type)

### Features That Depend On This:
- None (terminal feature)

---

## Open Questions

1. **Q:** Should we support incremental archival (only changed projections)?
   - **A:** Deferred. MVP archives entire projection each time.

2. **Q:** Should we support archive encryption?
   - **A:** Deferred. User can encrypt archive folder at OS level.

3. **Q:** Should we support archive to cloud storage (S3, Azure Blob)?
   - **A:** Deferred. MVP uses local file system only. Future: pluggable archive backends.

4. **Q:** What if archive folder is on slower storage (HDD vs SSD)?
   - **A:** Expected use case. Archives typically go to cheaper, slower storage.

---

## Success Criteria

- [ ] Archives compress to < 30% of original size
- [ ] Archive operation is atomic (all-or-nothing)
- [ ] Restore fully reconstructs projection
- [ ] Tombstone prevents rebuild loop
- [ ] All tests passing
- [ ] Documentation includes compression benchmarks
- [ ] Zero external dependencies (uses System.IO.Compression)

---

## Configuration Examples

### Example 1: Simple Archive

```csharp
services.AddOpossum(options =>
{
    options.GlobalArchivePath = "D:\\Archive\\Opossum";
});

services.AddProjections(projOptions =>
{
    projOptions.ConfigureRetention("StudentShortInfo", policy =>
    {
        policy.PolicyType = RetentionPolicyType.ArchiveAfter;
        policy.RetainFor = TimeSpan.FromYears(5);
        policy.ArchiveConfig = new ArchiveConfiguration
        {
            // Uses global archive path
            ArchivePath = "D:\\Archive\\Opossum",
            Compression = CompressionLevel.Optimal
        };
    });
});
```

### Example 2: Per-Projection Archive Path

```csharp
projOptions.ConfigureRetention("AuditLog", policy =>
{
    policy.PolicyType = RetentionPolicyType.ArchiveAfter;
    policy.RetainFor = TimeSpan.FromYears(7);
    policy.ArchiveConfig = new ArchiveConfiguration
    {
        // Override: Use dedicated audit archive
        ArchivePath = "D:\\Compliance\\AuditArchive",
        Compression = CompressionLevel.SmallestSize, // Max compression
        IncludeMetadata = true // Required for compliance
    };
});
```

### Example 3: Restore from Archive

```csharp
// Manual restore
var projectionStore = serviceProvider
    .GetRequiredService<IProjectionStore<StudentShortInfo>>();

await projectionStore.RestoreFromArchiveAsync("student-123");

// Projection now accessible normally
var student = await projectionStore.GetAsync("student-123");
```

---

## Compression Benchmarks

**Expected Results (Typical JSON Projection):**

| Original Size | CompressionLevel | Compressed Size | Ratio | Time |
|---------------|------------------|-----------------|-------|------|
| 1 MB | Optimal | 250 KB | 75% | ~50ms |
| 1 MB | SmallestSize | 200 KB | 80% | ~100ms |
| 1 MB | Fastest | 300 KB | 70% | ~20ms |
| 10 MB | Optimal | 2.5 MB | 75% | ~500ms |

**Storage Savings Example:**
- 10,000 projections × 500 KB each = 5 GB
- After archival: 10,000 × 125 KB = 1.25 GB
- **Savings: 3.75 GB (75%)**

---

## References

- GitHub Issue: TBD
- Discussion: Archiving & compression brainstorming
- Related Specs: SPEC-001 (Metadata), SPEC-003 (Retention)
- .NET Docs: [System.IO.Compression.ZipFile](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.zipfile)
