using Opossum.Core;
using Opossum.DependencyInjection;
using Opossum.Extensions;
using Opossum.Projections;

namespace Opossum.IntegrationTests.Projections;

/// <summary>
/// Integration tests for IMultiStreamProjectionDefinition feature.
/// Tests the ability of projections to query and use related events from other streams.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public class MultiStreamProjectionTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string _testStoragePath;
    private readonly IEventStore _eventStore;
    private readonly IProjectionManager _projectionManager;

    public MultiStreamProjectionTests()
    {
        _testStoragePath = Path.Combine(
            Path.GetTempPath(),
            "OpossumMultiStreamTests",
            Guid.NewGuid().ToString());

        var services = new ServiceCollection();

        services.AddOpossum(options =>
        {
            options.RootPath = _testStoragePath;
            options.AddContext("MultiStreamContext");
        });

        services.AddProjections(options =>
        {
            options.ScanAssembly(typeof(MultiStreamProjectionTests).Assembly);
        });

        _serviceProvider = services.BuildServiceProvider();
        _eventStore = _serviceProvider.GetRequiredService<IEventStore>();
        _projectionManager = _serviceProvider.GetRequiredService<IProjectionManager>();
    }

    [Fact]
    public async Task MultiStreamProjection_WithRelatedEvents_BuildsCorrectState()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var postId = Guid.NewGuid();

        var projection = new PostWithAuthorProjection();
        _projectionManager.RegisterProjection(projection);

        // Act - Create user first
        var userCreatedEvent = new UserCreatedEvent(userId, "John Doe", "john@example.com")
            .ToDomainEvent()
            .WithTag("userId", userId.ToString())
            .Build();

        await _eventStore.AppendAsync([userCreatedEvent], null);

        // Create post (will query for user info)
        var postCreatedEvent = new PostCreatedEvent(postId, userId, "My First Post", "Hello World")
            .ToDomainEvent()
            .WithTag("postId", postId.ToString())
            .WithTag("userId", userId.ToString())
            .Build();

        await _eventStore.AppendAsync([postCreatedEvent], null);

        // Rebuild projection
        await _projectionManager.RebuildAsync("PostsWithAuthor");

        // Assert
        var store = _serviceProvider.GetRequiredService<IProjectionStore<PostWithAuthorState>>();
        var post = await store.GetAsync(postId.ToString());

        Assert.NotNull(post);
        Assert.Equal(postId, post.PostId);
        Assert.Equal("My First Post", post.Title);
        Assert.Equal("Hello World", post.Content);
        Assert.Equal(userId, post.AuthorId);
        Assert.Equal("John Doe", post.AuthorName);
        Assert.Equal("john@example.com", post.AuthorEmail);
    }

    [Fact]
    public async Task MultiStreamProjection_WithMissingRelatedEvents_ThrowsException()
    {
        // Arrange
        var postId = Guid.NewGuid();
        var nonExistentUserId = Guid.NewGuid();

        var projection = new PostWithAuthorProjection();
        _projectionManager.RegisterProjection(projection);

        // Act - Create post without creating user first
        var postCreatedEvent = new PostCreatedEvent(postId, nonExistentUserId, "Orphaned Post", "No author")
            .ToDomainEvent()
            .WithTag("postId", postId.ToString())
            .WithTag("userId", nonExistentUserId.ToString())
            .Build();

        await _eventStore.AppendAsync([postCreatedEvent], null);

        // Assert - Should throw when related event not found
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _projectionManager.RebuildAsync("PostsWithAuthor"));
    }

    [Fact]
    public async Task MultiStreamProjection_WithMultipleRelatedEvents_UsesLatest()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var postId = Guid.NewGuid();

        var projection = new PostWithAuthorProjection();
        _projectionManager.RegisterProjection(projection);

        // Act - Create user
        var userCreatedEvent = new UserCreatedEvent(userId, "John Doe", "john@example.com")
            .ToDomainEvent()
            .WithTag("userId", userId.ToString())
            .Build();

        await _eventStore.AppendAsync([userCreatedEvent], null);

        // Update user name
        var userNameChangedEvent = new UserNameChangedEvent(userId, "John Smith")
            .ToDomainEvent()
            .WithTag("userId", userId.ToString())
            .Build();

        await _eventStore.AppendAsync([userNameChangedEvent], null);

        // Create post
        var postCreatedEvent = new PostCreatedEvent(postId, userId, "My Post", "Content")
            .ToDomainEvent()
            .WithTag("postId", postId.ToString())
            .WithTag("userId", userId.ToString())
            .Build();

        await _eventStore.AppendAsync([postCreatedEvent], null);

        // Rebuild projection
        await _projectionManager.RebuildAsync("PostsWithAuthor");

        // Assert - Should use updated name
        var store = _serviceProvider.GetRequiredService<IProjectionStore<PostWithAuthorState>>();
        var post = await store.GetAsync(postId.ToString());

        Assert.NotNull(post);
        Assert.Equal("John Smith", post.AuthorName); // Updated name, not original
    }

    [Fact]
    public async Task MultiStreamProjection_WithNoRelatedEventsNeeded_WorksCorrectly()
    {
        // Arrange
        var postId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var projection = new PostWithAuthorProjection();
        _projectionManager.RegisterProjection(projection);

        // Act - Create post first (no user yet)
        var postCreatedEvent = new PostCreatedEvent(postId, userId, "Initial Post", "Content")
            .ToDomainEvent()
            .WithTag("postId", postId.ToString())
            .WithTag("userId", userId.ToString())
            .Build();

        await _eventStore.AppendAsync([postCreatedEvent], null);

        // Update post title (doesn't need related events)
        var postTitleChangedEvent = new PostTitleChangedEvent(postId, "Updated Title")
            .ToDomainEvent()
            .WithTag("postId", postId.ToString())
            .Build();

        await _eventStore.AppendAsync([postTitleChangedEvent], null);

        // This should fail during rebuild when processing PostCreatedEvent
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _projectionManager.RebuildAsync("PostsWithAuthor"));
    }

    [Fact]
    public async Task MultiStreamProjection_WithComplexRelationships_BuildsCorrectly()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var product1Id = Guid.NewGuid();
        var product2Id = Guid.NewGuid();

        var projection = new OrderSummaryProjection();
        _projectionManager.RegisterProjection(projection);

        // Act - Create customer
        var customerCreatedEvent = new CustomerCreatedEvent(customerId, "Jane Doe", "VIP")
            .ToDomainEvent()
            .WithTag("customerId", customerId.ToString())
            .Build();

        await _eventStore.AppendAsync([customerCreatedEvent], null);

        // Create products
        var product1Event = new ProductCreatedEvent(product1Id, "Laptop", 999.99m)
            .ToDomainEvent()
            .WithTag("productId", product1Id.ToString())
            .Build();

        var product2Event = new ProductCreatedEvent(product2Id, "Mouse", 29.99m)
            .ToDomainEvent()
            .WithTag("productId", product2Id.ToString())
            .Build();

        await _eventStore.AppendAsync([product1Event, product2Event], null);

        // Create order with items
        var orderCreatedEvent = new OrderCreatedEvent(orderId, customerId)
            .ToDomainEvent()
            .WithTag("orderId", orderId.ToString())
            .WithTag("customerId", customerId.ToString())
            .Build();

        var item1AddedEvent = new OrderItemAddedEvent(orderId, product1Id, 1)
            .ToDomainEvent()
            .WithTag("orderId", orderId.ToString())
            .WithTag("productId", product1Id.ToString())
            .Build();

        var item2AddedEvent = new OrderItemAddedEvent(orderId, product2Id, 2)
            .ToDomainEvent()
            .WithTag("orderId", orderId.ToString())
            .WithTag("productId", product2Id.ToString())
            .Build();

        await _eventStore.AppendAsync([orderCreatedEvent, item1AddedEvent, item2AddedEvent], null);

        // Rebuild projection
        await _projectionManager.RebuildAsync("OrderSummary");

        // Assert
        var store = _serviceProvider.GetRequiredService<IProjectionStore<OrderSummaryState>>();
        var order = await store.GetAsync(orderId.ToString());

        Assert.NotNull(order);
        Assert.Equal(orderId, order.OrderId);
        Assert.Equal(customerId, order.CustomerId);
        Assert.Equal("Jane Doe", order.CustomerName);
        Assert.Equal("VIP", order.CustomerTier);
        Assert.Equal(2, order.Items.Count);
        
        var laptopItem = order.Items.FirstOrDefault(i => i.ProductId == product1Id);
        Assert.NotNull(laptopItem);
        Assert.Equal("Laptop", laptopItem.ProductName);
        Assert.Equal(999.99m, laptopItem.Price);
        Assert.Equal(1, laptopItem.Quantity);

        var mouseItem = order.Items.FirstOrDefault(i => i.ProductId == product2Id);
        Assert.NotNull(mouseItem);
        Assert.Equal("Mouse", mouseItem.ProductName);
        Assert.Equal(29.99m, mouseItem.Price);
        Assert.Equal(2, mouseItem.Quantity);

        Assert.Equal(1059.97m, order.TotalAmount);
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

