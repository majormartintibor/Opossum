using System.Diagnostics;
using Opossum.Core;
using Opossum.DependencyInjection;
using Opossum.Projections;

namespace Opossum.IntegrationTests.Projections;

/// <summary>
/// Integration tests for parallel rebuilding with per-projection locks.
/// Tests true parallelism, fail-fast behavior, and thread safety.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public class ParallelRebuildLockingTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string _testStoragePath;
    private readonly IEventStore _eventStore;
    private readonly IProjectionManager _projectionManager;

    public ParallelRebuildLockingTests()
    {
        _testStoragePath = Path.Combine(
            Path.GetTempPath(),
            "OpossumParallelLockingTests",
            Guid.NewGuid().ToString());

        var services = new ServiceCollection();
        services.AddLogging();

        services.AddOpossum(options =>
        {
            options.RootPath = _testStoragePath;
            options.UseStore("LockingTestContext");
        });

        services.AddProjections(options =>
        {
            options.ScanAssembly(typeof(ParallelRebuildLockingTests).Assembly);
            options.MaxConcurrentRebuilds = 4;
        });

        _serviceProvider = services.BuildServiceProvider();
        _eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        _projectionManager = _serviceProvider.GetRequiredService<IProjectionManager>();
    }

    [Fact]
    public async Task ConcurrentRebuilds_OfDifferentProjections_ExecuteInParallel()
    {
        // Arrange - Register 4 projections
        _projectionManager.RegisterProjection(new SlowProjection1());
        _projectionManager.RegisterProjection(new SlowProjection2());
        _projectionManager.RegisterProjection(new SlowProjection3());
        _projectionManager.RegisterProjection(new SlowProjection4());

        // Add events to make rebuilds take some time
        for (int i = 0; i < 10; i++)
        {
            await _eventStore.AppendAsync([CreateEvent<SlowEvent1>()], null);
            await _eventStore.AppendAsync([CreateEvent<SlowEvent2>()], null);
            await _eventStore.AppendAsync([CreateEvent<SlowEvent3>()], null);
            await _eventStore.AppendAsync([CreateEvent<SlowEvent4>()], null);
        }

        // Act - Rebuild all in parallel
        var stopwatch = Stopwatch.StartNew();
        var result = await _projectionManager.RebuildAllAsync(forceRebuild: false);
        stopwatch.Stop();

        // Assert
        Assert.Equal(4, result.TotalRebuilt);
        Assert.True(result.Success);

        // Calculate sum of individual durations
        var totalSequentialTime = result.Details.Sum(d => d.Duration.TotalMilliseconds);

        // Parallel execution should be significantly faster than sequential
        // With 4 cores and 4 projections, we expect close to 4x speedup
        // Allow for overhead, so we check it's at most 60% of sequential time
        Assert.True(stopwatch.ElapsedMilliseconds < totalSequentialTime * 0.6,
            $"Parallel execution ({stopwatch.ElapsedMilliseconds}ms) should be faster than " +
            $"60% of sequential time ({totalSequentialTime}ms)");
    }

    [Fact]
    public async Task DuplicateRebuild_SameProjection_ThrowsInvalidOperationException()
    {
        // Arrange
        _projectionManager.RegisterProjection(new LongRunningProjection());

        // Add events to make rebuild take longer
        for (int i = 0; i < 100; i++)
        {
            await _eventStore.AppendAsync([CreateEvent<LongRunningEvent>()], null);
        }

        // Act - Start first rebuild in background
        var firstRebuildTask = Task.Run(async () =>
        {
            await _projectionManager.RebuildAsync("LongRunningProjection");
        });

        // Wait a bit to ensure first rebuild has acquired the lock
        await Task.Delay(50);

        // Try to start second rebuild while first is still running
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _projectionManager.RebuildAsync("LongRunningProjection");
        });

        // Assert
        Assert.Contains("already being rebuilt or updated", exception.Message);
        Assert.Contains("LongRunningProjection", exception.Message);

        // Wait for first rebuild to complete
        await firstRebuildTask;
    }

    [Fact]
    public async Task Update_DuringRebuild_IsSkippedGracefully()
    {
        // Arrange
        _projectionManager.RegisterProjection(new LongRunningProjection());

        // Add initial events
        for (int i = 0; i < 50; i++)
        {
            await _eventStore.AppendAsync([CreateEvent<LongRunningEvent>()], null);
        }

        // Act - Start rebuild in background
        var rebuildTask = Task.Run(async () =>
        {
            await _projectionManager.RebuildAsync("LongRunningProjection");
        });

        // Wait a bit to ensure rebuild has started
        await Task.Delay(50);

        // Try to update while rebuild is running
        var newEvents = new[] { CreateSequencedEvent<LongRunningEvent>(51) };

        // This should not throw - it should skip gracefully
        await _projectionManager.UpdateAsync(newEvents, CancellationToken.None);

        // Wait for rebuild to complete
        await rebuildTask;

        // Assert - Rebuild should have completed successfully
        var checkpoint = await _projectionManager.GetCheckpointAsync("LongRunningProjection");
        Assert.True(checkpoint > 0);
    }

    [Fact]
    public async Task RebuildAfterRebuild_SameProjection_Succeeds()
    {
        // Arrange
        _projectionManager.RegisterProjection(new FastProjection());
        await _eventStore.AppendAsync([CreateEvent<FastEvent>()], null);

        // Act - First rebuild
        await _projectionManager.RebuildAsync("FastProjection");
        var checkpointAfterFirst = await _projectionManager.GetCheckpointAsync("FastProjection");

        // Second rebuild (should succeed now that first is complete)
        await _projectionManager.RebuildAsync("FastProjection");
        var checkpointAfterSecond = await _projectionManager.GetCheckpointAsync("FastProjection");

        // Assert
        Assert.Equal(checkpointAfterFirst, checkpointAfterSecond);
    }

    [Fact]
    public async Task ParallelRebuildAll_WithDifferentProjections_AllComplete()
    {
        // Arrange - Register multiple projections
        _projectionManager.RegisterProjection(new FastProjection());
        _projectionManager.RegisterProjection(new SlowProjection1());
        _projectionManager.RegisterProjection(new SlowProjection2());

        // Add events
        await _eventStore.AppendAsync([CreateEvent<FastEvent>()], null);
        await _eventStore.AppendAsync([CreateEvent<SlowEvent1>()], null);
        await _eventStore.AppendAsync([CreateEvent<SlowEvent2>()], null);

        // Act
        var result = await _projectionManager.RebuildAllAsync(forceRebuild: false);

        // Assert
        Assert.Equal(3, result.TotalRebuilt);
        Assert.True(result.Success);
        Assert.All(result.Details, detail => Assert.True(detail.Success));
    }

    // Helper method to create test events for appending
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

    // Helper method to create sequenced events for UpdateAsync (already-stored events)
    private static SequencedEvent CreateSequencedEvent<T>(long position) where T : IEvent, new()
    {
        return new SequencedEvent
        {
            Position = position,
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
        _serviceProvider.Dispose();

        if (Directory.Exists(_testStoragePath))
        {
            Directory.Delete(_testStoragePath, recursive: true);
        }
    }

    // Test projection definitions with artificial delays
    private sealed class LongRunningProjection : IProjectionDefinition<TestState>
    {
        public string ProjectionName => "LongRunningProjection";
        public string[] EventTypes => [nameof(LongRunningEvent)];
        public string KeySelector(SequencedEvent evt) => "key";

        public TestState? Apply(TestState? current, SequencedEvent @event)
        {
            // Simulate slow processing
            Thread.Sleep(10);
            return new TestState { Value = "processed" };
        }
    }

    private sealed class SlowProjection1 : IProjectionDefinition<TestState>
    {
        public string ProjectionName => "SlowProjection1";
        public string[] EventTypes => [nameof(SlowEvent1)];
        public string KeySelector(SequencedEvent evt) => "key1";

        public TestState? Apply(TestState? current, SequencedEvent @event)
        {
            Thread.Sleep(5);
            return new TestState { Value = "slow1" };
        }
    }

    private sealed class SlowProjection2 : IProjectionDefinition<TestState>
    {
        public string ProjectionName => "SlowProjection2";
        public string[] EventTypes => [nameof(SlowEvent2)];
        public string KeySelector(SequencedEvent evt) => "key2";

        public TestState? Apply(TestState? current, SequencedEvent @event)
        {
            Thread.Sleep(5);
            return new TestState { Value = "slow2" };
        }
    }

    private sealed class SlowProjection3 : IProjectionDefinition<TestState>
    {
        public string ProjectionName => "SlowProjection3";
        public string[] EventTypes => [nameof(SlowEvent3)];
        public string KeySelector(SequencedEvent evt) => "key3";

        public TestState? Apply(TestState? current, SequencedEvent @event)
        {
            Thread.Sleep(5);
            return new TestState { Value = "slow3" };
        }
    }

    private sealed class SlowProjection4 : IProjectionDefinition<TestState>
    {
        public string ProjectionName => "SlowProjection4";
        public string[] EventTypes => [nameof(SlowEvent4)];
        public string KeySelector(SequencedEvent evt) => "key4";

        public TestState? Apply(TestState? current, SequencedEvent @event)
        {
            Thread.Sleep(5);
            return new TestState { Value = "slow4" };
        }
    }

    private sealed class FastProjection : IProjectionDefinition<TestState>
    {
        public string ProjectionName => "FastProjection";
        public string[] EventTypes => [nameof(FastEvent)];
        public string KeySelector(SequencedEvent evt) => "fast";

        public TestState? Apply(TestState? current, SequencedEvent @event)
        {
            return new TestState { Value = "fast" };
        }
    }

    // Test state and events
    private sealed class TestState
    {
        public required string Value { get; init; }
    }

    private sealed class LongRunningEvent : IEvent { }
    private sealed class SlowEvent1 : IEvent { }
    private sealed class SlowEvent2 : IEvent { }
    private sealed class SlowEvent3 : IEvent { }
    private sealed class SlowEvent4 : IEvent { }
    private sealed class FastEvent : IEvent { }
}
