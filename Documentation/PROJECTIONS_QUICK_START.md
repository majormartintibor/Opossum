# Opossum Projections - Quick Start Guide

## Adding a New Projection in 3 Simple Steps

### Step 1: Define Your Projection State (DTO)

```csharp
public sealed record OrderSummary(
    Guid OrderId,
    string CustomerName,
    decimal TotalAmount,
    OrderStatus Status,
    int ItemCount);
```

### Step 2: Create the Projection Definition

```csharp
using Opossum;
using Opossum.Core;
using Opossum.Projections;

namespace YourApp.Orders;

[ProjectionDefinition("OrderSummary")]
public sealed class OrderSummaryProjection : IProjectionDefinition<OrderSummary>
{
    public string ProjectionName => "OrderSummary";

    // Which events does this projection care about?
    public string[] EventTypes => new[]
    {
        nameof(OrderPlacedEvent),
        nameof(OrderItemAddedEvent),
        nameof(OrderStatusChangedEvent)
    };

    // How do we identify which projection instance to update?
    public string KeySelector(SequencedEvent evt)
    {
        var orderIdTag = evt.Event.Tags.FirstOrDefault(t => t.Key == "orderId");
        return orderIdTag?.Value ?? throw new InvalidOperationException("Missing orderId tag");
    }

    // How do we apply events to the projection?
    public OrderSummary? Apply(OrderSummary? current, IEvent evt)
    {
        return evt switch
        {
            OrderPlacedEvent placed => new OrderSummary(
                OrderId: placed.OrderId,
                CustomerName: placed.CustomerName,
                TotalAmount: 0m,
                Status: OrderStatus.Pending,
                ItemCount: 0),

            OrderItemAddedEvent itemAdded when current != null =>
                current with 
                { 
                    TotalAmount = current.TotalAmount + itemAdded.Price,
                    ItemCount = current.ItemCount + 1
                },

            OrderStatusChangedEvent statusChanged when current != null =>
                current with { Status = statusChanged.NewStatus },

            _ => current
        };
    }
}
```

### Step 3: Use in Query Handlers

```csharp
public sealed class GetOrdersQueryHandler
{
    public async Task<CommandResult<PaginatedResponse<OrderSummary>>> HandleAsync(
        GetOrdersQuery query,
        IProjectionStore<OrderSummary> projectionStore)  // ← Inject the store
    {
        // Get all orders from the projection
        var allOrders = await projectionStore.GetAllAsync();

        // Filter
        var filtered = query.Status.HasValue
            ? allOrders.Where(o => o.Status == query.Status.Value)
            : allOrders;

        // Sort
        var sorted = filtered.OrderByDescending(o => o.TotalAmount);

        // Paginate
        var paginated = sorted
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return CommandResult<PaginatedResponse<OrderSummary>>.Ok(new PaginatedResponse<OrderSummary>
        {
            Items = paginated,
            TotalCount = filtered.Count(),
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        });
    }
}
```

## Registration (Already Done in Program.cs)

```csharp
// In Program.cs or Startup.cs
builder.Services.AddOpossum(options =>
{
    options.RootPath = "D:\\Database";
    options.AddContext("MyApp");
});

// Enable projections with auto-discovery
builder.Services.AddProjections(options =>
{
    options.ScanAssembly(typeof(Program).Assembly);  // Auto-discovers [ProjectionDefinition]
    options.PollingInterval = TimeSpan.FromSeconds(5);
    options.BatchSize = 1000;
    options.EnableAutoRebuild = true;  // Builds projections on first run
});
```

## How Projections Stay Up-to-Date

1. **On Startup**: If no checkpoint exists, projections are built from ALL events
2. **Background Daemon**: Polls every 5 seconds for new events
3. **Incremental Updates**: Only applies events since last checkpoint
4. **Automatic**: No manual intervention needed!

## Debugging Projections

### Check Projection Files

```bash
# Location: {RootPath}/{Context}/Projections/{ProjectionName}/
D:\Database\MyApp\Projections\OrderSummary\
    ├── 123e4567-e89b-12d3-a456-426614174000.json
    ├── 234e5678-e89b-12d3-a456-426614174001.json
    └── ...
```

### Check Checkpoint

```bash
# Location: {RootPath}/{Context}/Projections/_checkpoints/
D:\Database\MyApp\Projections\_checkpoints\OrderSummary.checkpoint
```

Example checkpoint:
```json
{
  "projectionName": "OrderSummary",
  "lastProcessedPosition": 15432,
  "lastUpdated": "2024-01-15T10:30:00Z",
  "totalEventsProcessed": 15432
}
```

