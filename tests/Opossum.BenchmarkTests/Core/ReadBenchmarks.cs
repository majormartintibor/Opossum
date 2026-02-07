using Opossum.BenchmarkTests.Helpers;

namespace Opossum.BenchmarkTests.Core;

/// <summary>
/// Benchmarks for read/query operations
/// Tests query performance at different scales and with different query types
/// </summary>
[Config(typeof(OpossumBenchmarkConfig))]
[MemoryDiagnoser]
public class ReadBenchmarks
{
    private IServiceProvider _serviceProvider = null!;
    private IEventStore _store = null!;
    private TempFileSystemHelper _tempHelper = null!;

    // Pre-populated stores with different event counts
    private string _store100Path = null!;
    private string _store1KPath = null!;
    private string _store10KPath = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _tempHelper = new TempFileSystemHelper("ReadBench");
        
        // Create stores with different event counts (once per benchmark run)
        _store100Path = CreateAndPopulateStore(100);
        _store1KPath = CreateAndPopulateStore(1000);
        _store10KPath = CreateAndPopulateStore(10000);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        (_serviceProvider as IDisposable)?.Dispose();
        _tempHelper?.Dispose();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // Use existing populated store (don't recreate for each iteration)
        var options = new OpossumOptions
        {
            RootPath = _store100Path, // Will be overridden by specific benchmarks
            FlushEventsImmediately = false
        };
        options.AddContext("BenchmarkContext");
        
        var services = new ServiceCollection();
        services.AddOpossum(opt =>
        {
            opt.RootPath = options.RootPath;
            opt.FlushEventsImmediately = false;
            opt.AddContext("BenchmarkContext");
        });
        
