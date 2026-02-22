using Opossum.Core;
using Opossum.DependencyInjection;
using Opossum.Extensions;
using Opossum.Projections;

namespace Opossum.IntegrationTests.Projections;

public class ProjectionEndToEndTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string _testStoragePath;
    private readonly IEventStore _eventStore;
    private readonly IProjectionManager _projectionManager;

    public ProjectionEndToEndTests()
    {
        _testStoragePath = Path.Combine(
            Path.GetTempPath(),
            "OpossumE2ETests",
            Guid.NewGuid().ToString());

        var services = new ServiceCollection();

        services.AddOpossum(options =>
        {
            options.RootPath = _testStoragePath;
            options.AddContext("E2EContext");
        });

        services.AddProjections(options =>
        {
            options.ScanAssembly(typeof(ProjectionEndToEndTests).Assembly);
            options.PollingInterval = TimeSpan.FromMilliseconds(500); // Fast polling
            options.BatchSize = 100;
            options.EnableAutoRebuild = true;
        });

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
    public async Task EndToEnd_CreateAndQueryOrder_WorksCorrectly()
    {
        // Arrange - Register projection
        var projection = new E2EOrderProjection();
        _projectionManager.RegisterProjection(projection);

        var orderId = Guid.NewGuid();

        // Act - Create order events
        var events = new[]
        {
            new E2EOrderCreatedEvent(orderId, "Customer 1", "customer1@test.com")
                .ToDomainEvent()
                .WithTag("orderId", orderId.ToString())
                .Build(),

            new E2EItemAddedEvent(orderId, "Product A", 99.99m)
                .ToDomainEvent()
                .WithTag("orderId", orderId.ToString())
                .Build(),

            new E2EItemAddedEvent(orderId, "Product B", 49.99m)
                .ToDomainEvent()
                .WithTag("orderId", orderId.ToString())
                .Build()
        };

        await _eventStore.AppendAsync(events, null);

        // Rebuild projection
        await _projectionManager.RebuildAsync("E2EOrders");

        // Assert - Query projection
        var store = _serviceProvider.GetRequiredService<IProjectionStore<E2EOrderState>>();
        var order = await store.GetAsync(orderId.ToString());

        Assert.NotNull(order);
        Assert.Equal(orderId, order.OrderId);
        Assert.Equal("Customer 1", order.CustomerName);
        Assert.Equal("customer1@test.com", order.CustomerEmail);
        Assert.Equal(149.98m, order.TotalAmount);
        Assert.Equal(2, order.ItemCount);
        Assert.Equal(2, order.Items.Count);
        Assert.Contains(order.Items, i => i.ProductName == "Product A");
        Assert.Contains(order.Items, i => i.ProductName == "Product B");
    }

    [Fact]
    public async Task EndToEnd_MultipleOrders_QueriesWork()
    {
        // Arrange
        var projection = new E2EOrderProjection();
        _projectionManager.RegisterProjection(projection);

        var order1Id = Guid.NewGuid();
        var order2Id = Guid.NewGuid();
        var order3Id = Guid.NewGuid();

        // Act - Create multiple orders
        var events = new[]
        {
            new E2EOrderCreatedEvent(order1Id, "Customer 1", "c1@test.com")
                .ToDomainEvent().WithTag("orderId", order1Id.ToString()).Build(),
            new E2EItemAddedEvent(order1Id, "Product A", 100m)
                .ToDomainEvent().WithTag("orderId", order1Id.ToString()).Build(),

            new E2EOrderCreatedEvent(order2Id, "Customer 2", "c2@test.com")
                .ToDomainEvent().WithTag("orderId", order2Id.ToString()).Build(),
            new E2EItemAddedEvent(order2Id, "Product B", 200m)
                .ToDomainEvent().WithTag("orderId", order2Id.ToString()).Build(),
            new E2EItemAddedEvent(order2Id, "Product C", 300m)
                .ToDomainEvent().WithTag("orderId", order2Id.ToString()).Build(),

            new E2EOrderCreatedEvent(order3Id, "Customer 3", "c3@test.com")
                .ToDomainEvent().WithTag("orderId", order3Id.ToString()).Build(),
            new E2EItemAddedEvent(order3Id, "Product D", 50m)
                .ToDomainEvent().WithTag("orderId", order3Id.ToString()).Build()
        };

        await _eventStore.AppendAsync(events, null);
        await _projectionManager.RebuildAsync("E2EOrders");

        // Assert - Query all orders
        var store = _serviceProvider.GetRequiredService<IProjectionStore<E2EOrderState>>();
        var allOrders = await store.GetAllAsync();

        Assert.Equal(3, allOrders.Count);

        // Query expensive orders (>= 200m)
        var expensiveOrders = await store.QueryAsync(o => o.TotalAmount >= 200m);
        Assert.Single(expensiveOrders); // Only order2 with 500m total
        Assert.Contains(expensiveOrders, o => o.OrderId == order2Id);
        Assert.DoesNotContain(expensiveOrders, o => o.TotalAmount == 100m);
    }

    [Fact]
    public async Task EndToEnd_IncrementalUpdate_UpdatesProjection()
    {
        // Arrange
        var projection = new E2EOrderProjection();
        _projectionManager.RegisterProjection(projection);

        var orderId = Guid.NewGuid();

        // Create initial order
        var initialEvents = new[]
        {
            new E2EOrderCreatedEvent(orderId, "Customer", "customer@test.com")
                .ToDomainEvent().WithTag("orderId", orderId.ToString()).Build(),
            new E2EItemAddedEvent(orderId, "Product A", 100m)
                .ToDomainEvent().WithTag("orderId", orderId.ToString()).Build()
        };

        await _eventStore.AppendAsync(initialEvents, null);
        await _projectionManager.RebuildAsync("E2EOrders");

        var store = _serviceProvider.GetRequiredService<IProjectionStore<E2EOrderState>>();
        var orderBefore = await store.GetAsync(orderId.ToString());
        Assert.Equal(1, orderBefore!.ItemCount);
        Assert.Equal(100m, orderBefore.TotalAmount);

        // Act - Add more items
        var newEvents = new[]
        {
            new E2EItemAddedEvent(orderId, "Product B", 200m)
                .ToDomainEvent().WithTag("orderId", orderId.ToString()).Build()
        };

        await _eventStore.AppendAsync(newEvents, null);

        // Read all events and update projection
        var allEvents = await _eventStore.ReadAsync(Query.All(), null);
        var latestEvents = allEvents.OrderBy(e => e.Position).Skip(initialEvents.Length).ToArray();
        await _projectionManager.UpdateAsync(latestEvents);

        // Assert
        var orderAfter = await store.GetAsync(orderId.ToString());
        Assert.Equal(2, orderAfter!.ItemCount);
        Assert.Equal(300m, orderAfter.TotalAmount);
    }

    [Fact]
    public async Task EndToEnd_OrderCancellation_RemovesProjection()
    {
        // Arrange
        var projection = new E2EOrderProjection();
        _projectionManager.RegisterProjection(projection);

        var orderId = Guid.NewGuid();

        var events = new[]
        {
            new E2EOrderCreatedEvent(orderId, "Customer", "customer@test.com")
                .ToDomainEvent().WithTag("orderId", orderId.ToString()).Build(),
            new E2EItemAddedEvent(orderId, "Product A", 100m)
                .ToDomainEvent().WithTag("orderId", orderId.ToString()).Build(),
            new E2EOrderCancelledEvent(orderId)
                .ToDomainEvent().WithTag("orderId", orderId.ToString()).Build()
        };

        await _eventStore.AppendAsync(events, null);

        // Act
        await _projectionManager.RebuildAsync("E2EOrders");

        // Assert
        var store = _serviceProvider.GetRequiredService<IProjectionStore<E2EOrderState>>();
        var order = await store.GetAsync(orderId.ToString());
        Assert.Null(order); // Should be deleted
    }

    [Fact]
    public async Task EndToEnd_CheckpointManagement_TracksProgress()
    {
        // Arrange
        var projection = new E2EOrderProjection();
        _projectionManager.RegisterProjection(projection);

        var initialCheckpoint = await _projectionManager.GetCheckpointAsync("E2EOrders");
        Assert.Equal(0, initialCheckpoint);

        // Act - Create events and rebuild
        var orderId = Guid.NewGuid();
        var events = new[]
        {
            new E2EOrderCreatedEvent(orderId, "Customer", "customer@test.com")
                .ToDomainEvent().WithTag("orderId", orderId.ToString()).Build(),
            new E2EItemAddedEvent(orderId, "Product", 100m)
                .ToDomainEvent().WithTag("orderId", orderId.ToString()).Build()
        };

        await _eventStore.AppendAsync(events, null);
        await _projectionManager.RebuildAsync("E2EOrders");

        // Assert
        var checkpointAfterRebuild = await _projectionManager.GetCheckpointAsync("E2EOrders");
        Assert.True(checkpointAfterRebuild > 0);

        // Verify checkpoint file exists
        var checkpointPath = Path.Combine(
            _testStoragePath,
            "E2EContext",
            "Projections",
            "_checkpoints",
            "E2EOrders.checkpoint");

        Assert.True(File.Exists(checkpointPath));
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
            catch { }
        }
    }

    // Test projection
    [ProjectionDefinition("E2EOrders")]
    private class E2EOrderProjection : IProjectionDefinition<E2EOrderState>
    {
        public string ProjectionName => "E2EOrders";

        public string[] EventTypes =>
        [
            nameof(E2EOrderCreatedEvent),
            nameof(E2EItemAddedEvent),
            nameof(E2EOrderCancelledEvent)
        ];

        public string KeySelector(SequencedEvent evt)
        {
            return evt.Event.Tags.First(t => t.Key == "orderId").Value;
        }

        public E2EOrderState? Apply(E2EOrderState? current, IEvent evt)
        {
            return evt switch
            {
                E2EOrderCreatedEvent created => new E2EOrderState(
                    created.OrderId,
                    created.CustomerName,
                    created.CustomerEmail,
                    0m,
                    0,
                    []),

                E2EItemAddedEvent itemAdded when current != null => current with
                {
                    TotalAmount = current.TotalAmount + itemAdded.Price,
                    ItemCount = current.ItemCount + 1,
                    Items =

                    [
                        .. current.Items,
                        .. new[]
                                {
                                    new E2EOrderItem(itemAdded.ProductName, itemAdded.Price)
                                },
                    ]
                },

                E2EOrderCancelledEvent => null,

                _ => current
            };
        }
    }

    // Test events
    private record E2EOrderCreatedEvent(Guid OrderId, string CustomerName, string CustomerEmail) : IEvent;
    private record E2EItemAddedEvent(Guid OrderId, string ProductName, decimal Price) : IEvent;
    private record E2EOrderCancelledEvent(Guid OrderId) : IEvent;

    // Test state
    private record E2EOrderState(
        Guid OrderId,
        string CustomerName,
        string CustomerEmail,
        decimal TotalAmount,
        int ItemCount,
        List<E2EOrderItem> Items);

    private record E2EOrderItem(string ProductName, decimal Price);
}
