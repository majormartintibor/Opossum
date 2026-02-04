# Projection Tag Indexing Feature

## Overview

Tag-based indexing for projections enables efficient querying without loading all projections into memory. This feature allows you to filter projections by tagged attributes, similar to how events can be queried by tags.

## Problem Solved

**Before:**
```csharp
// ❌ Loads ALL students into memory
var allStudents = await projectionStore.GetAllAsync();
var premiumStudents = allStudents.Where(s => s.EnrollmentTier == Tier.Premium);
```

**After:**
```csharp
// ✅ Only loads Premium students from index
var premiumStudents = await projectionStore.QueryByTagAsync(
    new Tag { Key = "EnrollmentTier", Value = "Premium" });
```

## Usage

### 1. Create a Tag Provider

Define which properties should be indexed:

```csharp
public sealed class StudentShortInfoTagProvider : IProjectionTagProvider<StudentShortInfo>
{
    public IEnumerable<Tag> GetTags(StudentShortInfo state)
    {
        // Index by enrollment tier
        yield return new Tag 
        { 
            Key = "EnrollmentTier", 
            Value = state.EnrollmentTier.ToString() 
        };
        
        // Index by maxed-out status
        yield return new Tag 
        { 
            Key = "IsMaxedOut", 
            Value = state.IsMaxedOut.ToString() 
        };
    }
}
```

### 2. Register Tag Provider on Projection

Add the `[ProjectionTags]` attribute:

```csharp
[ProjectionDefinition("StudentShortInfo")]
[ProjectionTags(typeof(StudentShortInfoTagProvider))]  // ← Add this
public sealed class StudentShortInfoProjection : IProjectionDefinition<StudentShortInfo>
{
    // ...
}
```

### 3. Query Using Tags

#### Single Tag Query

```csharp
// Get all Premium students
var premiumStudents = await projectionStore.QueryByTagAsync(
    new Tag { Key = "EnrollmentTier", Value = "Premium" });
```

#### Multi-Tag Query (AND Logic)

```csharp
// Get Premium students who are maxed out
var tags = new[]
{
    new Tag { Key = "EnrollmentTier", Value = "Premium" },
    new Tag { Key = "IsMaxedOut", Value = "True" }
};

var results = await projectionStore.QueryByTagsAsync(tags);
```

#### Conditional Query

```csharp
// Use tags if filters are specified, otherwise get all
IReadOnlyList<StudentShortInfo> students;

if (query.TierFilter.HasValue)
{
    students = await projectionStore.QueryByTagAsync(
        new Tag { Key = "EnrollmentTier", Value = query.TierFilter.Value.ToString() });
}
else
{
    students = await projectionStore.GetAllAsync();
}
```

## Index File Structure

Indices are stored per projection type:

```
/Database/OpossumSampleApp/Projections/
  /StudentShortInfo/
    /{studentId}.json                          ← Projection files
    /Indices/                                   ← Tag indices
      /EnrollmentTier_Basic.json               ← ["guid1", "guid2", ...]
      /EnrollmentTier_Premium.json
      /EnrollmentTier_Enterprise.json
      /IsMaxedOut_True.json
      /IsMaxedOut_False.json
  /CourseShortInfo/
    /{courseId}.json
    /Indices/
      /IsFull_True.json
      /IsFull_False.json
```

## Architecture

### Components

1. **`IProjectionTagProvider<TState>`**
   - Interface for extracting tags from projection state
   - Implemented by developers for each projection

2. **`ProjectionTagsAttribute`**
   - Declarative attribute for registering tag providers
   - Auto-discovered during assembly scanning

3. **`ProjectionTagIndex`**
   - Internal class managing index files
   - Thread-safe operations
   - Atomic updates when tags change

4. **`FileSystemProjectionStore<TState>`**
   - Injected with tag provider instance
   - Automatically maintains indices on save/delete
   - Provides query methods

### Index Lifecycle

#### Projection Save

```csharp
await store.SaveAsync(key, projection);

// Internally:
// 1. Extract tags: tagProvider.GetTags(projection)
// 2. Compare with old tags (if exists)
// 3. Remove from old tag indices
// 4. Add to new tag indices
// 5. Save projection file
```

#### Projection Update (Tags Changed)

```csharp
// Student tier changes from Basic → Premium
projection = projection with { EnrollmentTier = Tier.Premium };
await store.SaveAsync(key, projection);

// Internally:
// 1. Remove from "EnrollmentTier_Basic.json"
// 2. Add to "EnrollmentTier_Premium.json"
```

#### Projection Delete

```csharp
await store.DeleteAsync(key);

// Internally:
// 1. Remove from all tag indices
// 2. Delete projection file
```

#### Rebuild

```csharp
await projectionManager.RebuildAsync("StudentShortInfo");

// Internally:
// 1. Delete /Indices folder
// 2. Delete checkpoint
// 3. Delete projection files
// 4. Replay all events
// 5. Indices rebuilt automatically during SaveAsync
```

## Performance Characteristics

### Query Performance

| Operation | Before (GetAllAsync) | After (QueryByTagAsync) |
|-----------|---------------------|------------------------|
| Filter 10 from 10,000 | O(10,000) | O(10) |
| Filter 100 from 100,000 | O(100,000) | O(100) |
| Multi-tag filter | O(n) + filter | O(intersection) |

