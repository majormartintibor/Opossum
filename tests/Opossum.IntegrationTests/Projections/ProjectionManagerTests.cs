using Opossum.Core;
using Opossum.Extensions;
using Opossum.Projections;

namespace Opossum.IntegrationTests.Projections;

public class ProjectionManagerTests : IClassFixture<ProjectionFixture>
{
    private readonly ProjectionFixture _fixture;

    public ProjectionManagerTests(ProjectionFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void RegisterProjection_WithValidDefinition_RegistersSuccessfully()
    {
        // Arrange
        var uniqueName = $"TestOrders_{Guid.NewGuid()}";
        var definition = new TestOrderProjectionWithName(uniqueName);

        // Act
        _fixture.ProjectionManager.RegisterProjection(definition);

        // Assert
        var registeredProjections = _fixture.ProjectionManager.GetRegisteredProjections();
        Assert.Contains(uniqueName, registeredProjections);
    }

    [Fact]
    public void RegisterProjection_WithDuplicateName_ThrowsInvalidOperationException()
    {
        // Arrange
        var definition1 = new TestOrderProjection();
        var definition2 = new TestOrderProjection();
        _fixture.ProjectionManager.RegisterProjection(definition1);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            _fixture.ProjectionManager.RegisterProjection(definition2));
        Assert.Contains("already registered", exception.Message);
    }

    [Fact]
    public async Task GetCheckpointAsync_ForNewProjection_ReturnsZero()
    {
        // Arrange
        var projectionName = $"TestProjection_{Guid.NewGuid()}";

        // Act
        var checkpoint = await _fixture.ProjectionManager.GetCheckpointAsync(projectionName);

        // Assert
        Assert.Equal(0, checkpoint);
    }

    [Fact]
    public async Task SaveCheckpointAsync_SavesCheckpoint()
    {
        // Arrange
        var projectionName = $"TestProjection_{Guid.NewGuid()}";
        var position = 12345L;

        // Act
        await _fixture.ProjectionManager.SaveCheckpointAsync(projectionName, position);

        // Assert
        var retrieved = await _fixture.ProjectionManager.GetCheckpointAsync(projectionName);
        Assert.Equal(position, retrieved);
    }

    [Fact]
    public async Task SaveCheckpointAsync_MultipleTimes_UpdatesCheckpoint()
    {
        // Arrange
        var projectionName = $"TestProjection_{Guid.NewGuid()}";

        // Act
        await _fixture.ProjectionManager.SaveCheckpointAsync(projectionName, 100);
        await _fixture.ProjectionManager.SaveCheckpointAsync(projectionName, 200);
        await _fixture.ProjectionManager.SaveCheckpointAsync(projectionName, 300);

        // Assert
        var retrieved = await _fixture.ProjectionManager.GetCheckpointAsync(projectionName);
        Assert.Equal(300, retrieved);
    }

    [Fact]
    public async Task RebuildAsync_WithExistingEvents_BuildsProjection()
    {
        // Arrange
        var orderId = Guid.NewGuid();

        // Create test events
        var events = new[]
        {
            new OrderCreatedEvent(orderId, "Customer A").ToDomainEvent()
                .WithTag("orderId", orderId.ToString())
                .Build(),
            new ItemAddedEvent(orderId, 100m).ToDomainEvent()
                .WithTag("orderId", orderId.ToString())
                .Build(),
            new ItemAddedEvent(orderId, 50m).ToDomainEvent()
                .WithTag("orderId", orderId.ToString())
                .Build()
        };

        await _fixture.EventStore.AppendAsync(events, null);

        // Register projection
        var projectionName = $"Orders_{Guid.NewGuid()}";
        var definition = new TestOrderProjectionWithName(projectionName);
        _fixture.ProjectionManager.RegisterProjection(definition);

        // Create and manually "register" the store (since we're not using auto-discovery)
        var store = new FileSystemProjectionStore<OrderSummaryState>(
            _fixture.OpossumOptions,
            projectionName);

        // Act
        await _fixture.ProjectionManager.RebuildAsync(projectionName);

        // Assert
        var projection = await store.GetAsync(orderId.ToString());
        Assert.NotNull(projection);
        Assert.Equal(orderId, projection.OrderId);
        Assert.Equal("Customer A", projection.CustomerName);
        Assert.Equal(150m, projection.TotalAmount);
        Assert.Equal(2, projection.ItemCount);

        // Verify checkpoint was saved
        var checkpoint = await _fixture.ProjectionManager.GetCheckpointAsync(projectionName);
        Assert.True(checkpoint > 0);
    }