// ============================================================================
// TEST EVENTS
// ============================================================================

public record UserCreatedEvent(Guid UserId, string Name, string Email) : IEvent;
public record UserNameChangedEvent(Guid UserId, string NewName) : IEvent;
public record PostCreatedEvent(Guid PostId, Guid AuthorId, string Title, string Content) : IEvent;
public record PostTitleChangedEvent(Guid PostId, string NewTitle) : IEvent;
public record CustomerCreatedEvent(Guid CustomerId, string Name, string Tier) : IEvent;
public record ProductCreatedEvent(Guid ProductId, string Name, decimal Price) : IEvent;
public record OrderCreatedEvent(Guid OrderId, Guid CustomerId) : IEvent;
public record OrderItemAddedEvent(Guid OrderId, Guid ProductId, int Quantity) : IEvent;

// ============================================================================
// TEST PROJECTIONS
// ============================================================================

public record PostWithAuthorState
{
    public Guid PostId { get; init; }
    public Guid AuthorId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string AuthorName { get; init; } = string.Empty;
    public string AuthorEmail { get; init; } = string.Empty;
}

[ProjectionDefinition("PostsWithAuthorProjection")]
public class PostWithAuthorProjection : IMultiStreamProjectionDefinition<PostWithAuthorState>
{
    public string ProjectionName => "PostsWithAuthor";

