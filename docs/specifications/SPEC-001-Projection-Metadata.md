# SPEC-001: Projection Metadata & Indexing

**Status:** Draft  
**Created:** 2024  
**Dependencies:** None (Foundation Feature)  
**Blocked By:** None  
**Blocks:** SPEC-002 (Cache Warming), SPEC-003 (Retention Policies)

---

## Problem Statement

Projections currently have no metadata about their lifecycle:
- When was a projection created?
- When was it last updated?
- How many times has it been modified?
- How large is it?

This lack of metadata prevents intelligent features like:
- Cache warming (can't identify "recent" projections)
- Retention policies (can't identify "old" projections)
- Archiving decisions (can't identify "cold" projections)
- Storage budgeting (can't calculate total size)

---

## Requirements

### Functional Requirements

1. **FR-1:** Track creation timestamp for each projection
2. **FR-2:** Track last update timestamp for each projection
3. **FR-3:** Track version (update count) for each projection
4. **FR-4:** Track size in bytes for each projection
5. **FR-5:** Provide fast metadata queries without loading projection data
6. **FR-6:** Maintain metadata index for efficient bulk queries
7. **FR-7:** Automatically migrate existing projections (backward compatible)

### Non-Functional Requirements

1. **NFR-1:** Metadata overhead < 5% of projection size
2. **NFR-2:** Metadata queries must be O(1) or O(log n)
3. **NFR-3:** No breaking changes to existing projection storage
4. **NFR-4:** Thread-safe metadata updates

---

## Design

### Data Model

#### ProjectionMetadata Class

```csharp
namespace Opossum.Projections;

/// <summary>
/// Metadata tracked for each projection instance.
/// </summary>
public sealed record ProjectionMetadata
{
    /// <summary>
    /// When this projection was first created
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }
    
    /// <summary>
    /// When this projection was last updated
    /// </summary>
    public required DateTimeOffset LastUpdatedAt { get; init; }
    
    /// <summary>
    /// Number of times this projection has been updated (starts at 1)
    /// </summary>
    public required long Version { get; init; }
    
    /// <summary>
    /// Size of the projection file in bytes
    /// </summary>
    public required long SizeInBytes { get; init; }
}
```

#### Storage Format (Wrapped)

**New Format (with metadata):**
```json
{
  "data": {
    "studentId": "guid-1234",
    "firstName": "Alice",
    "enrollmentTier": "Premium"
  },
  "metadata": {
    "createdAt": "2024-01-15T10:30:00Z",
    "lastUpdatedAt": "2024-03-20T14:22:15Z",
    "version": 5,
    "sizeInBytes": 256
  }
}
```

**Old Format (no metadata) - Auto-migrated on read:**
```json
{
  "studentId": "guid-1234",
  "firstName": "Alice",
  "enrollmentTier": "Premium"
}
```

#### Metadata Index

**File:** `/Projections/{ProjectionName}/Metadata/index.json`

**Structure:**
```json
{
  "guid-1234": {
    "createdAt": "2024-01-15T10:30:00Z",
    "lastUpdatedAt": "2024-03-20T14:22:15Z",
    "version": 5,
    "sizeInBytes": 256
  },
  "guid-5678": {
    "createdAt": "2024-02-01T09:15:00Z",
    "lastUpdatedAt": "2024-03-19T11:45:30Z",
    "version": 12,
    "sizeInBytes": 312
  }
}
```

**Purpose:**
- Fast queries: "All projections updated since date X"
- Storage calculations: Sum of SizeInBytes
- No need to load projection data

---

## API Surface

### New Classes

```csharp
// ProjectionMetadata.cs
public sealed record ProjectionMetadata { ... }

// ProjectionWithMetadata.cs
internal sealed record ProjectionWithMetadata<TState>
{
    public required TState Data { get; init; }
    public required ProjectionMetadata Metadata { get; init; }
}

// ProjectionMetadataIndex.cs
internal sealed class ProjectionMetadataIndex
{
    Task SaveAsync(string key, ProjectionMetadata metadata);
    Task<ProjectionMetadata?> GetAsync(string key);
    Task<IReadOnlyDictionary<string, ProjectionMetadata>> GetAllAsync();
    Task<IReadOnlyList<ProjectionMetadata>> GetUpdatedSinceAsync(DateTimeOffset since);
    Task DeleteAsync(string key);
    Task ClearAsync();
}
```

### Updated Methods

```csharp
// FileSystemProjectionStore<TState>
public async Task SaveAsync(string key, TState state, CancellationToken cancellationToken)
{
    // NEW: Wrap state with metadata before saving
    // NEW: Update metadata index
}

public async Task<TState?> GetAsync(string key, CancellationToken cancellationToken)
{
    // NEW: Unwrap metadata wrapper
    // NEW: Auto-migrate old format if detected
}

public async Task DeleteAsync(string key, CancellationToken cancellationToken)
{
    // NEW: Remove from metadata index
}
```

### New Query Methods (Optional Extension)

```csharp
// IProjectionStore<TState> extensions
public static class ProjectionStoreMetadataExtensions
{
    Task<ProjectionMetadata?> GetMetadataAsync<TState>(
        this IProjectionStore<TState> store, string key);
    
    Task<IReadOnlyList<string>> GetKeysUpdatedSinceAsync<TState>(
        this IProjectionStore<TState> store, DateTimeOffset since);
    
    Task<long> GetTotalSizeBytesAsync<TState>(
        this IProjectionStore<TState> store);
}
```

---

## Implementation Phases

### Phase 1: Core Infrastructure

**Files to Create:**
- `src/Opossum/Projections/ProjectionMetadata.cs`
- `src/Opossum/Projections/ProjectionWithMetadata.cs` (internal)
- `src/Opossum/Projections/ProjectionMetadataIndex.cs` (internal)

**Files to Modify:**
- `src/Opossum/Projections/FileSystemProjectionStore.cs`

**Tasks:**
1. Create `ProjectionMetadata` record
2. Create `ProjectionWithMetadata<TState>` wrapper
3. Create `ProjectionMetadataIndex` class
4. Update `SaveAsync` to wrap and track metadata
5. Update `GetAsync` to unwrap and auto-migrate
6. Update `DeleteAsync` to clean up metadata

### Phase 2: Migration & Testing

**Tasks:**
1. Add auto-migration logic in `GetAsync`
2. Test migration of existing projections
3. Test concurrent metadata updates
4. Add integration tests for metadata index

### Phase 3: Extension Methods (Optional)

**Tasks:**
1. Create `ProjectionStoreMetadataExtensions` class
2. Add query helpers for metadata
3. Document usage examples

---

## Testing Requirements

### Unit Tests

- [ ] `ProjectionMetadata` serialization/deserialization
- [ ] `ProjectionMetadataIndex.SaveAsync` creates/updates index
- [ ] `ProjectionMetadataIndex.GetAsync` returns correct metadata
- [ ] `ProjectionMetadataIndex.GetUpdatedSinceAsync` filters correctly
- [ ] `ProjectionMetadataIndex.DeleteAsync` removes entry
- [ ] Concurrent metadata updates (thread safety)

### Integration Tests

- [ ] Save projection → metadata created and indexed
- [ ] Update projection → version incremented, lastUpdatedAt updated
- [ ] Delete projection → metadata removed from index
- [ ] Load old-format projection → auto-migrated to new format
- [ ] Restart app → metadata index persists correctly
- [ ] Query by update date → returns correct projections

### Performance Tests

- [ ] Metadata overhead < 5% of projection size
- [ ] Index loading time for 100k projections < 1 second
- [ ] Metadata query time O(1) or O(log n)

---

## Migration Strategy

### Backward Compatibility

**Scenario 1: Fresh Installation**
- All projections created with metadata from start
- No migration needed

**Scenario 2: Existing Projections (No Metadata)**
- First `GetAsync` detects old format
- Derives metadata from file system timestamps:
  - `CreatedAt` = file creation time
  - `LastUpdatedAt` = file last write time
  - `Version` = 1 (unknown)
  - `SizeInBytes` = file size
- Optionally re-saves in new format (background job)

**Scenario 3: Rebuild**
- Deleting projection folder forces full rebuild
- All projections created with metadata

### Migration Code

```csharp
public async Task<TState?> GetAsync(string key, CancellationToken cancellationToken)
{
    var json = await File.ReadAllTextAsync(filePath, cancellationToken);
    
    try
    {
        // Try new format first
        var wrapper = JsonSerializer.Deserialize<ProjectionWithMetadata<TState>>(json);
        return wrapper.Data;
    }
    catch (JsonException)
    {
        // Old format detected - migrate
        var state = JsonSerializer.Deserialize<TState>(json);
        
        var fileInfo = new FileInfo(filePath);
        var metadata = new ProjectionMetadata
        {
            CreatedAt = fileInfo.CreationTimeUtc,
            LastUpdatedAt = fileInfo.LastWriteTimeUtc,
            Version = 1,
            SizeInBytes = fileInfo.Length
        };
        
        // Cache metadata
        await _metadataIndex.SaveAsync(key, metadata);
        
        // Optionally: background re-save in new format
        _backgroundMigrationQueue.Enqueue(key);
        
        return state;
    }
}
```

---

## Dependencies

### This Feature Depends On:
- None (foundation feature)

### Features That Depend On This:
- **SPEC-002:** Cache Warming (needs LastUpdatedAt, SizeInBytes)
- **SPEC-003:** Retention Policies (needs CreatedAt, LastUpdatedAt)
- **SPEC-004:** Archiving (needs metadata for tombstones)

---

## Open Questions

1. **Q:** Should metadata index be partitioned (e.g., by year)?
   - **A:** No for MVP. Single file sufficient for < 1M projections.

2. **Q:** Should we track `LastAccessedAt`?
   - **A:** Deferred. Requires read overhead, not needed for MVP.

3. **Q:** Should migration be automatic or explicit?
   - **A:** Automatic on first read. Optional background re-save.

4. **Q:** What if metadata index is corrupted?
   - **A:** Rebuild from projection files on next load.

---

## Success Criteria

- [ ] All projections have metadata after first access
- [ ] Metadata index queries work without loading projection data
- [ ] Existing projections auto-migrate seamlessly
- [ ] All tests passing
- [ ] Documentation updated
- [ ] Zero breaking changes to existing APIs

---

## References

- GitHub Issue: TBD
- Discussion: (Link to GitHub discussion or PR)
- Related Specs: SPEC-002, SPEC-003, SPEC-004
