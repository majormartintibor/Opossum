using Opossum.BenchmarkTests.Helpers;

namespace Opossum.BenchmarkTests.Core;

/// <summary>
/// Benchmarks for complex query operations
/// Tests advanced query patterns, combinations, and filtering scenarios
/// </summary>
[Config(typeof(OpossumBenchmarkConfig))]
[MemoryDiagnoser]
public class QueryBenchmarks
{
    private TempFileSystemHelper _tempHelper = null!;
    private string _storePath = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _tempHelper = new TempFileSystemHelper("QueryBench");

        // Create and populate store with 2K events (good middle ground)
        _storePath = CreateAndPopulateStore(2000);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _tempHelper?.Dispose();
    }

    private string CreateAndPopulateStore(int eventCount)
    {
        var path = _tempHelper.CreateSubDirectory($"Store_{eventCount}Events");
        var options = new OpossumOptions { RootPath = path, FlushEventsImmediately = false };
        options.UseStore("BenchmarkContext");

        var services = new ServiceCollection();
        services.AddOpossum(opt =>
        {
            opt.RootPath = path;
            opt.FlushEventsImmediately = false;
            opt.UseStore("BenchmarkContext");
        });

        using var sp = services.BuildServiceProvider();
        var store = sp.GetRequiredService<IEventStore>();

        // Populate with events
        var events = BenchmarkDataGenerator.GenerateEvents(
            eventCount,
            tagCount: 5,
            eventTypes: ["OrderCreated", "OrderShipped", "OrderDelivered", "OrderCancelled", "PaymentProcessed"]);

        // Batch append
        const int batchSize = 100;
        for (int i = 0; i < events.Count; i += batchSize)
        {
            var batch = events.Skip(i).Take(batchSize).ToArray();
            store.AppendAsync(batch, null).GetAwaiter().GetResult();
        }

        return path;
    }

    private IEventStore CreateStore()
    {
        var services = new ServiceCollection();
        services.AddOpossum(opt =>
        {
            opt.RootPath = _storePath;
            opt.FlushEventsImmediately = false;
            opt.UseStore("BenchmarkContext");
        });

        return services.BuildServiceProvider().GetRequiredService<IEventStore>();
    }

    // ========================================================================
    // Complex Queries - EventType + Tag Combinations
    // ========================================================================

    /// <summary>
    /// Complex query: Single EventType + Single Tag
    /// </summary>
    [Benchmark(Baseline = true, Description = "EventType + Single Tag")]
    public async Task Query_EventType_And_SingleTag()
    {
        var store = CreateStore();

        var query = Query.FromItems([
            new QueryItem
            {
                EventTypes = ["OrderCreated"],
                Tags = [new Tag("Region", "US-West")]
            }
        ]);

        var results = await store.ReadAsync(query, null);
    }

    /// <summary>
    /// Complex query: Multiple EventTypes + Multiple Tags
    /// </summary>
    [Benchmark(Description = "Multiple EventTypes + Multiple Tags")]
    public async Task Query_MultipleEventTypes_And_MultipleTags()
    {
        var store = CreateStore();

        var query = Query.FromItems([
            new QueryItem
            {
                EventTypes = ["OrderCreated", "OrderShipped"],
                Tags = [
                    new Tag("Region", "US-West"),
                    new Tag("Environment", "Production")
                ]
            }
        ]);

        var results = await store.ReadAsync(query, null);
    }

    // ========================================================================
    // Query with Ordering
    // ========================================================================

    /// <summary>
    /// Query with descending order (newest first)
    /// </summary>
    [Benchmark(Description = "Query with Descending order")]
    public async Task Query_WithDescendingOrder()
    {
        var store = CreateStore();

        var query = Query.FromEventTypes(["OrderCreated", "OrderShipped"]);

        var results = await store.ReadAsync(query, [ReadOption.Descending]);
    }

    // ========================================================================
    // Selectivity Testing
    // ========================================================================

    /// <summary>
    /// Query with high selectivity (few matches)
    /// Uses a very specific tag that matches ~1% of events
    /// </summary>
    [Benchmark(Description = "High selectivity (few matches)")]
    public async Task Query_HighSelectivity_FewMatches()
    {
        var store = CreateStore();

        // Very specific query - should match ~1% of events
        var query = Query.FromItems([
            new QueryItem
            {
                EventTypes = ["OrderCancelled"], // ~20% of events
                Tags = [
                    new Tag("Priority", "High"), // ~10% of those
                    new Tag("Status", "Active") // ~10% of those
                ]
                // Result: ~0.2% of events (very selective)
            }
        ]);

        var results = await store.ReadAsync(query, null);
    }

    /// <summary>
    /// Query with low selectivity (many matches)
    /// Uses broad criteria that matches ~75% of events
    /// </summary>
    [Benchmark(Description = "Low selectivity (many matches)")]
    public async Task Query_LowSelectivity_ManyMatches()
    {
        var store = CreateStore();

        // Broad query - should match ~75% of events
        var query = Query.FromEventTypes([
            "OrderCreated",
            "OrderShipped",
            "OrderDelivered" // 3 out of 4 event types
        ]);

        var results = await store.ReadAsync(query, null);
    }

    // ========================================================================
    // Multiple QueryItems (OR Logic Between Items)
    // ========================================================================

    /// <summary>
    /// Multiple QueryItems with OR logic between them
    /// Item 1: OrderCreated in US-West
    /// OR
    /// Item 2: OrderShipped in Production
    /// </summary>
    [Benchmark(Description = "Multiple QueryItems (OR logic)")]
    public async Task Query_MultipleQueryItems_OrLogic()
    {
        var store = CreateStore();

        var query = Query.FromItems([
            // Item 1: OrderCreated in US-West
            new QueryItem
            {
                EventTypes = ["OrderCreated"],
                Tags = [new Tag("Region", "US-West")]
            },
            // OR
            // Item 2: OrderShipped in Production
            new QueryItem
            {
                EventTypes = ["OrderShipped"],
                Tags = [new Tag("Environment", "Production")]
            }
        ]);

        var results = await store.ReadAsync(query, null);
    }

    // ========================================================================
    // Complex Real-World Scenarios
    // ========================================================================

    /// <summary>
    /// Real-world scenario: Get all payment-related events for a specific tenant
    /// </summary>
    [Benchmark(Description = "Real-world: Payment events for tenant")]
    public async Task Query_RealWorld_PaymentEventsForTenant()
    {
        var store = CreateStore();

        var query = Query.FromItems([
            new QueryItem
            {
                EventTypes = ["PaymentProcessed", "OrderCreated"],
                Tags = [
                    new Tag("Tenant", "Tenant123"),
                    new Tag("Environment", "Production")
                ]
            }
        ]);

        var results = await store.ReadAsync(query, [ReadOption.Descending]); // Newest first
    }

    /// <summary>
    /// Real-world scenario: Find all orders in a specific state
    /// Combines multiple event types with status tag
    /// </summary>
    [Benchmark(Description = "Real-world: Orders in specific state")]
    public async Task Query_RealWorld_OrdersInState()
    {
        var store = CreateStore();

        var query = Query.FromItems([
            // Active orders (created or shipped)
            new QueryItem
            {
                EventTypes = ["OrderCreated", "OrderShipped"],
                Tags = [new Tag("Status", "Active")]
            },
            // OR High priority delivered orders
            new QueryItem
            {
                EventTypes = ["OrderDelivered"],
                Tags = [new Tag("Priority", "High")]
            }
        ]);

        var results = await store.ReadAsync(query, null);
    }
}
