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
    public async Task GetCheckpointAsync_ForNewProjection_ReturnsZero()
    {
        // Arrange
        _projectionManager.RegisterProjection(new PmTestItemProjection());

        // Act
        var checkpoint = await _projectionManager.GetCheckpointAsync("PmTestItems");

        // Assert
        Assert.Equal(0, checkpoint);
    }

    [Fact]
    public async Task RebuildAsync_WithExistingEvents_BuildsProjection()
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
    public async Task RebuildAsync_WithNonExistentProjection_ThrowsInvalidOperationException()
    {
        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _projectionManager.RebuildAsync("NonExistent"));

        Assert.Contains("NonExistent", ex.Message);
        Assert.Contains("not registered", ex.Message);
    }

    [Fact]
    public async Task RebuildAsync_WithDeleteEvent_RemovesProjection()
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
    public async Task SaveCheckpointAsync_SavesCheckpoint()
    {
        // Act
        await _projectionManager.SaveCheckpointAsync("PmTestItems", 42);

        // Assert
        var checkpoint = await _projectionManager.GetCheckpointAsync("PmTestItems");
        Assert.Equal(42, checkpoint);
    }

    [Fact]
    public async Task SaveCheckpointAsync_MultipleTimes_UpdatesCheckpoint()
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
    public async Task UpdateAsync_WithNewEvents_UpdatesProjections()
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

// file-scoped helpers to avoid conflicts with assembly scanning and other test types
file record PmTestItemCreatedEvent(Guid ItemId, string Name) : IEvent;
file record PmTestItemUpdatedEvent(Guid ItemId, string Name) : IEvent;
file record PmTestItemDeletedEvent(Guid ItemId) : IEvent;

file record PmTestItemState
{
    public Guid ItemId { get; init; }
    public string Name { get; init; } = string.Empty;
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
