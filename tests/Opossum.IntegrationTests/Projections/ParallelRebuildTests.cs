using Opossum.Core;
using Opossum.DependencyInjection;
using Opossum.IntegrationTests.Helpers;
using Opossum.Projections;

namespace Opossum.IntegrationTests.Projections;

/// <summary>
/// Integration tests for parallel projection rebuilding functionality.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public class ParallelRebuildTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string _testStoragePath;
    private readonly IEventStore _eventStore;
    private readonly IProjectionManager _projectionManager;

    public ParallelRebuildTests()
    {
        _testStoragePath = Path.Combine(
            Path.GetTempPath(),
            "OpossumParallelRebuildTests",
            Guid.NewGuid().ToString());

        var services = new ServiceCollection();

        services.AddLogging();

        services.AddOpossum(options =>
        {
            options.RootPath = _testStoragePath;
            options.UseStore("ParallelRebuildContext");
        });

        services.AddProjections(options =>
        {
            options.ScanAssembly(typeof(ParallelRebuildTests).Assembly);
            options.MaxConcurrentRebuilds = 4;
        });

        _serviceProvider = services.BuildServiceProvider();
        _eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        _projectionManager = _serviceProvider.GetRequiredService<IProjectionManager>();
    }

    [Fact]
    public async Task RebuildAllAsync_WithNoProjections_ReturnsEmptyResultAsync()
    {
        // Act
        var result = await _projectionManager.RebuildAllAsync(forceRebuild: false);

        // Assert
        Assert.Equal(0, result.TotalRebuilt);
        Assert.Equal(TimeSpan.Zero, result.Duration);
        Assert.Empty(result.Details);
        Assert.True(result.Success);
        Assert.Empty(result.FailedProjections);
    }

    [Fact]
    public async Task RebuildAllAsync_WithForceRebuildFalse_OnlyRebuildsProjectionsWithNoCheckpointAsync()
    {
        // Arrange - Register two projections and manually set checkpoint for one
        var projection1 = new TestProjection1();
        var projection2 = new TestProjection2();
        _projectionManager.RegisterProjection(projection1);
        _projectionManager.RegisterProjection(projection2);

        // Set checkpoint for projection1 (simulate it's already built)
        await _projectionManager.SaveCheckpointAsync("TestProjection1", 100);

        // Act
        var result = await _projectionManager.RebuildAllAsync(forceRebuild: false);

        // Assert
        Assert.Equal(1, result.TotalRebuilt); // Only TestProjection2 should rebuild
        Assert.True(result.Success);
        Assert.Single(result.Details);
        Assert.Equal("TestProjection2", result.Details[0].ProjectionName);
    }

    [Fact]
    public async Task RebuildAllAsync_WithForceRebuildTrue_RebuildsAllProjectionsAsync()
    {
        // Arrange - Register two projections and set checkpoints for both
        var projection1 = new TestProjection1();
        var projection2 = new TestProjection2();
        _projectionManager.RegisterProjection(projection1);
        _projectionManager.RegisterProjection(projection2);

        await _projectionManager.SaveCheckpointAsync("TestProjection1", 100);
        await _projectionManager.SaveCheckpointAsync("TestProjection2", 200);

        // Act
        var result = await _projectionManager.RebuildAllAsync(forceRebuild: true);

        // Assert
        Assert.Equal(2, result.TotalRebuilt);
        Assert.True(result.Success);
        Assert.Equal(2, result.Details.Count);
    }

    [Fact]
    public async Task RebuildAsync_WithSpecificProjections_RebuildsOnlyThoseAsync()
    {
        // Arrange - Register three projections
        var projection1 = new TestProjection1();
        var projection2 = new TestProjection2();
        var projection3 = new TestProjection3();
        _projectionManager.RegisterProjection(projection1);
        _projectionManager.RegisterProjection(projection2);
        _projectionManager.RegisterProjection(projection3);

        // Act - Rebuild only projection1 and projection3
        var result = await _projectionManager.RebuildAsync(
            ["TestProjection1", "TestProjection3"]);

        // Assert
        Assert.Equal(2, result.TotalRebuilt);
        Assert.True(result.Success);
        Assert.Equal(2, result.Details.Count);
        Assert.Contains(result.Details, d => d.ProjectionName == "TestProjection1");
        Assert.Contains(result.Details, d => d.ProjectionName == "TestProjection3");
        Assert.DoesNotContain(result.Details, d => d.ProjectionName == "TestProjection2");
    }

    [Fact]
    public async Task RebuildAsync_WithEmptyArray_ReturnsEmptyResultAsync()
    {
        // Act
        var result = await _projectionManager.RebuildAsync([]);

        // Assert
        Assert.Equal(0, result.TotalRebuilt);
        Assert.Empty(result.Details);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task RebuildAsync_WithNullArray_ThrowsArgumentNullExceptionAsync()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _projectionManager.RebuildAsync((string[])null!));
    }

    [Fact]
    public async Task GetRebuildStatusAsync_WhenNotRebuilding_ReturnsFalseAsync()
    {
        // Act
        var status = await _projectionManager.GetRebuildStatusAsync();

        // Assert
        Assert.False(status.IsRebuilding);
        Assert.Empty(status.InProgressProjections);
        Assert.Empty(status.QueuedProjections);
        Assert.Null(status.StartedAt);
    }

    [Fact]
    public async Task RebuildResult_ContainsAccurateDetailsAsync()
    {
        // Arrange
        var projection = new TestProjection1();
        _projectionManager.RegisterProjection(projection);

        // Add some events using the extension method
        var stream1Events = new List<NewEvent>
        {
            new() {
                Event = new DomainEvent
                {
                    EventType = nameof(TestEvent1),
                    Event = new TestEvent1 { Value = "test" },
                    Tags = []
                },
                Metadata = new Metadata
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    CorrelationId = Guid.NewGuid()
                }
            }
        };

        var stream2Events = new List<NewEvent>
        {
            new() {
                Event = new DomainEvent
                {
                    EventType = nameof(TestEvent1),
                    Event = new TestEvent1 { Value = "test2" },
                    Tags = []
                },
                Metadata = new Metadata
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    CorrelationId = Guid.NewGuid()
                }
            }
        };

        await _eventStore.AppendAsync([.. stream1Events], null);
        await _eventStore.AppendAsync([.. stream2Events], null);

        // Act
        var result = await _projectionManager.RebuildAsync(["TestProjection1"]);

        // Assert
        Assert.Single(result.Details);
        var detail = result.Details[0];
        Assert.Equal("TestProjection1", detail.ProjectionName);
        Assert.True(detail.Success);
        Assert.True(detail.Duration > TimeSpan.Zero);
        Assert.Equal(2, detail.EventsProcessed);
        Assert.Null(detail.ErrorMessage);
    }

    [Fact]
    public async Task RebuildAllAsync_MeasuresOverallDurationAsync()
    {
        // Arrange
        var projection1 = new TestProjection1();
        var projection2 = new TestProjection2();
        _projectionManager.RegisterProjection(projection1);
        _projectionManager.RegisterProjection(projection2);

        // Act
        var result = await _projectionManager.RebuildAllAsync(forceRebuild: false);

        // Assert
        Assert.True(result.Duration > TimeSpan.Zero);
        Assert.True(result.Duration.TotalMilliseconds > 0);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        TestDirectoryHelper.ForceDelete(_testStoragePath);
    }

    // Test projection definitions
    private sealed class TestProjection1 : IProjectionDefinition<TestState>
    {
        public string ProjectionName => "TestProjection1";
        public string[] EventTypes => [nameof(TestEvent1)];

        public string KeySelector(SequencedEvent evt) => "key1";

        public TestState? Apply(TestState? current, SequencedEvent @event)
        {
            if (@event.Event.Event is TestEvent1 e)
                return new TestState { Value = e.Value };
            return current;
        }
    }

    private sealed class TestProjection2 : IProjectionDefinition<TestState>
    {
        public string ProjectionName => "TestProjection2";
        public string[] EventTypes => [nameof(TestEvent2)];

        public string KeySelector(SequencedEvent evt) => "key2";

        public TestState? Apply(TestState? current, SequencedEvent @event)
        {
            if (@event.Event.Event is TestEvent2 e)
                return new TestState { Value = e.Value };
            return current;
        }
    }

    private sealed class TestProjection3 : IProjectionDefinition<TestState>
    {
        public string ProjectionName => "TestProjection3";
        public string[] EventTypes => [nameof(TestEvent3)];

        public string KeySelector(SequencedEvent evt) => "key3";

        public TestState? Apply(TestState? current, SequencedEvent @event)
        {
            if (@event.Event.Event is TestEvent3 e)
                return new TestState { Value = e.Value };
            return current;
        }
    }

    private sealed class TestState
    {
        public required string Value { get; init; }
    }

    private sealed class TestEvent1 : IEvent
    {
        public required string Value { get; init; }
    }

    private sealed class TestEvent2 : IEvent
    {
        public required string Value { get; init; }
    }

    private sealed class TestEvent3 : IEvent
    {
        public required string Value { get; init; }
    }
}
