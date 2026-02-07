using Opossum.BenchmarkTests.Helpers;

namespace Opossum.BenchmarkTests.Core;

/// <summary>
/// Benchmarks for event appending operations
/// Tests the core write path performance with various configurations
/// </summary>
[Config(typeof(OpossumBenchmarkConfig))]
[MemoryDiagnoser]
public class AppendBenchmarks
{
    private IServiceProvider _serviceProvider = null!;
    private IEventStore _store = null!;
    private TempFileSystemHelper _tempHelper = null!;
    private SequencedEvent _singleEvent = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _tempHelper = new TempFileSystemHelper("AppendBench");
        _singleEvent = BenchmarkDataGenerator.GenerateEvents(1, tagCount: 2)[0];
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _tempHelper?.Dispose();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // Create fresh event store for each iteration
        var iterationPath = _tempHelper.CreateSubDirectory($"Iteration_{Guid.NewGuid():N}");

        var options = new OpossumOptions
        {
            RootPath = iterationPath,
            FlushEventsImmediately = false // Will be overridden by specific benchmarks
        };
        options.AddContext("BenchmarkContext");

        // Use DI to create event store (same as production usage)
        var services = new ServiceCollection();
        services.AddOpossum(opt =>
        {
            opt.RootPath = options.RootPath;
            opt.FlushEventsImmediately = options.FlushEventsImmediately;
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

    /// <summary>
    /// Baseline: Single event append without flush (fastest)
    /// </summary>
    [Benchmark(Baseline = true, Description = "Single event append (no flush)")]
    public async Task SingleEventAppend_NoFlush()
    {
        var events = new[] { _singleEvent };
        await _store.AppendAsync(events, null);
    }

    /// <summary>
    /// Single event append with flush (production mode)
    /// </summary>
    [Benchmark(Description = "Single event append (with flush)")]
    public async Task SingleEventAppend_WithFlush()
    {
        // Create new store with flush enabled
        var options = new OpossumOptions
        {
            RootPath = _tempHelper.CreateSubDirectory($"Flush_{Guid.NewGuid():N}"),
            FlushEventsImmediately = true
        };
        options.AddContext("BenchmarkContext");

        var services = new ServiceCollection();
        services.AddOpossum(opt =>
        {
            opt.RootPath = options.RootPath;
            opt.FlushEventsImmediately = true;
            opt.AddContext("BenchmarkContext");
        });

        using var sp = services.BuildServiceProvider();
        var store = sp.GetRequiredService<IEventStore>();

        var events = new[] { _singleEvent };
        await store.AppendAsync(events, null);
    }

    /// <summary>
    /// Batch append - 10 events without flush
    /// </summary>
    [Benchmark(Description = "Batch append (10 events, no flush)")]
    public async Task BatchAppend_10Events_NoFlush()
    {
        var events = BenchmarkDataGenerator.GenerateEvents(10, tagCount: 2);
        await _store.AppendAsync(events.ToArray(), null);
    }

    /// <summary>
    /// Batch append - 10 events with flush
    /// </summary>
    [Benchmark(Description = "Batch append (10 events, with flush)")]
    public async Task BatchAppend_10Events_WithFlush()
    {
        // Create new store with flush enabled
        var options = new OpossumOptions
        {
            RootPath = _tempHelper.CreateSubDirectory($"FlushBatch_{Guid.NewGuid():N}"),
            FlushEventsImmediately = true
        };
        options.AddContext("BenchmarkContext");

        var services = new ServiceCollection();
        services.AddOpossum(opt =>
        {
            opt.RootPath = options.RootPath;
            opt.FlushEventsImmediately = true;
            opt.AddContext("BenchmarkContext");
        });

        using var sp = services.BuildServiceProvider();
        var store = sp.GetRequiredService<IEventStore>();

        var events = BenchmarkDataGenerator.GenerateEvents(10, tagCount: 2);
        await store.AppendAsync(events.ToArray(), null);
    }

    // ========================================================================
    // Phase 2: Additional Batch Sizes (Finding Optimal Batch Size)
    // ========================================================================

    /// <summary>
    /// Batch append - 2 events (minimal batch)
    /// </summary>
    [Benchmark(Description = "Batch append (2 events, no flush)")]
    public async Task BatchAppend_2Events_NoFlush()
    {
        var events = BenchmarkDataGenerator.GenerateEvents(2, tagCount: 2);
        await _store.AppendAsync(events.ToArray(), null);
    }

    /// <summary>
    /// Batch append - 5 events
    /// </summary>
    [Benchmark(Description = "Batch append (5 events, no flush)")]
    public async Task BatchAppend_5Events_NoFlush()
    {
        var events = BenchmarkDataGenerator.GenerateEvents(5, tagCount: 2);
        await _store.AppendAsync(events.ToArray(), null);
    }

    /// <summary>
    /// Batch append - 20 events
    /// </summary>
    [Benchmark(Description = "Batch append (20 events, no flush)")]
    public async Task BatchAppend_20Events_NoFlush()
    {
        var events = BenchmarkDataGenerator.GenerateEvents(20, tagCount: 2);
        await _store.AppendAsync(events.ToArray(), null);
    }

    /// <summary>
    /// Batch append - 50 events (larger batch)
    /// </summary>
    [Benchmark(Description = "Batch append (50 events, no flush)")]
    public async Task BatchAppend_50Events_NoFlush()
    {
        var events = BenchmarkDataGenerator.GenerateEvents(50, tagCount: 2);
        await _store.AppendAsync(events.ToArray(), null);
    }

    /// <summary>
    /// Batch append - 100 events (large batch)
    /// </summary>
    [Benchmark(Description = "Batch append (100 events, no flush)")]
    public async Task BatchAppend_100Events_NoFlush()
    {
        var events = BenchmarkDataGenerator.GenerateEvents(100, tagCount: 2);
        await _store.AppendAsync(events.ToArray(), null);
    }

    // ========================================================================
    // Phase 2: DCB Validation Scenarios
    // ========================================================================

    /// <summary>
    /// Append with DCB validation - FailIfEventsMatch (uniqueness constraint)
    /// Simulates email uniqueness check like in RegisterStudent
    /// </summary>
    [Benchmark(Description = "Append with DCB (FailIfEventsMatch)")]
    public async Task AppendWithDCB_FailIfEventsMatch()
    {
        var email = $"test-{Guid.NewGuid()}@example.com";
        var studentId = Guid.NewGuid();

        var evt = BenchmarkDataGenerator.GenerateEvents(1, tagCount: 0)[0];
        evt.Event.Tags.Add(new Tag { Key = "studentEmail", Value = email });

        // DCB condition: Fail if any event with this email already exists
        var validationQuery = Query.FromTags([new Tag { Key = "studentEmail", Value = email }]);
        var condition = new AppendCondition { FailIfEventsMatch = validationQuery };

        await _store.AppendAsync([evt], condition);
    }
}
