using Opossum.Core;
using Opossum.Projections;

namespace Opossum.UnitTests.Projections;

public class ProjectionDefinitionTests
{
    [Fact]
    public void ProjectionDefinition_WithBasicEvents_AppliesCorrectly()
    {
        // Arrange
        var projection = new TestOrderProjection();

        // Act & Assert
        Assert.Equal("OrderSummary", projection.ProjectionName);
        Assert.Equal(3, projection.EventTypes.Length);
        Assert.Contains("OrderCreated", projection.EventTypes);
        Assert.Contains("ItemAdded", projection.EventTypes);
        Assert.Contains("OrderCancelled", projection.EventTypes);
    }

    [Fact]
    public void KeySelector_WithValidEvent_ExtractsKey()
    {
        // Arrange
        var projection = new TestOrderProjection();
        var orderId = Guid.NewGuid();
        var evt = CreateSequencedEvent(new OrderCreatedEvent(orderId, "Customer"), orderId);

        // Act
        var key = projection.KeySelector(evt);

        // Assert
        Assert.Equal(orderId.ToString(), key);
    }

    [Fact]
    public void Apply_WithOrderCreatedEvent_CreatesNewProjection()
    {
        // Arrange
        var projection = new TestOrderProjection();
        var orderId = Guid.NewGuid();
        var evt = new OrderCreatedEvent(orderId, "Customer A");

        // Act
        var result = projection.Apply(null, evt);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(orderId, result.OrderId);
        Assert.Equal("Customer A", result.CustomerName);
        Assert.Equal(0m, result.TotalAmount);
        Assert.Equal(0, result.ItemCount);
    }

    [Fact]
    public void Apply_WithItemAddedEvent_UpdatesProjection()
    {
        // Arrange
        var projection = new TestOrderProjection();
        var orderId = Guid.NewGuid();
        var current = new OrderSummary(orderId, "Customer A", 100m, 1);
        var evt = new ItemAddedEvent(orderId, 50m);

        // Act
        var result = projection.Apply(current, evt);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(150m, result.TotalAmount);
        Assert.Equal(2, result.ItemCount);
    }

    [Fact]
    public void Apply_WithOrderCancelledEvent_ReturnsNull()
    {
        // Arrange
        var projection = new TestOrderProjection();
        var orderId = Guid.NewGuid();
        var current = new OrderSummary(orderId, "Customer A", 100m, 1);
        var evt = new OrderCancelledEvent(orderId);

        // Act
        var result = projection.Apply(current, evt);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Apply_WithUnknownEvent_ReturnsCurrentState()
    {
        // Arrange
        var projection = new TestOrderProjection();
        var orderId = Guid.NewGuid();
        var current = new OrderSummary(orderId, "Customer A", 100m, 1);
        var evt = new UnknownEvent();

        // Act
        var result = projection.Apply(current, evt);

        // Assert
        Assert.Same(current, result);
    }

    [Fact]
    public void Apply_WithItemAddedEvent_WhenCurrentIsNull_ReturnsNull()
    {
        // Arrange
        var projection = new TestOrderProjection();
        var evt = new ItemAddedEvent(Guid.NewGuid(), 50m);

        // Act
        var result = projection.Apply(null, evt);

        // Assert
        Assert.Null(result);
    }

    private static SequencedEvent CreateSequencedEvent(IEvent evt, Guid orderId)
    {
        return new SequencedEvent
        {
            Position = 1,
            Event = new DomainEvent
            {
                EventType = evt.GetType().Name,
                Event = evt,
                Tags =
                [
                    new Tag { Key = "orderId", Value = orderId.ToString() }
                ]
            },
            Metadata = new Metadata()
        };
    }

    // Test projection definition
    [ProjectionDefinition("OrderSummary")]
    private class TestOrderProjection : IProjectionDefinition<OrderSummary>
    {
        public string ProjectionName => "OrderSummary";

        public string[] EventTypes =>
        [
            "OrderCreated",
            "ItemAdded",
            "OrderCancelled"
        ];

        public string KeySelector(SequencedEvent evt)
        {
            var orderIdTag = evt.Event.Tags.FirstOrDefault(t => t.Key == "orderId");
            return orderIdTag?.Value ?? throw new InvalidOperationException("Missing orderId");
        }

        public OrderSummary? Apply(OrderSummary? current, IEvent evt)
        {
            return evt switch
            {
                OrderCreatedEvent created => new OrderSummary(
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
    private record UnknownEvent : IEvent;

    // Test projection state
    private record OrderSummary(
        Guid OrderId,
        string CustomerName,
        decimal TotalAmount,
        int ItemCount);
}
