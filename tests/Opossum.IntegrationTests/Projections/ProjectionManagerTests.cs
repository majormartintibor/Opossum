using Opossum.Core;
using Opossum.DependencyInjection;
using Opossum.Extensions;
using Opossum.Projections;

namespace Opossum.IntegrationTests.Projections;

public class ProjectionManagerTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string _testStoragePath;
    private readonly IEventStore _eventStore;
    private readonly IProjectionManager _projectionManager;

    public ProjectionManagerTests()
    {
        _testStoragePath = Path.Combine(
            Path.GetTempPath(),
            "OpossumProjectionManagerTests",
            Guid.NewGuid().ToString());

        var services = new ServiceCollection();

        services.AddOpossum(options =>
        {
            options.RootPath = _testStoragePath;
            options.UseStore("ProjectionManagerContext");
        });

        services.AddProjections(options =>
        {
            options.EnableAutoRebuild = false;
        });

        services.AddProjectionStore<PmTestItemState>("PmTestItems");

        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        _serviceProvider = services.BuildServiceProvider();
        _eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        _projectionManager = _serviceProvider.GetRequiredService<IProjectionManager>();
    }

    [Fact]
    public void RegisterProjection_WithValidDefinition_RegistersSuccessfully()
    {
        // Arrange
        var projection = new PmTestItemProjection();

        // Act
        _projectionManager.RegisterProjection(projection);

        // Assert
        var registered = _projectionManager.GetRegisteredProjections();
        Assert.Contains("PmTestItems", registered);
    }

    [Fact]
    public void RegisterProjection_WithDuplicateName_ThrowsInvalidOperationException()
    {
        // Arrange
        _projectionManager.RegisterProjection(new PmTestItemProjection());

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _projectionManager.RegisterProjection(new PmTestItemProjection()));

        Assert.Contains("PmTestItems", ex.Message);
        Assert.Contains("already registered", ex.Message);
    }

    [Fact]
    public async Task GetCheckpointAsync_ForNewProjection_ReturnsZeroAsync()
    {
        // Arrange
        _projectionManager.RegisterProjection(new PmTestItemProjection());

        // Act
        var checkpoint = await _projectionManager.GetCheckpointAsync("PmTestItems");

        // Assert
        Assert.Equal(0, checkpoint);
    }

    [Fact]
    public async Task RebuildAsync_WithExistingEvents_BuildsProjectionAsync()
    {
        // Arrange
        _projectionManager.RegisterProjection(new PmTestItemProjection());

        var itemId = Guid.NewGuid();
        var events = new[]
        {
            new PmTestItemCreatedEvent(itemId, "Test Item")
                .ToDomainEvent()
                .WithTag("itemId", itemId.ToString())
                .Build()
        };
        await _eventStore.AppendAsync(events, null);

        // Act
        await _projectionManager.RebuildAsync("PmTestItems");

        // Assert
        var store = _serviceProvider.GetRequiredService<IProjectionStore<PmTestItemState>>();
        var item = await store.GetAsync(itemId.ToString());

        Assert.NotNull(item);
        Assert.Equal(itemId, item.ItemId);
        Assert.Equal("Test Item", item.Name);
    }

    [Fact]
    public async Task RebuildAsync_WithNonExistentProjection_ThrowsInvalidOperationExceptionAsync()
    {
        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _projectionManager.RebuildAsync("NonExistent"));

        Assert.Contains("NonExistent", ex.Message);
        Assert.Contains("not registered", ex.Message);
    }

    [Fact]
    public async Task RebuildAsync_WithDeleteEvent_RemovesProjectionAsync()
    {
        // Arrange
        _projectionManager.RegisterProjection(new PmTestItemProjection());

        var itemId = Guid.NewGuid();
        var events = new[]
        {
            new PmTestItemCreatedEvent(itemId, "Test Item")
                .ToDomainEvent()
                .WithTag("itemId", itemId.ToString())
                .Build(),
            new PmTestItemDeletedEvent(itemId)
                .ToDomainEvent()
                .WithTag("itemId", itemId.ToString())
                .Build()
        };
        await _eventStore.AppendAsync(events, null);

        // Act
        await _projectionManager.RebuildAsync("PmTestItems");

        // Assert - Apply returned null so projection should not exist
        var store = _serviceProvider.GetRequiredService<IProjectionStore<PmTestItemState>>();
        var item = await store.GetAsync(itemId.ToString());

        Assert.Null(item);
    }

    [Fact]
    public async Task SaveCheckpointAsync_SavesCheckpointAsync()
    {
        // Act
        await _projectionManager.SaveCheckpointAsync("PmTestItems", 42);

        // Assert
        var checkpoint = await _projectionManager.GetCheckpointAsync("PmTestItems");
        Assert.Equal(42, checkpoint);
    }

    [Fact]
    public async Task SaveCheckpointAsync_MultipleTimes_UpdatesCheckpointAsync()
    {
        // Act
        await _projectionManager.SaveCheckpointAsync("PmTestItems", 10);
        await _projectionManager.SaveCheckpointAsync("PmTestItems", 25);
        await _projectionManager.SaveCheckpointAsync("PmTestItems", 50);

        // Assert
        var checkpoint = await _projectionManager.GetCheckpointAsync("PmTestItems");
        Assert.Equal(50, checkpoint);
    }

    [Fact]
    public async Task UpdateAsync_WithNewEvents_UpdatesProjectionsAsync()
    {
        // Arrange
        _projectionManager.RegisterProjection(new PmTestItemProjection());

        var itemId = Guid.NewGuid();
        var createEvent = new PmTestItemCreatedEvent(itemId, "Original Name")
            .ToDomainEvent()
            .WithTag("itemId", itemId.ToString())
            .Build();

        await _eventStore.AppendAsync([createEvent], null);
        await _projectionManager.RebuildAsync("PmTestItems");

        var updateEvent = new PmTestItemUpdatedEvent(itemId, "Updated Name")
            .ToDomainEvent()
            .WithTag("itemId", itemId.ToString())
            .Build();

        await _eventStore.AppendAsync([updateEvent], null);

        var allEvents = await _eventStore.ReadAsync(Query.All(), null);
        var newEvents = allEvents.OrderBy(e => e.Position).Skip(1).ToArray();

        // Act
        await _projectionManager.UpdateAsync(newEvents);

        // Assert
        var store = _serviceProvider.GetRequiredService<IProjectionStore<PmTestItemState>>();
        var item = await store.GetAsync(itemId.ToString());

        Assert.NotNull(item);
        Assert.Equal("Updated Name", item.Name);
    }

    [Fact]
    public async Task RebuildAsync_OldFilesRemainOnDiskDuringRebuildAsync()
    {
        // Arrange: use a slow projection so we can observe file state during rebuild.
        // Old projection files should stay on disk until CommitRebuildAsync performs the
        // atomic directory swap at the very end of the rebuild.
        var startedSignal = new ManualResetEventSlim(initialState: false);

        // For the first rebuild, let Apply pass through immediately (resumeSignal is set).
        // We reset it before the second rebuild to pause Apply and observe the file state.
        var resumeSignal = new ManualResetEventSlim(initialState: true);

        var slowProjection = new SlowPmTestItemProjection(startedSignal, resumeSignal);
        _projectionManager.RegisterProjection(slowProjection);

        var itemId = Guid.NewGuid();
        await _eventStore.AppendAsync(
            [new PmTestItemCreatedEvent(itemId, "Version1")
                .ToDomainEvent()
                .WithTag("itemId", itemId.ToString())
                .Build()],
            null);

        // First rebuild: creates the initial projection files on disk (Apply runs unblocked).
        await _projectionManager.RebuildAsync("SlowPmTestItems");

        var projectionPath = Path.Combine(
            _testStoragePath, "ProjectionManagerContext", "Projections", "SlowPmTestItems");

        Assert.True(Directory.GetFiles(projectionPath, "*.json").Length > 0,
            "Initial projection files should exist after first rebuild");

        // Reset both signals: the second rebuild will pause inside Apply.
        startedSignal.Reset();
        resumeSignal.Reset();

        // Act: start a second rebuild in the background; it will pause inside Apply.
        var rebuildTask = Task.Run(async () =>
            await _projectionManager.RebuildAsync("SlowPmTestItems"));

        // Wait until the rebuild has started processing (Apply was entered).
        Assert.True(startedSignal.Wait(TimeSpan.FromSeconds(10)),
            "Rebuild should have started within 10 seconds");

        // Assert: old projection files must still be on disk while the rebuild is running.
        Assert.True(Directory.Exists(projectionPath),
            "Projection directory should exist during rebuild");
        Assert.True(Directory.GetFiles(projectionPath, "*.json").Length > 0,
            "Old projection files should remain on disk during rebuild");

        // Release the pause so the rebuild can finish.
        resumeSignal.Set();
        await rebuildTask;

        // Projection files should still exist after rebuild completes.
        Assert.True(Directory.GetFiles(projectionPath, "*.json").Length > 0,
            "Projection files should exist after rebuild");
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();

        if (Directory.Exists(_testStoragePath))
        {
            try
            {
                Directory.Delete(_testStoragePath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}

/// <summary>
/// Tests that projection rebuilds work correctly when RebuildBatchSize is smaller than the
/// total number of events, i.e., multiple read batches are needed.
/// </summary>
public class BatchedRebuildTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string _testStoragePath;
    private readonly IEventStore _eventStore;
    private readonly IProjectionManager _projectionManager;

    /// <summary>Small batch size to force multi-batch rebuilds with moderate test data sets.</summary>
    private const int SmallBatchSize = 100;

    public BatchedRebuildTests()
    {
        _testStoragePath = Path.Combine(
            Path.GetTempPath(),
            "OpossumBatchedRebuildTests",
            Guid.NewGuid().ToString());

        var services = new ServiceCollection();

        services.AddOpossum(options =>
        {
            options.RootPath = _testStoragePath;
            options.UseStore("BatchedRebuildContext");
        });

        services.AddProjections(options =>
        {
            options.EnableAutoRebuild = false;
            options.RebuildBatchSize = SmallBatchSize;
        });

        services.AddProjectionStore<PmTestItemState>("PmTestItems");

        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();
        _eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        _projectionManager = _serviceProvider.GetRequiredService<IProjectionManager>();
    }

    [Fact]
    public async Task RebuildAsync_WithMoreEventsThanBatchSize_BuildsAllProjectionsAsync()
    {
        // Arrange: seed more events than SmallBatchSize to require multiple read batches.
        _projectionManager.RegisterProjection(new PmTestItemProjection());

        var ids = Enumerable.Range(0, SmallBatchSize * 3).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in ids)
        {
            await _eventStore.AppendAsync(
                [new PmTestItemCreatedEvent(id, $"Item {id}")
                    .ToDomainEvent()
                    .WithTag("itemId", id.ToString())
                    .Build()],
                null);
        }

        // Act
        await _projectionManager.RebuildAsync("PmTestItems");

        // Assert: every item must appear in the projection store.
        var store = _serviceProvider.GetRequiredService<IProjectionStore<PmTestItemState>>();
        foreach (var id in ids)
        {
            var item = await store.GetAsync(id.ToString());
            Assert.NotNull(item);
            Assert.Equal(id, item.ItemId);
        }
    }

    [Fact]
    public async Task RebuildAsync_UpdateEventsAcrossBatchBoundary_AppliesAllUpdatesAsync()
    {
        // Arrange: create an item then update it — the update event may land in a different
        // batch than the create event, exercising cross-batch state continuity.
        _projectionManager.RegisterProjection(new PmTestItemProjection());

        var itemId = Guid.NewGuid();

        // Pad with unrelated events to push the create event near the end of batch 1.
        for (int i = 0; i < SmallBatchSize - 1; i++)
        {
            var otherId = Guid.NewGuid();
            await _eventStore.AppendAsync(
                [new PmTestItemCreatedEvent(otherId, $"Filler {i}")
                    .ToDomainEvent()
                    .WithTag("itemId", otherId.ToString())
                    .Build()],
                null);
        }

        // The create event is the last one in batch 1; the update event will be in batch 2.
        await _eventStore.AppendAsync(
            [new PmTestItemCreatedEvent(itemId, "Original")
                .ToDomainEvent()
                .WithTag("itemId", itemId.ToString())
                .Build()],
            null);

        await _eventStore.AppendAsync(
            [new PmTestItemUpdatedEvent(itemId, "Updated")
                .ToDomainEvent()
                .WithTag("itemId", itemId.ToString())
                .Build()],
            null);

        // Act
        await _projectionManager.RebuildAsync("PmTestItems");

        // Assert: the item should reflect the update, not the original value.
        var store = _serviceProvider.GetRequiredService<IProjectionStore<PmTestItemState>>();
        var item = await store.GetAsync(itemId.ToString());
        Assert.NotNull(item);
        Assert.Equal("Updated", item.Name);
    }

    [Fact]
    public async Task RebuildAsync_CheckpointReflectsLastEvent_AfterBatchedRebuildAsync()
    {
        // Arrange
        _projectionManager.RegisterProjection(new PmTestItemProjection());

        var eventCount = SmallBatchSize * 2 + 1;  // Ensure > 1 batch
        for (int i = 0; i < eventCount; i++)
        {
            var id = Guid.NewGuid();
            await _eventStore.AppendAsync(
                [new PmTestItemCreatedEvent(id, $"Item {i}")
                    .ToDomainEvent()
                    .WithTag("itemId", id.ToString())
                    .Build()],
                null);
        }

        // Act
        await _projectionManager.RebuildAsync("PmTestItems");

        // Assert: checkpoint must equal the last appended position.
        var checkpoint = await _projectionManager.GetCheckpointAsync("PmTestItems");
        Assert.Equal(eventCount, checkpoint);
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();

        if (Directory.Exists(_testStoragePath))
        {
            try { Directory.Delete(_testStoragePath, recursive: true); }
            catch { /* ignore cleanup errors */ }
        }
    }
}

// file-scoped helpers to avoid conflicts with assembly scanning and other test types
file record PmTestItemCreatedEvent(Guid ItemId, string Name) : IEvent;
file record PmTestItemUpdatedEvent(Guid ItemId, string Name) : IEvent;
file record PmTestItemDeletedEvent(Guid ItemId) : IEvent;

file record PmTestItemState
{
    public Guid ItemId { get; init; }
    public string Name { get; init; } = string.Empty;
}

file record SlowPmTestItemState
{
    public Guid ItemId { get; init; }
    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// A projection that pauses inside Apply to allow tests to observe file state during rebuild.
/// </summary>
file sealed class SlowPmTestItemProjection(
    ManualResetEventSlim startedSignal,
    ManualResetEventSlim resumeSignal) : IProjectionDefinition<SlowPmTestItemState>
{
    public string ProjectionName => "SlowPmTestItems";

    public string[] EventTypes =>
    [
        typeof(PmTestItemCreatedEvent).Name,
        typeof(PmTestItemUpdatedEvent).Name,
    ];

    public string KeySelector(SequencedEvent evt) =>
        evt.Event.Event switch
        {
            PmTestItemCreatedEvent e => e.ItemId.ToString(),
            PmTestItemUpdatedEvent e => e.ItemId.ToString(),
            _ => "unknown"
        };

    public SlowPmTestItemState? Apply(SlowPmTestItemState? current, SequencedEvent evt)
    {
        startedSignal.Set();   // Signal the test that Apply has been entered
        resumeSignal.Wait();   // Wait until the test releases the pause
        return evt.Event.Event switch
        {
            PmTestItemCreatedEvent e => new SlowPmTestItemState { ItemId = e.ItemId, Name = e.Name },
            PmTestItemUpdatedEvent e => current! with { Name = e.Name },
            _ => current
        };
    }
}

[ProjectionDefinition("PmTestItems")]
file class PmTestItemProjection : IProjectionDefinition<PmTestItemState>
{
    public string ProjectionName => "PmTestItems";

    public string[] EventTypes =>
    [
        typeof(PmTestItemCreatedEvent).Name,
        typeof(PmTestItemUpdatedEvent).Name,
        typeof(PmTestItemDeletedEvent).Name
    ];

    public string KeySelector(SequencedEvent evt)
    {
        return evt.Event.Event switch
        {
            PmTestItemCreatedEvent e => e.ItemId.ToString(),
            PmTestItemUpdatedEvent e => e.ItemId.ToString(),
            PmTestItemDeletedEvent e => e.ItemId.ToString(),
            _ => throw new InvalidOperationException($"Unknown event type: {evt.Event.EventType}")
        };
    }

    public PmTestItemState? Apply(PmTestItemState? current, SequencedEvent evt)
    {
        return evt.Event.Event switch
        {
            PmTestItemCreatedEvent e => new PmTestItemState { ItemId = e.ItemId, Name = e.Name },
            PmTestItemUpdatedEvent e => current! with { Name = e.Name },
            PmTestItemDeletedEvent => null,
            _ => current
        };
    }
}