    [Fact]
    public async Task UpdateAsync_WithNewEvents_UpdatesProjections()
    {
        // Arrange
        var orderId = Guid.NewGuid();

        // Create initial event
        var createEvent = new OrderCreatedEvent(orderId, "Customer A").ToDomainEvent()
            .WithTag("orderId", orderId.ToString())
            .Build();

        await _fixture.EventStore.AppendAsync(new[] { createEvent }, null);

        // Register projection and rebuild
        var projectionName = $"Orders_{Guid.NewGuid()}";
        var definition = new TestOrderProjectionWithName(projectionName);
        _fixture.ProjectionManager.RegisterProjection(definition);
        await _fixture.ProjectionManager.RebuildAsync(projectionName);

        // Create new events
        var newEvents = new[]
        {
            new ItemAddedEvent(orderId, 75m).ToDomainEvent()
                .WithTag("orderId", orderId.ToString())
                .Build()
        };

        await _fixture.EventStore.AppendAsync(newEvents, null);

        // Read back the new events
        var allEvents = await _fixture.EventStore.ReadAsync(Query.All(), null);
        var latestEvents = allEvents.OrderBy(e => e.Position).Skip(1).ToArray();

        // Create store manually
        var store = new FileSystemProjectionStore<OrderSummaryState>(
            _fixture.OpossumOptions,
            projectionName);

        // Act
        await _fixture.ProjectionManager.UpdateAsync(latestEvents);

        // Assert
        var projection = await store.GetAsync(orderId.ToString());
        Assert.NotNull(projection);
        Assert.Equal(75m, projection.TotalAmount);
        Assert.Equal(1, projection.ItemCount);
    }

    [Fact]
    public async Task RebuildAsync_WithNonExistentProjection_ThrowsInvalidOperationException()
    {
        // Arrange
        var nonExistentProjection = "NonExistentProjection";

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fixture.ProjectionManager.RebuildAsync(nonExistentProjection));
    }

    [Fact]
    public async Task RebuildAsync_WithDeleteEvent_RemovesProjection()
    {
        // Arrange
        var orderId = Guid.NewGuid();

        var events = new[]
        {
            new OrderCreatedEvent(orderId, "Customer A").ToDomainEvent()
                .WithTag("orderId", orderId.ToString())
                .Build(),
            new OrderCancelledEvent(orderId).ToDomainEvent()
                .WithTag("orderId", orderId.ToString())
                .Build()
        };

        await _fixture.EventStore.AppendAsync(events, null);

        var projectionName = $"Orders_{Guid.NewGuid()}";
        var definition = new TestOrderProjectionWithName(projectionName);
        _fixture.ProjectionManager.RegisterProjection(definition);

        // Create store manually
        var store = new FileSystemProjectionStore<OrderSummaryState>(
            _fixture.OpossumOptions,
            projectionName);

        // Act
        await _fixture.ProjectionManager.RebuildAsync(projectionName);

        // Assert
        var projection = await store.GetAsync(orderId.ToString());
        Assert.Null(projection); // Should be deleted
    }

    // Test projection definition
    private class TestOrderProjection : IProjectionDefinition<OrderSummaryState>
    {
        public string ProjectionName => "TestOrders";

        public string[] EventTypes => new[]
        {
            nameof(OrderCreatedEvent),
            nameof(ItemAddedEvent),
            nameof(OrderCancelledEvent)
        };

        public string KeySelector(SequencedEvent evt)
        {
            var orderIdTag = evt.Event.Tags.FirstOrDefault(t => t.Key == "orderId");
            return orderIdTag?.Value ?? throw new InvalidOperationException("Missing orderId");
        }

        public OrderSummaryState? Apply(OrderSummaryState? current, IEvent evt)
        {
            return evt switch
            {
                OrderCreatedEvent created => new OrderSummaryState(
                    created.OrderId,
                    created.CustomerName,
                    0m,
                    0),

                ItemAddedEvent itemAdded when current != null => current with
                {
                    TotalAmount = current.TotalAmount + itemAdded.Price,
                    ItemCount = current.ItemCount + 1
                },

                OrderCancelledEvent => null,

                _ => current
            };
        }
    }

    private class TestOrderProjectionWithName : IProjectionDefinition<OrderSummaryState>
    {
        private readonly string _name;

        public TestOrderProjectionWithName(string name)
        {
            _name = name;
        }

        public string ProjectionName => _name;

        public string[] EventTypes => new[]
        {
            nameof(OrderCreatedEvent),
            nameof(ItemAddedEvent),
            nameof(OrderCancelledEvent)
        };

        public string KeySelector(SequencedEvent evt)
        {
            var orderIdTag = evt.Event.Tags.FirstOrDefault(t => t.Key == "orderId");
            return orderIdTag?.Value ?? throw new InvalidOperationException("Missing orderId");
        }

        public OrderSummaryState? Apply(OrderSummaryState? current, IEvent evt)
        {
            return evt switch
            {
                OrderCreatedEvent created => new OrderSummaryState(
                    created.OrderId,
                    created.CustomerName,
                    0m,
                    0),

                ItemAddedEvent itemAdded when current != null => current with
                {
                    TotalAmount = current.TotalAmount + itemAdded.Price,
                    ItemCount = current.ItemCount + 1
                },

                OrderCancelledEvent => null,

                _ => current
            };
        }
    }

    // Test events
    private record OrderCreatedEvent(Guid OrderId, string CustomerName) : IEvent;
    private record ItemAddedEvent(Guid OrderId, decimal Price) : IEvent;
    private record OrderCancelledEvent(Guid OrderId) : IEvent;

    // Test state
    private record OrderSummaryState(
        Guid OrderId,
        string CustomerName,
        decimal TotalAmount,
        int ItemCount);
}