        _serviceProvider = services.BuildServiceProvider();
        _store = _serviceProvider.GetRequiredService<IEventStore>();
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        (_serviceProvider as IDisposable)?.Dispose();
    }

    // ========================================================================
    // Helper: Create and Populate Store
    // ========================================================================

    private string CreateAndPopulateStore(int eventCount)
    {
        var path = _tempHelper.CreateSubDirectory($"Store_{eventCount}Events");
        var options = new OpossumOptions { RootPath = path, FlushEventsImmediately = false };
        options.AddContext("BenchmarkContext");
        
        var services = new ServiceCollection();
        services.AddOpossum(opt =>
        {
            opt.RootPath = path;
            opt.FlushEventsImmediately = false;
            opt.AddContext("BenchmarkContext");
        });
        
        using var sp = services.BuildServiceProvider();
        var store = sp.GetRequiredService<IEventStore>();
        
        // Populate with events (4 event types cycling)
        var events = BenchmarkDataGenerator.GenerateEvents(
            eventCount, 
            tagCount: 3,
            eventTypes: ["OrderCreated", "OrderShipped", "OrderDelivered", "OrderCancelled"]);
        
        // Batch append for efficiency
        const int batchSize = 100;
        for (int i = 0; i < events.Count; i += batchSize)
        {
            var batch = events.Skip(i).Take(batchSize).ToArray();
            store.AppendAsync(batch, null).GetAwaiter().GetResult();
        }
        
        return path;
    }

    // ========================================================================
    // Query by Event Type - Scale Testing
    // ========================================================================

    /// <summary>
    /// Query by single event type from 100 events (baseline)
    /// </summary>
    [Benchmark(Baseline = true, Description = "Query by event type (100 events)")]
    public async Task QueryByEventType_100Events()
    {
        // Recreate store with 100 events
        var services = new ServiceCollection();
        services.AddOpossum(opt =>
        {
            opt.RootPath = _store100Path;
            opt.FlushEventsImmediately = false;
            opt.AddContext("BenchmarkContext");
        });
        
        using var sp = services.BuildServiceProvider();
        var store = sp.GetRequiredService<IEventStore>();
        
        var query = Query.FromEventTypes(["OrderCreated"]);
        var results = await store.ReadAsync(query, null);
    }

    /// <summary>
    /// Query by single event type from 1K events
    /// </summary>
    [Benchmark(Description = "Query by event type (1K events)")]
    public async Task QueryByEventType_1KEvents()
    {
        var services = new ServiceCollection();
        services.AddOpossum(opt =>
        {
            opt.RootPath = _store1KPath;
            opt.FlushEventsImmediately = false;
            opt.AddContext("BenchmarkContext");
        });
        
        using var sp = services.BuildServiceProvider();
        var store = sp.GetRequiredService<IEventStore>();
        
        var query = Query.FromEventTypes(["OrderCreated"]);
        var results = await store.ReadAsync(query, null);
    }

    /// <summary>
    /// Query by single event type from 10K events
    /// </summary>
    [Benchmark(Description = "Query by event type (10K events)")]
    public async Task QueryByEventType_10KEvents()
    {
        var services = new ServiceCollection();
        services.AddOpossum(opt =>
        {
            opt.RootPath = _store10KPath;
            opt.FlushEventsImmediately = false;
            opt.AddContext("BenchmarkContext");
        });
        
        using var sp = services.BuildServiceProvider();
        var store = sp.GetRequiredService<IEventStore>();
        
        var query = Query.FromEventTypes(["OrderCreated"]);
        var results = await store.ReadAsync(query, null);
    }

    // ========================================================================
    // Query by Multiple Event Types (OR Logic)
    // ========================================================================

    /// <summary>
    /// Query by multiple event types (OR logic) from 1K events
    /// </summary>
    [Benchmark(Description = "Query by multiple event types (1K events)")]
    public async Task QueryByMultipleEventTypes_1KEvents()
    {
        var services = new ServiceCollection();
        services.AddOpossum(opt =>
        {
            opt.RootPath = _store1KPath;
            opt.FlushEventsImmediately = false;
            opt.AddContext("BenchmarkContext");
        });
        
        using var sp = services.BuildServiceProvider();
        var store = sp.GetRequiredService<IEventStore>();
        
        var query = Query.FromEventTypes(["OrderCreated", "OrderShipped", "OrderDelivered"]);
        var results = await store.ReadAsync(query, null);
    }

    // ========================================================================
    // Query by Tag - Scale Testing
    // ========================================================================

    /// <summary>
    /// Query by single tag from 100 events
    /// </summary>
    [Benchmark(Description = "Query by tag (100 events)")]
    public async Task QueryByTag_100Events()
    {
        var services = new ServiceCollection();
        services.AddOpossum(opt =>
        {
            opt.RootPath = _store100Path;
            opt.FlushEventsImmediately = false;
            opt.AddContext("BenchmarkContext");
        });
        
        using var sp = services.BuildServiceProvider();
        var store = sp.GetRequiredService<IEventStore>();
        
        var query = Query.FromTags([new Tag { Key = "Region", Value = "US-West" }]);
        var results = await store.ReadAsync(query, null);
    }

    /// <summary>
    /// Query by single tag from 1K events
    /// </summary>
    [Benchmark(Description = "Query by tag (1K events)")]
    public async Task QueryByTag_1KEvents()
    {
        var services = new ServiceCollection();
        services.AddOpossum(opt =>
        {
            opt.RootPath = _store1KPath;
            opt.FlushEventsImmediately = false;
            opt.AddContext("BenchmarkContext");
        });
        
        using var sp = services.BuildServiceProvider();
        var store = sp.GetRequiredService<IEventStore>();
        
        var query = Query.FromTags([new Tag { Key = "Region", Value = "US-West" }]);
        var results = await store.ReadAsync(query, null);
    }

    /// <summary>
    /// Query by single tag from 10K events
    /// </summary>
    [Benchmark(Description = "Query by tag (10K events)")]
    public async Task QueryByTag_10KEvents()
    {
        var services = new ServiceCollection();
        services.AddOpossum(opt =>
        {
            opt.RootPath = _store10KPath;
            opt.FlushEventsImmediately = false;
            opt.AddContext("BenchmarkContext");
        });
        
        using var sp = services.BuildServiceProvider();
        var store = sp.GetRequiredService<IEventStore>();
        
        var query = Query.FromTags([new Tag { Key = "Region", Value = "US-West" }]);
        var results = await store.ReadAsync(query, null);
    }

    // ========================================================================
    // Query by Multiple Tags (AND Logic)
    // ========================================================================

    /// <summary>
    /// Query by multiple tags (AND logic) from 1K events
    /// </summary>
    [Benchmark(Description = "Query by multiple tags (1K events)")]
    public async Task QueryByMultipleTags_1KEvents()
    {
        var services = new ServiceCollection();
        services.AddOpossum(opt =>
        {
            opt.RootPath = _store1KPath;
            opt.FlushEventsImmediately = false;
            opt.AddContext("BenchmarkContext");
        });
        
        using var sp = services.BuildServiceProvider();
        var store = sp.GetRequiredService<IEventStore>();
        
        var query = Query.FromTags([
            new Tag { Key = "Region", Value = "US-West" },
            new Tag { Key = "Environment", Value = "Production" }
        ]);
        var results = await store.ReadAsync(query, null);
    }

    // ========================================================================
    // Query.All() - Scale Testing
    // ========================================================================

    /// <summary>
    /// Query.All() from 100 events
    /// </summary>
    [Benchmark(Description = "Query.All() (100 events)")]
    public async Task QueryAll_100Events()
    {
        var services = new ServiceCollection();
        services.AddOpossum(opt =>
        {
            opt.RootPath = _store100Path;
            opt.FlushEventsImmediately = false;
            opt.AddContext("BenchmarkContext");
        });
        
        using var sp = services.BuildServiceProvider();
        var store = sp.GetRequiredService<IEventStore>();
        
        var query = Query.All();
        var results = await store.ReadAsync(query, null);
    }

    /// <summary>
    /// Query.All() from 1K events
    /// </summary>
    [Benchmark(Description = "Query.All() (1K events)")]
    public async Task QueryAll_1KEvents()
    {
        var services = new ServiceCollection();
        services.AddOpossum(opt =>
        {
            opt.RootPath = _store1KPath;
            opt.FlushEventsImmediately = false;
            opt.AddContext("BenchmarkContext");
        });
        
        using var sp = services.BuildServiceProvider();
        var store = sp.GetRequiredService<IEventStore>();
        
        var query = Query.All();
        var results = await store.ReadAsync(query, null);
    }

    /// <summary>
    /// Query.All() from 10K events
    /// </summary>
    [Benchmark(Description = "Query.All() (10K events)")]
    public async Task QueryAll_10KEvents()
    {
        var services = new ServiceCollection();
        services.AddOpossum(opt =>
        {
            opt.RootPath = _store10KPath;
            opt.FlushEventsImmediately = false;
            opt.AddContext("BenchmarkContext");
        });
        
        using var sp = services.BuildServiceProvider();
        var store = sp.GetRequiredService<IEventStore>();
        
        var query = Query.All();
        var results = await store.ReadAsync(query, null);
    }
}
