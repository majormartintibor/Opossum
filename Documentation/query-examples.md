# Query Examples - DCB Compliant

This document demonstrates how to build queries using the DCB-compliant `QueryItem` structure.

## Query Logic

### Within a QueryItem
- **Multiple EventTypes**: Combined with **OR** logic (match ANY type)
- **Multiple Tags**: Combined with **AND** logic (match ALL tags)

### Between QueryItems
- **Multiple QueryItems**: Combined with **OR** logic (match ANY item)

## Basic Query Examples

### 1. Query by Single Event Type

```csharp
var query = Query.FromEventTypes("StudentEnlistedToCourseEvent");

// Matches: Events of type "StudentEnlistedToCourseEvent"
```

### 2. Query by Multiple Event Types (OR)

```csharp
var query = Query.FromEventTypes(
    "StudentEnlistedToCourseEvent",
    "StudentWithdrawnFromCourseEvent"
);

// Matches: Events of type StudentEnlistedToCourseEvent OR StudentWithdrawnFromCourseEvent
```

### 3. Query by Tags (AND)

```csharp
var studentTag = new Tag { Key = "StudentId", Value = "12345" };
var courseTag = new Tag { Key = "CourseId", Value = "CS101" };

var query = Query.FromTags(studentTag, courseTag);

// Matches: Events that have BOTH StudentId:12345 AND CourseId:CS101
```

### 4. Query All Events

```csharp
var query = Query.All();

// Matches: ALL events in the store
```

## Advanced Query Examples

### 5. Combined Type AND Tags in Single QueryItem

```csharp
var query = new Query
{
    QueryItems = 
    [
        new QueryItem
        {
            EventTypes = ["StudentEnlistedToCourseEvent", "StudentWithdrawnFromCourseEvent"],
            Tags = 
            [
                new Tag { Key = "CourseId", Value = "CS101" }
            ]
        }
    ]
};

// Matches: Events that are:
//   (StudentEnlistedToCourseEvent OR StudentWithdrawnFromCourseEvent)
//   AND have tag CourseId:CS101
```

### 6. Multiple QueryItems (OR between items)

```csharp
var query = new Query
{
    QueryItems = 
    [
        // Item 1: Events of specific types
        new QueryItem
        {
            EventTypes = ["EventType1", "EventType2"]
        },
        
        // Item 2: Events with specific tags
        new QueryItem
        {
            Tags = 
            [
                new Tag { Key = "tag1", Value = "value1" },
                new Tag { Key = "tag2", Value = "value2" }
            ]
        }
    ]
};

// Matches: Events that are EITHER:
//   - of type EventType1 OR EventType2
//   OR
//   - have tag1:value1 AND tag2:value2
```

### 7. Complex DCB Example (from specification)

```csharp
var query = new Query
{
    QueryItems = 
    [
        // Item 1: Type-only query
        new QueryItem
        {
            EventTypes = ["EventType1", "EventType2"]
        },
        
        // Item 2: Tag-only query
        new QueryItem
        {
            Tags = 
            [
                new Tag { Key = "tag1", Value = "value1" },
                new Tag { Key = "tag2", Value = "value2" }
            ]
        },
        
        // Item 3: Combined type AND tags
        new QueryItem
        {
            EventTypes = ["EventType2", "EventType3"],
            Tags = 
            [
                new Tag { Key = "tag1", Value = "value1" },
                new Tag { Key = "tag3", Value = "value3" }
            ]
        }
    ]
};

// Matches: Events that are EITHER:
//   - of type EventType1 OR EventType2
//   OR
//   - have tag1:value1 AND tag2:value2
//   OR
//   - (of type EventType2 OR EventType3) AND (have tag1:value1 AND tag3:value3)
```

## Real-World Course Management Examples

### 8. Get All Events for a Specific Student

```csharp
var query = new Query
{
    QueryItems = 
    [
        new QueryItem
        {
            Tags = [new Tag { Key = "StudentId", Value = studentId.ToString() }]
        }
    ]
};

// Matches: All events tagged with this StudentId
```

### 9. Get Enrollment/Withdrawal Events for a Course

```csharp
var query = new Query
{
    QueryItems = 
    [
        new QueryItem
        {
            EventTypes = 
            [
                "StudentEnlistedToCourseEvent",
                "StudentWithdrawnFromCourseEvent"
            ],
            Tags = [new Tag { Key = "CourseId", Value = courseId.ToString() }]
        }
    ]
};

// Matches: Enrollment OR Withdrawal events for this specific course
```

### 10. Get All Course Capacity Events OR Student-Specific Events

```csharp
var query = new Query
{
    QueryItems = 
    [
        // All capacity-related events
        new QueryItem
        {
            EventTypes = 
            [
                "CourseReachedCapacityEvent",
                "CourseCapacityIncreasedEvent"
            ]
        },
        
        // All events for a specific student
        new QueryItem
        {
            Tags = [new Tag { Key = "StudentId", Value = studentId.ToString() }]
        }
    ]
};

// Matches: All capacity events OR all events for the specific student
```

