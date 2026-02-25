using Opossum.BenchmarkTests.Helpers;

namespace Opossum.BenchmarkTests.Core;

/// <summary>
/// Isolated benchmark to verify descending order fix
/// Measures ONLY the overhead of ReadOption.Descending
/// by comparing ascending vs descending on the same pre-loaded store
/// </summary>
[Config(typeof(OpossumBenchmarkConfig))]
[MemoryDiagnoser]
public class DescendingOrderIsolatedBenchmark
{
    private IServiceProvider _serviceProvider = null!;
    private IEventStore _store = null!;
    private TempFileSystemHelper _tempHelper = null!;
    private Query _testQuery = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _tempHelper = new TempFileSystemHelper("DescendingIsolated");

        // Create and populate store ONCE (not per iteration)
        var storePath = _tempHelper.CreateSubDirectory("TestStore");
        var options = new OpossumOptions { RootPath = storePath, FlushEventsImmediately = false };
        options.UseStore("BenchmarkContext");

        var services = new ServiceCollection();
        services.AddOpossum(opt =>
        {
            opt.RootPath = storePath;
            opt.FlushEventsImmediately = false;
            opt.UseStore("BenchmarkContext");
        });

        _serviceProvider = services.BuildServiceProvider();
        _store = _serviceProvider.GetRequiredService<IEventStore>();

        // Populate with 500 events (same as QueryBenchmarks)
        var events = BenchmarkDataGenerator.GenerateEvents(
            500,
            tagCount: 2,
            eventTypes: ["OrderCreated", "OrderShipped"]);

        // Batch append for efficiency
        const int batchSize = 50;
        for (int i = 0; i < events.Count; i += batchSize)
        {
            var batch = events.Skip(i).Take(batchSize).ToArray();
            _store.AppendAsync(batch, null).GetAwaiter().GetResult();
        }

        // Create query (same as QueryBenchmarks)
        _testQuery = Query.FromEventTypes(["OrderCreated", "OrderShipped"]);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        (_serviceProvider as IDisposable)?.Dispose();
        _tempHelper?.Dispose();
    }

    // ========================================================================
    // Isolated Descending vs Ascending Comparison
    // ========================================================================

    /// <summary>
    /// Baseline: Query with ascending order (default)
    /// </summary>
    [Benchmark(Baseline = true, Description = "Ascending order (baseline)")]
    public async Task Query_AscendingOrderAsync()
    {
        var results = await _store.ReadAsync(_testQuery, null);
    }

    /// <summary>
    /// Test: Query with descending order
    /// Should be close to baseline if fix worked!
    /// </summary>
    [Benchmark(Description = "Descending order")]
    public async Task Query_DescendingOrderAsync()
    {
        var results = await _store.ReadAsync(_testQuery, [ReadOption.Descending]);
    }
}