    public string[] EventTypes => [
        nameof(PostCreatedEvent),
        nameof(PostTitleChangedEvent)
    ];

    public string KeySelector(SequencedEvent evt)
    {
        return evt.Event.Event switch
        {
            PostCreatedEvent pce => pce.PostId.ToString(),
            PostTitleChangedEvent ptce => ptce.PostId.ToString(),
            _ => throw new InvalidOperationException($"Unknown event type: {evt.Event.EventType}")
        };
    }

    public Query? GetRelatedEventsQuery(IEvent evt)
    {
        return evt switch
        {
            PostCreatedEvent pce => Query.FromItems(new QueryItem
            {
                Tags = [new Tag { Key = "userId", Value = pce.AuthorId.ToString() }],
                EventTypes = [nameof(UserCreatedEvent), nameof(UserNameChangedEvent)]
            }),
            _ => null
        };
    }

    public PostWithAuthorState? Apply(PostWithAuthorState? current, IEvent evt, SequencedEvent[] relatedEvents)
    {
        return evt switch
        {
            PostCreatedEvent pce => ApplyPostCreated(pce, relatedEvents),
            PostTitleChangedEvent ptce => current! with { Title = ptce.NewTitle },
            _ => current
        };
    }

    private PostWithAuthorState ApplyPostCreated(PostCreatedEvent evt, SequencedEvent[] relatedEvents)
    {
        // Build user state from related events
        string userName = string.Empty;
        string userEmail = string.Empty;

        foreach (var relatedEvent in relatedEvents.OrderBy(e => e.Position))
        {
            switch (relatedEvent.Event.Event)
            {
                case UserCreatedEvent uce:
                    userName = uce.Name;
                    userEmail = uce.Email;
                    break;
                case UserNameChangedEvent unce:
                    userName = unce.NewName;
                    break;
            }
        }

        if (string.IsNullOrEmpty(userName))
        {
            throw new InvalidOperationException(
                $"Cannot create post projection: User {evt.AuthorId} not found in related events");
        }

        return new PostWithAuthorState
        {
            PostId = evt.PostId,
            AuthorId = evt.AuthorId,
            Title = evt.Title,
            Content = evt.Content,
            AuthorName = userName,
            AuthorEmail = userEmail
        };
    }
}