### 11. Build Aggregate for Course Enrollment

```csharp
// To rebuild a CourseEnlistmentAggregate, we need all events related to that course
var courseId = Guid.Parse("8f4c3e2d-3c5b-4f1e-9f7d-2a5f6e8c9b12");

var query = new Query
{
    QueryItems = 
    [
        new QueryItem
        {
            Tags = [new Tag { Key = "CourseId", Value = courseId.ToString() }]
        }
    ]
};

var events = await eventStore.ReadAsync(query, null);
var aggregate = await eventStore.LoadAggregateAsync<CourseEnlistmentAggregate>(query);

// This loads all events tagged with the CourseId and rebuilds the aggregate
```

## Factory Method Examples

### Using Query.FromItems()

```csharp
var item1 = new QueryItem { EventTypes = ["Type1", "Type2"] };
var item2 = new QueryItem { Tags = [tag1, tag2] };

var query = Query.FromItems(item1, item2);

// Cleaner than manually creating Query and adding items
```

### Building Queries Programmatically

```csharp
public static Query GetStudentActivityQuery(Guid studentId, string[] eventTypes)
{
    return new Query
    {
        QueryItems = 
        [
            new QueryItem
            {
                EventTypes = eventTypes.ToList(),
                Tags = [new Tag { Key = "StudentId", Value = studentId.ToString() }]
            }
        ]
    };
}

// Usage
var query = GetStudentActivityQuery(
    studentId, 
    ["StudentEnlistedToCourseEvent", "StudentWithdrawnFromCourseEvent"]
);
```

## Query Matching Logic (Implementation Reference)

When implementing `FileSystemEventStore.ReadAsync()`, use this logic:

```csharp
public async Task<SequencedEvent[]> ReadAsync(Query query, ReadOption[]? readOptions)
{
    // If no query items, return all events
    if (query.QueryItems.Count == 0)
    {
        return await ReadAllEventsAsync();
    }
    
    var matchingEventIds = new HashSet<Guid>();
    
    // Process each QueryItem (OR logic between items)
    foreach (var queryItem in query.QueryItems)
    {
        var itemMatches = await GetMatchingEventsForQueryItem(queryItem);
        matchingEventIds.UnionWith(itemMatches);
    }
    
    return await LoadEventsById(matchingEventIds);
}

private async Task<HashSet<Guid>> GetMatchingEventsForQueryItem(QueryItem queryItem)
{
    var typeMatches = new HashSet<Guid>();
    var tagMatches = new HashSet<Guid>();
    
    // Get events matching any of the types (OR)
    if (queryItem.EventTypes.Count > 0)
    {
        foreach (var eventType in queryItem.EventTypes)
        {
            var typeEvents = await ReadEventTypeIndex(eventType);
            typeMatches.UnionWith(typeEvents);
        }
    }
    
    // Get events matching all of the tags (AND)
    if (queryItem.Tags.Count > 0)
    {
        foreach (var tag in queryItem.Tags)
        {
            var tagEvents = await ReadTagIndex(tag);
            
            if (tagMatches.Count == 0)
            {
                tagMatches.UnionWith(tagEvents);
            }
            else
            {
                tagMatches.IntersectWith(tagEvents); // AND logic
            }
        }
    }
    
    // Combine types and tags based on what's present
    if (queryItem.EventTypes.Count > 0 && queryItem.Tags.Count > 0)
    {
        // Both present: return intersection (AND)
        typeMatches.IntersectWith(tagMatches);
        return typeMatches;
    }
    else if (queryItem.EventTypes.Count > 0)
    {
        // Only types
        return typeMatches;
    }
    else
    {
        // Only tags
        return tagMatches;
    }
}
```

## JSON Representation

The queries can be serialized to JSON matching the DCB specification:

```csharp
var query = new Query
{
    QueryItems = 
    [
        new QueryItem
        {
            EventTypes = ["EventType1", "EventType2"],
            Tags = [new Tag { Key = "tag1", Value = "value1" }]
        }
    ]
};

var json = JsonSerializer.Serialize(query);

// Results in:
// {
//   "queryItems": [
//     {
//       "eventTypes": ["EventType1", "EventType2"],
//       "tags": [{ "key": "tag1", "value": "value1" }]
//     }
//   ]
// }
```

## Summary

The refactored `QueryItem` structure now fully supports the DCB specification:

✅ **OR logic** for multiple EventTypes within a QueryItem  
✅ **AND logic** for multiple Tags within a QueryItem  
✅ **Combined Type+Tag filtering** in a single QueryItem  
✅ **OR logic** between multiple QueryItems  
✅ Clean factory methods for common scenarios  
✅ Full DCB compliance
