using Opossum.BenchmarkTests.Helpers;

namespace Opossum.BenchmarkTests.Core;

/// <summary>
/// Benchmarks for ReadLastAsync — finds the single highest-position event matching a query.
/// The key invariant under test: regardless of store size, only ONE event file is read from
/// disk. Index-lookup cost grows with store size; the file-read cost stays constant.
/// </summary>
[Config(typeof(OpossumBenchmarkConfig))]
[MemoryDiagnoser]
public class ReadLastBenchmarks
{
    private TempFileSystemHelper _tempHelper = null!;

    private string _store100Path = null!;
    private string _store1KPath = null!;
    private string _store10KPath = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _tempHelper = new TempFileSystemHelper("ReadLastBench");

        _store100Path = CreateAndPopulateStore(100);
        _store1KPath = CreateAndPopulateStore(1000);
        _store10KPath = CreateAndPopulateStore(10000);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _tempHelper?.Dispose();
    }

    // ========================================================================
    // Helper
    // ========================================================================

    private string CreateAndPopulateStore(int eventCount)
    {
        var path = _tempHelper.CreateSubDirectory($"Store_{eventCount}Events");

        var services = new ServiceCollection();
        services.AddOpossum(opt =>
        {
            opt.RootPath = path;
            opt.FlushEventsImmediately = false;
            opt.UseStore("BenchmarkContext");
        });

        using var sp = services.BuildServiceProvider();
        var store = sp.GetRequiredService<IEventStore>();

        var events = BenchmarkDataGenerator.GenerateEvents(
            eventCount,
            tagCount: 3,
            eventTypes: ["OrderCreated", "OrderShipped", "OrderDelivered", "OrderCancelled"]);

        const int batchSize = 100;
        for (int i = 0; i < events.Count; i += batchSize)
        {
            var batch = events.Skip(i).Take(batchSize).ToArray();
            store.AppendAsync(batch, null).GetAwaiter().GetResult();
        }

        return path;
    }

    private IEventStore CreateStore(string path)
    {
        var services = new ServiceCollection();
        services.AddOpossum(opt =>
        {
            opt.RootPath = path;
            opt.FlushEventsImmediately = false;
            opt.UseStore("BenchmarkContext");
        });

        return services.BuildServiceProvider().GetRequiredService<IEventStore>();
    }

    // ========================================================================
    // ReadLast by Event Type — Scale Testing
    // Proves: index lookup + single file read; file-read cost is O(1) regardless of scale.
    // ========================================================================

    /// <summary>
    /// ReadLastAsync by event type from 100 events (baseline)
    /// </summary>
    [Benchmark(Baseline = true, Description = "ReadLast by event type (100 events)")]
    public async Task ReadLastByEventType_100EventsAsync()
    {
        var store = CreateStore(_store100Path);
        var query = Query.FromEventTypes(["OrderCreated"]);
        _ = await store.ReadLastAsync(query);
    }

    /// <summary>
    /// ReadLastAsync by event type from 1K events.
    /// Time should grow only from the larger index file read; the event file read is constant.
    /// </summary>
    [Benchmark(Description = "ReadLast by event type (1K events)")]
    public async Task ReadLastByEventType_1KEventsAsync()
    {
        var store = CreateStore(_store1KPath);
        var query = Query.FromEventTypes(["OrderCreated"]);
        _ = await store.ReadLastAsync(query);
    }

    /// <summary>
    /// ReadLastAsync by event type from 10K events.
    /// </summary>
    [Benchmark(Description = "ReadLast by event type (10K events)")]
    public async Task ReadLastByEventType_10KEventsAsync()
    {
        var store = CreateStore(_store10KPath);
        var query = Query.FromEventTypes(["OrderCreated"]);
        _ = await store.ReadLastAsync(query);
    }

    // ========================================================================
    // ReadLast by Tag — Index Lookup Path
    // ========================================================================

    /// <summary>
    /// ReadLastAsync by single tag from 1K events.
    /// </summary>
    [Benchmark(Description = "ReadLast by tag (1K events)")]
    public async Task ReadLastByTag_1KEventsAsync()
    {
        var store = CreateStore(_store1KPath);
        var query = Query.FromTags([new Tag("Region", "US-West")]);
        _ = await store.ReadLastAsync(query);
    }

    // ========================================================================
    // ReadLast with Query.All() — Pathological / NOT a production use case
    //
    // DO NOT OPTIMIZE based on these numbers.
    //
    // ReadLastAsync(Query.All()) has no meaningful real-world application:
    //   - ReadLastAsync exists specifically for typed DCB patterns
    //     (e.g. "last InvoiceCreated"). Calling it with Query.All() —
    //     "give me the globally last event of any type" — has no DCB use
    //     case and bypasses the entire point of the API.
    //   - GetAllPositionsAsync allocates long[N] for the whole store just
    //     to use [^1]. This is a known, accepted inefficiency for a path
    //     that should never be called in production.
    //
    // These benchmarks exist solely to document that the allocation DOES
    // scale linearly, so a future reader seeing this in a profile knows it
    // is expected and intentional — not a regression to chase.
    // ========================================================================

    /// <summary>
    /// ReadLastAsync with Query.All() from 1K events.
    /// NOT a production use case — see section comment above. Do not optimize.
    /// GetAllPositionsAsync allocates long[1000] just to use the last element.
    /// </summary>
    [Benchmark(Description = "ReadLast Query.All() (1K events) [NOT production]")]
    public async Task ReadLastAll_1KEventsAsync()
    {
        var store = CreateStore(_store1KPath);
        var query = Query.All();
        _ = await store.ReadLastAsync(query);
    }

    /// <summary>
    /// ReadLastAsync with Query.All() from 10K events.
    /// NOT a production use case — see section comment above. Do not optimize.
    /// Allocation is 10x larger than the 1K case, as expected and accepted.
    /// </summary>
    [Benchmark(Description = "ReadLast Query.All() (10K events) [NOT production]")]
    public async Task ReadLastAll_10KEventsAsync()
    {
        var store = CreateStore(_store10KPath);
        var query = Query.All();
        _ = await store.ReadLastAsync(query);
    }
}