public record OrderItemInfo
{
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int Quantity { get; init; }
}

public record OrderSummaryState
{
    public Guid OrderId { get; init; }
    public Guid CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerTier { get; init; } = string.Empty;
    public List<OrderItemInfo> Items { get; init; } = new();
    public decimal TotalAmount { get; init; }
}

[ProjectionDefinition("OrderSummaryProjection")]
public class OrderSummaryProjection : IMultiStreamProjectionDefinition<OrderSummaryState>
{
    public string ProjectionName => "OrderSummary";

    public string[] EventTypes => [
        nameof(OrderCreatedEvent),
        nameof(OrderItemAddedEvent)
    ];

    public string KeySelector(SequencedEvent evt)
    {
        return evt.Event.Event switch
        {
            OrderCreatedEvent oce => oce.OrderId.ToString(),
            OrderItemAddedEvent oiae => oiae.OrderId.ToString(),
            _ => throw new InvalidOperationException($"Unknown event type: {evt.Event.EventType}")
        };
    }

    public Query? GetRelatedEventsQuery(IEvent evt)
    {
        return evt switch
        {
            OrderCreatedEvent oce => Query.FromItems(new QueryItem
            {
                Tags = [new Tag { Key = "customerId", Value = oce.CustomerId.ToString() }],
                EventTypes = [nameof(CustomerCreatedEvent)]
            }),
            OrderItemAddedEvent oiae => Query.FromItems(new QueryItem
            {
                Tags = [new Tag { Key = "productId", Value = oiae.ProductId.ToString() }],
                EventTypes = [nameof(ProductCreatedEvent)]
            }),
            _ => null
        };
    }

    public OrderSummaryState? Apply(OrderSummaryState? current, IEvent evt, SequencedEvent[] relatedEvents)
    {
        return evt switch
        {
            OrderCreatedEvent oce => ApplyOrderCreated(oce, relatedEvents),
            OrderItemAddedEvent oiae => ApplyItemAdded(current!, oiae, relatedEvents),
            _ => current
        };
    }

    private OrderSummaryState ApplyOrderCreated(OrderCreatedEvent evt, SequencedEvent[] relatedEvents)
    {
        var customerEvent = relatedEvents
            .Select(e => e.Event.Event)
            .OfType<CustomerCreatedEvent>()
            .FirstOrDefault();

        if (customerEvent == null)
        {
            throw new InvalidOperationException(
                $"Cannot create order: Customer {evt.CustomerId} not found");
        }

        return new OrderSummaryState
        {
            OrderId = evt.OrderId,
            CustomerId = evt.CustomerId,
            CustomerName = customerEvent.Name,
            CustomerTier = customerEvent.Tier,
            Items = new(),
            TotalAmount = 0
        };
    }

    private OrderSummaryState ApplyItemAdded(OrderSummaryState current, OrderItemAddedEvent evt, SequencedEvent[] relatedEvents)
    {
        var productEvent = relatedEvents
            .Select(e => e.Event.Event)
            .OfType<ProductCreatedEvent>()
            .FirstOrDefault();

        if (productEvent == null)
        {
            throw new InvalidOperationException(
                $"Cannot add item: Product {evt.ProductId} not found");
        }

        var newItem = new OrderItemInfo
        {
            ProductId = evt.ProductId,
            ProductName = productEvent.Name,
            Price = productEvent.Price,
            Quantity = evt.Quantity
        };

        var newItems = new List<OrderItemInfo>(current.Items) { newItem };
        var newTotal = newItems.Sum(i => i.Price * i.Quantity);

        return current with
        {
            Items = newItems,
            TotalAmount = newTotal
        };
    }
}