### Manual Projection Rebuild

If you need to rebuild a projection (e.g., after changing apply logic):

```csharp
// Inject IProjectionManager
public class ProjectionRebuildController
{
    private readonly IProjectionManager _projectionManager;

    public ProjectionRebuildController(IProjectionManager projectionManager)
    {
        _projectionManager = projectionManager;
    }

    [HttpPost("admin/projections/{projectionName}/rebuild")]
    public async Task<IActionResult> Rebuild(string projectionName)
    {
        await _projectionManager.RebuildAsync(projectionName);
        return Ok($"Projection '{projectionName}' rebuilt successfully");
    }
}
```

## Best Practices

### 1. ✅ Keep Projection State Simple
```csharp
// GOOD: Simple DTOs with value types
public record OrderSummary(Guid OrderId, string CustomerName, decimal Total);

// AVOID: Complex nested objects, navigation properties
public record OrderSummary(Guid OrderId, Customer Customer, List<OrderItem> Items);
```

### 2. ✅ Use Immutable Records
```csharp
// GOOD: Record with 'with' expressions
public record OrderSummary(...);

current with { TotalAmount = newAmount }

// AVOID: Mutable classes
public class OrderSummary { public decimal TotalAmount { get; set; } }
```

### 3. ✅ Handle Missing Tags Gracefully
```csharp
public string KeySelector(SequencedEvent evt)
{
    var tag = evt.Event.Tags.FirstOrDefault(t => t.Key == "orderId");
    
    if (tag == null)
    {
        throw new InvalidOperationException(
            $"Event {evt.Event.EventType} at position {evt.Position} is missing orderId tag");
    }
    
    return tag.Value;
}
```

### 4. ✅ Return null to Delete
```csharp
public OrderSummary? Apply(OrderSummary? current, IEvent evt)
{
    return evt switch
    {
        OrderDeletedEvent => null,  // ← Deletes the projection
        // ...
    };
}
```

### 5. ✅ Subscribe Only to Relevant Events
```csharp
// GOOD: Only events that affect this projection
public string[] EventTypes => new[]
{
    nameof(OrderPlacedEvent),
    nameof(OrderStatusChangedEvent)
};

// AVOID: Subscribing to all events
public string[] EventTypes => new[] { "*" };  // ❌ Don't do this
```

## Performance Tips

1. **Projection Lag**: Should be < 5 seconds. If higher, reduce `PollingInterval`
2. **Large Projections**: Consider adding `Skip()` and `Take()` in `GetAllAsync()` results
3. **Hot Path Queries**: Cache frequently accessed projections in memory
4. **Complex Filtering**: Use `QueryAsync()` for server-side filtering

## Common Patterns

### Counting Pattern (Enrollments, Order Items, etc.)
```csharp
StudentEnrolledToCourseEvent enrolled when current != null =>
    current with { CurrentEnrollmentCount = current.CurrentEnrollmentCount + 1 }
```

### Status Tracking Pattern
```csharp
OrderStatusChangedEvent statusChanged when current != null =>
    current with { Status = statusChanged.NewStatus }
```

### Soft Delete Pattern
```csharp
OrderCancelledEvent => null  // Returns null to delete projection
```

### Computed Property Pattern
```csharp
public record OrderSummary(...)
{
    public bool IsExpensive => TotalAmount > 1000;
    public bool IsPending => Status == OrderStatus.Pending;
}
```

## Troubleshooting

### Projection Not Updating?

1. **Check daemon is running**: Look for log messages from `ProjectionDaemon`
2. **Check checkpoint**: Verify `lastProcessedPosition` is advancing
3. **Check event tags**: Ensure events have the required tags for `KeySelector`
4. **Check apply logic**: Verify `Apply()` method handles all event types

### Performance Issues?

1. **Reduce polling interval**: Lower `PollingInterval` for faster updates
2. **Increase batch size**: Higher `BatchSize` for bulk processing
3. **Add indexing**: Consider in-memory caching for hot projections

### Projections Out of Sync?

1. **Rebuild**: Use `IProjectionManager.RebuildAsync()`
2. **Check event order**: Projections assume events are ordered by position
3. **Verify event types**: Ensure `EventTypes` array includes all relevant events

## Need Help?

- Check the full architecture: `Documentation/PROJECTIONS_ARCHITECTURE.md`
- Check implementation details: `Documentation/PROJECTIONS_IMPLEMENTATION_SUMMARY.md`
- Review examples: `Samples/CourseManagement/CourseShortInfo/CourseShortInfoProjection.cs`
