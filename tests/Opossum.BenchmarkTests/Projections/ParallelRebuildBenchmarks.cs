using BenchmarkDotNet.Attributes;
using Opossum.Core;
using Opossum.DependencyInjection;
using Opossum.Projections;

namespace Opossum.BenchmarkTests.Projections;

/// <summary>
/// Benchmarks for parallel projection rebuilding performance.
/// Measures the speedup achieved by parallel vs sequential rebuilds.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 3)]
public class ParallelRebuildBenchmarks : IDisposable
{
    private ServiceProvider? _serviceProvider;
    private string? _testStoragePath;
    private IEventStore? _eventStore;
    private IProjectionManager? _projectionManager;

    [GlobalSetup]
    public void Setup()
    {
        _testStoragePath = Path.Combine(
            Path.GetTempPath(),
            "OpossumBenchmark",
            Guid.NewGuid().ToString());

        var services = new ServiceCollection();
        services.AddLogging();

        services.AddOpossum(options =>
        {
            options.RootPath = _testStoragePath;
            options.AddContext("BenchmarkContext");
        });

        services.AddProjections(options =>
        {
            options.ScanAssembly(typeof(ParallelRebuildBenchmarks).Assembly);
            options.MaxConcurrentRebuilds = 4;
        });

        _serviceProvider = services.BuildServiceProvider();
        _eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        _projectionManager = _serviceProvider.GetRequiredService<IProjectionManager>();

        // Register projections
        _projectionManager.RegisterProjection(new BenchProjection1());
        _projectionManager.RegisterProjection(new BenchProjection2());
        _projectionManager.RegisterProjection(new BenchProjection3());
        _projectionManager.RegisterProjection(new BenchProjection4());

        // Seed data - 1000 events per projection type
        for (int i = 0; i < 1000; i++)
        {
            _eventStore.AppendAsync([CreateEvent<BenchEvent1>()], null).Wait();
            _eventStore.AppendAsync([CreateEvent<BenchEvent2>()], null).Wait();
            _eventStore.AppendAsync([CreateEvent<BenchEvent3>()], null).Wait();
            _eventStore.AppendAsync([CreateEvent<BenchEvent4>()], null).Wait();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Dispose();
    }

    [Benchmark(Baseline = true)]
    public async Task SequentialRebuild_4Projections()
    {
        // Rebuild each projection one at a time
        await _projectionManager!.RebuildAsync("BenchProjection1");
        await _projectionManager.RebuildAsync("BenchProjection2");
        await _projectionManager.RebuildAsync("BenchProjection3");
        await _projectionManager.RebuildAsync("BenchProjection4");
    }

    [Benchmark]
    public async Task ParallelRebuild_4Projections_Concurrency4()
    {
        // Rebuild all at once with MaxConcurrentRebuilds = 4
        await _projectionManager!.RebuildAllAsync(forceRebuild: true);
    }

    [Benchmark]
    public async Task ParallelRebuild_4Projections_Concurrency2()
    {
        // Temporarily lower concurrency to test scaling
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddOpossum(options =>
        {
            options.RootPath = _testStoragePath!;
            options.AddContext("BenchmarkContext");
        });

        services.AddProjections(options =>
        {
            options.ScanAssembly(typeof(ParallelRebuildBenchmarks).Assembly);
            options.MaxConcurrentRebuilds = 2; // Lower concurrency
        });

        using var sp = services.BuildServiceProvider();
        var pm = sp.GetRequiredService<IProjectionManager>();

        // Register projections
        pm.RegisterProjection(new BenchProjection1());
        pm.RegisterProjection(new BenchProjection2());
        pm.RegisterProjection(new BenchProjection3());
        pm.RegisterProjection(new BenchProjection4());

        await pm.RebuildAllAsync(forceRebuild: true);
    }

    private NewEvent CreateEvent<T>() where T : IEvent, new()
    {
        return new NewEvent
        {
            Event = new DomainEvent
            {
                EventType = typeof(T).Name,
                Event = new T(),
                Tags = []
            },
            Metadata = new Metadata
            {
                Timestamp = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid()
            }
        };
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();

        if (_testStoragePath != null && Directory.Exists(_testStoragePath))
        {
            try
            {
                Directory.Delete(_testStoragePath, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    // Benchmark projection definitions
    private sealed class BenchProjection1 : IProjectionDefinition<BenchState>
    {
        public string ProjectionName => "BenchProjection1";
        public string[] EventTypes => [nameof(BenchEvent1)];
        public string KeySelector(SequencedEvent evt) => "bench1";

        public BenchState? Apply(BenchState? current, IEvent @event)
        {
            // Simulate some work
            var result = 0;
            for (int i = 0; i < 100; i++)
            {
                result += i;
            }
            return new BenchState { Value = result };
        }
    }

    private sealed class BenchProjection2 : IProjectionDefinition<BenchState>
    {
        public string ProjectionName => "BenchProjection2";
        public string[] EventTypes => [nameof(BenchEvent2)];
        public string KeySelector(SequencedEvent evt) => "bench2";

        public BenchState? Apply(BenchState? current, IEvent @event)
        {
            var result = 0;
            for (int i = 0; i < 100; i++)
            {
                result += i;
            }
            return new BenchState { Value = result };
        }
    }

    private sealed class BenchProjection3 : IProjectionDefinition<BenchState>
    {
        public string ProjectionName => "BenchProjection3";
        public string[] EventTypes => [nameof(BenchEvent3)];
        public string KeySelector(SequencedEvent evt) => "bench3";

        public BenchState? Apply(BenchState? current, IEvent @event)
        {
            var result = 0;
            for (int i = 0; i < 100; i++)
            {
                result += i;
            }
            return new BenchState { Value = result };
        }
    }

    private sealed class BenchProjection4 : IProjectionDefinition<BenchState>
    {
        public string ProjectionName => "BenchProjection4";
        public string[] EventTypes => [nameof(BenchEvent4)];
        public string KeySelector(SequencedEvent evt) => "bench4";

        public BenchState? Apply(BenchState? current, IEvent @event)
        {
            var result = 0;
            for (int i = 0; i < 100; i++)
            {
                result += i;
            }
            return new BenchState { Value = result };
        }
    }

    private sealed class BenchState
    {
        public required int Value { get; init; }
    }

    private sealed class BenchEvent1 : IEvent { }
    private sealed class BenchEvent2 : IEvent { }
    private sealed class BenchEvent3 : IEvent { }
    private sealed class BenchEvent4 : IEvent { }
}