### Index Maintenance

- **Save**: O(tags) - constant time per tag
- **Update**: O(changed tags) - only updates differences
- **Delete**: O(tags) - removes from all indices
- **Rebuild**: O(n × tags) - rebuilds all indices

## Best Practices

### 1. Choose Indexable Properties Wisely

✅ **Good candidates:**
- Enum values (Status, Tier, Category)
- Boolean flags (IsActive, IsFull, IsMaxedOut)
- Low-cardinality strings (Type, Region)

❌ **Poor candidates:**
- High-cardinality values (Email, Name, Description)
- Frequently changing values
- Computed values that change often

### 2. Tag Naming Conventions

```csharp
// ✅ Clear, descriptive keys
new Tag { Key = "EnrollmentTier", Value = "Premium" }
new Tag { Key = "IsMaxedOut", Value = "True" }

// ❌ Ambiguous keys
new Tag { Key = "tier", Value = "premium" }
new Tag { Key = "Status1", Value = "1" }
```

### 3. Case Sensitivity

- Tags are stored **case-sensitive**
- Queries are **case-insensitive**
- Use consistent casing in tag providers for clarity

### 4. Multi-Tag Queries

- Multiple tags use AND logic (intersection)
- Order smallest index first for performance
- Query is optimized automatically

### 5. Error Handling

Tag extraction failures fail-fast:

```csharp
public IEnumerable<Tag> GetTags(StudentShortInfo state)
{
    // ✅ This will throw if state is invalid
    // Better to fail during save than have inconsistent indices
    yield return new Tag 
    { 
        Key = "EnrollmentTier", 
        Value = state.EnrollmentTier.ToString() 
    };
}
```

## Migration Guide

### Enabling Tags for Existing Projections

1. **Create tag provider:**
   ```csharp
   public sealed class MyProjectionTagProvider : IProjectionTagProvider<MyProjection>
   {
       public IEnumerable<Tag> GetTags(MyProjection state)
       {
           yield return new Tag { Key = "Status", Value = state.Status };
       }
   }
   ```

2. **Add attribute to projection:**
   ```csharp
   [ProjectionDefinition("MyProjection")]
   [ProjectionTags(typeof(MyProjectionTagProvider))]
   public sealed class MyProjectionDefinition : IProjectionDefinition<MyProjection>
   ```

3. **Rebuild projection:**
   ```bash
   # Delete projection folder to force rebuild
   rm -rf /Database/OpossumSampleApp/Projections/MyProjection
   
   # Restart application - daemon will rebuild with indices
   ```

4. **Update query handlers:**
   ```csharp
   // Before
   var all = await store.GetAllAsync();
   var filtered = all.Where(x => x.Status == "Active");

   // After
   var filtered = await store.QueryByTagAsync(
       new Tag { Key = "Status", Value = "Active" });
   ```

## Testing

### Unit Testing Tag Providers

```csharp
[Fact]
public void GetTags_ExtractsCorrectTags()
{
    // Arrange
    var provider = new StudentShortInfoTagProvider();
    var student = new StudentShortInfo(
        StudentId: Guid.NewGuid(),
        FirstName: "Alice",
        LastName: "Smith",
        Email: "alice@test.com",
        EnrollmentTier: Tier.Premium,
        CurrentEnrollmentCount: 5,
        MaxEnrollmentCount: 5);

    // Act
    var tags = provider.GetTags(student).ToList();

    // Assert
    Assert.Equal(2, tags.Count);
    Assert.Contains(tags, t => t.Key == "EnrollmentTier" && t.Value == "Premium");
    Assert.Contains(tags, t => t.Key == "IsMaxedOut" && t.Value == "True");
}
```

### Integration Testing Tag Queries

See `tests\Opossum.IntegrationTests\Projections\ProjectionTagQueryTests.cs` for comprehensive examples.

## Troubleshooting

### Indices Not Being Created

**Symptom:** Queries return empty results

**Causes:**
1. Tag provider not registered (missing `[ProjectionTags]` attribute)
2. Tag provider not implementing `IProjectionTagProvider<TState>`
3. Projection not rebuilt after adding tags

**Solution:** Delete projection folder and restart to force rebuild

### Stale Indices

**Symptom:** Query results don't match actual projection data

**Causes:**
1. Manual file edits bypassing the store
2. Interrupted rebuild

**Solution:** 
```csharp
await projectionManager.RebuildAsync("ProjectionName");
```

### Performance Issues

**Symptom:** Tag queries slower than expected

**Causes:**
1. Too many tags per projection (index overhead)
2. High-cardinality tag values
3. Queries returning large result sets

**Solution:** Review tag strategy, consider fewer indices

## Future Enhancements

Potential future improvements:
- OR logic for multi-tag queries
- Tag value ranges (e.g., `Value > 100`)
- Composite tag keys
- Index statistics and monitoring
- Automatic index rebuilding on corruption detection

## Related Documentation

- [Event Tag Indexing](../Storage/EventTagIndexing.md)
- [Projection System](../Projections/README.md)
- [Query Patterns](../Patterns/QueryPatterns.md)
