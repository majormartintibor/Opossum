using Opossum.Core;
using Opossum.Storage.FileSystem;

namespace Opossum.UnitTests.Storage.FileSystem;

public class IndexManagerTests : IDisposable
{
    private readonly IndexManager _manager;
    private readonly string _tempContextPath;

    public IndexManagerTests()
    {
        _manager = new IndexManager();
        _tempContextPath = Path.Combine(Path.GetTempPath(), $"IndexManagerTests_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempContextPath))
        {
            Directory.Delete(_tempContextPath, recursive: true);
        }
    }

    // ========================================================================
    // AddEventToIndicesAsync Tests
    // ========================================================================

    [Fact]
    public async Task AddEventToIndicesAsync_AddsToEventTypeIndexAsync()
    {
        // Arrange
        var sequencedEvent = CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "test" });

        // Act
        await _manager.AddEventToIndicesAsync(_tempContextPath, sequencedEvent);

        // Assert
        Assert.True(_manager.EventTypeIndexExists(_tempContextPath, "TestEvent"));
        var positions = await _manager.GetPositionsByEventTypeAsync(_tempContextPath, "TestEvent");
        Assert.Single(positions);
        Assert.Equal(1, positions[0]);
    }

    [Fact]
    public async Task AddEventToIndicesAsync_AddsToTagIndicesAsync()
    {
        // Arrange
        var sequencedEvent = CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "test" },
            new Tag("Environment", "Production"), new Tag("Region", "US-West"));

        // Act
        await _manager.AddEventToIndicesAsync(_tempContextPath, sequencedEvent);

        // Assert
        var tag1 = new Tag("Environment", "Production");
        var tag2 = new Tag("Region", "US-West");

        Assert.True(_manager.TagIndexExists(_tempContextPath, tag1));
        Assert.True(_manager.TagIndexExists(_tempContextPath, tag2));

        var positions1 = await _manager.GetPositionsByTagAsync(_tempContextPath, tag1);
        var positions2 = await _manager.GetPositionsByTagAsync(_tempContextPath, tag2);

        Assert.Single(positions1);
        Assert.Single(positions2);
        Assert.Equal(1, positions1[0]);
        Assert.Equal(1, positions2[0]);
    }

    [Fact]
    public async Task AddEventToIndicesAsync_WithNoTags_OnlyAddsToEventTypeIndexAsync()
    {
        // Arrange
        var sequencedEvent = CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "test" });

        // Act
        await _manager.AddEventToIndicesAsync(_tempContextPath, sequencedEvent);

        // Assert
        Assert.True(_manager.EventTypeIndexExists(_tempContextPath, "TestEvent"));
    }

    [Fact]
    public async Task AddEventToIndicesAsync_WithNullContextPath_ThrowsArgumentNullExceptionAsync()
    {
        // Arrange
        var sequencedEvent = CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "test" });

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.AddEventToIndicesAsync(null!, sequencedEvent));
    }

    [Fact]
    public async Task AddEventToIndicesAsync_WithNullSequencedEvent_ThrowsArgumentNullExceptionAsync()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.AddEventToIndicesAsync(_tempContextPath, null!));
    }

    // ========================================================================
    // GetPositionsByEventTypeAsync Tests
    // ========================================================================

    [Fact]
    public async Task GetPositionsByEventTypeAsync_ReturnsCorrectPositionsAsync()
    {
        // Arrange
        await _manager.AddEventToIndicesAsync(_tempContextPath, CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "1" }));
        await _manager.AddEventToIndicesAsync(_tempContextPath, CreateTestEvent(2, "TestEvent", new TestDomainEvent { Data = "2" }));
        await _manager.AddEventToIndicesAsync(_tempContextPath, CreateTestEvent(3, "OtherEvent", new TestDomainEvent { Data = "3" }));

        // Act
        var positions = await _manager.GetPositionsByEventTypeAsync(_tempContextPath, "TestEvent");

        // Assert
        Assert.Equal(2, positions.Length);
        Assert.Equal([1, 2], positions);
    }

    [Fact]
    public async Task GetPositionsByEventTypeAsync_WithNonExistentType_ReturnsEmptyArrayAsync()
    {
        // Act
        var positions = await _manager.GetPositionsByEventTypeAsync(_tempContextPath, "NonExistent");

        // Assert
        Assert.NotNull(positions);
        Assert.Empty(positions);
    }

    [Fact]
    public async Task GetPositionsByEventTypeAsync_WithNullContextPath_ThrowsArgumentNullExceptionAsync()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.GetPositionsByEventTypeAsync(null!, "TestEvent"));
    }

    [Fact]
    public async Task GetPositionsByEventTypeAsync_WithNullEventType_ThrowsArgumentNullExceptionAsync()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.GetPositionsByEventTypeAsync(_tempContextPath, null!));
    }

    [Fact]
    public async Task GetPositionsByEventTypeAsync_WithEmptyEventType_ThrowsArgumentExceptionAsync()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _manager.GetPositionsByEventTypeAsync(_tempContextPath, ""));
    }

    // ========================================================================
    // GetPositionsByEventTypesAsync Tests
    // ========================================================================

    [Fact]
    public async Task GetPositionsByEventTypesAsync_ReturnsUnionOfPositionsAsync()
    {
        // Arrange
        await _manager.AddEventToIndicesAsync(_tempContextPath, CreateTestEvent(1, "EventA", new TestDomainEvent { Data = "1" }));
        await _manager.AddEventToIndicesAsync(_tempContextPath, CreateTestEvent(2, "EventB", new TestDomainEvent { Data = "2" }));
        await _manager.AddEventToIndicesAsync(_tempContextPath, CreateTestEvent(3, "EventA", new TestDomainEvent { Data = "3" }));
        await _manager.AddEventToIndicesAsync(_tempContextPath, CreateTestEvent(4, "EventC", new TestDomainEvent { Data = "4" }));

        // Act
        var positions = await _manager.GetPositionsByEventTypesAsync(_tempContextPath, ["EventA", "EventB"]);

        // Assert
        Assert.Equal(3, positions.Length);
        Assert.Equal([1, 2, 3], positions); // Should be sorted
    }

    [Fact]
    public async Task GetPositionsByEventTypesAsync_RemovesDuplicatesAsync()
    {
        // Arrange
        await _manager.AddEventToIndicesAsync(_tempContextPath, CreateTestEvent(1, "EventA", new TestDomainEvent { Data = "1" }));

        // Act - Request same event type twice
        var positions = await _manager.GetPositionsByEventTypesAsync(_tempContextPath, ["EventA", "EventA"]);

        // Assert
        Assert.Single(positions);
        Assert.Equal(1, positions[0]);
    }

    [Fact]
    public async Task GetPositionsByEventTypesAsync_WithEmptyArray_ReturnsEmptyArrayAsync()
    {
        // Act
        var positions = await _manager.GetPositionsByEventTypesAsync(_tempContextPath, []);

        // Assert
        Assert.NotNull(positions);
        Assert.Empty(positions);
    }

    [Fact]
    public async Task GetPositionsByEventTypesAsync_ReturnsSortedPositionsAsync()
    {
        // Arrange
        await _manager.AddEventToIndicesAsync(_tempContextPath, CreateTestEvent(5, "EventA", new TestDomainEvent { Data = "5" }));
        await _manager.AddEventToIndicesAsync(_tempContextPath, CreateTestEvent(2, "EventB", new TestDomainEvent { Data = "2" }));
        await _manager.AddEventToIndicesAsync(_tempContextPath, CreateTestEvent(8, "EventA", new TestDomainEvent { Data = "8" }));

        // Act
        var positions = await _manager.GetPositionsByEventTypesAsync(_tempContextPath, ["EventA", "EventB"]);

        // Assert
        Assert.Equal([2, 5, 8], positions);
    }

    [Fact]
    public async Task GetPositionsByEventTypesAsync_WithNullContextPath_ThrowsArgumentNullExceptionAsync()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.GetPositionsByEventTypesAsync(null!, ["EventA"]));
    }

    [Fact]
    public async Task GetPositionsByEventTypesAsync_WithNullArray_ThrowsArgumentNullExceptionAsync()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.GetPositionsByEventTypesAsync(_tempContextPath, null!));
    }

    // ========================================================================
    // GetPositionsByTagAsync Tests
    // ========================================================================

    [Fact]
    public async Task GetPositionsByTagAsync_ReturnsCorrectPositionsAsync()
    {
        // Arrange
        var tag1 = new Tag("Environment", "Production");
        var tag2 = new Tag("Environment", "Development");

        var event1 = CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "1" }, tag1);
        var event2 = CreateTestEvent(2, "TestEvent", new TestDomainEvent { Data = "2" }, tag1);
        var event3 = CreateTestEvent(3, "TestEvent", new TestDomainEvent { Data = "3" }, tag2);

        await _manager.AddEventToIndicesAsync(_tempContextPath, event1);
        await _manager.AddEventToIndicesAsync(_tempContextPath, event2);
        await _manager.AddEventToIndicesAsync(_tempContextPath, event3);

        // Act
        var positions = await _manager.GetPositionsByTagAsync(_tempContextPath, tag1);

        // Assert
        Assert.Equal(2, positions.Length);
        Assert.Equal([1, 2], positions);
    }

    [Fact]
    public async Task GetPositionsByTagAsync_WithNonExistentTag_ReturnsEmptyArrayAsync()
    {
        // Arrange
        var tag = new Tag("NonExistent", "Value");

        // Act
        var positions = await _manager.GetPositionsByTagAsync(_tempContextPath, tag);

        // Assert
        Assert.NotNull(positions);
        Assert.Empty(positions);
    }

    [Fact]
    public async Task GetPositionsByTagAsync_WithNullContextPath_ThrowsArgumentNullExceptionAsync()
    {
        // Arrange
        var tag = new Tag("Environment", "Production");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.GetPositionsByTagAsync(null!, tag));
    }

    [Fact]
    public async Task GetPositionsByTagAsync_WithNullTag_ThrowsArgumentNullExceptionAsync()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.GetPositionsByTagAsync(_tempContextPath, null!));
    }

    // ========================================================================
    // GetPositionsByTagsAsync Tests
    // ========================================================================

    [Fact]
    public async Task GetPositionsByTagsAsync_ReturnsUnionOfPositionsAsync()
    {
        // Arrange
        var tag1 = new Tag("Environment", "Production");
        var tag2 = new Tag("Region", "US-West");

        var event1 = CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "1" }, tag1);
        var event2 = CreateTestEvent(2, "TestEvent", new TestDomainEvent { Data = "2" }, tag2);
        var event3 = CreateTestEvent(3, "TestEvent", new TestDomainEvent { Data = "3" }, tag1);

        await _manager.AddEventToIndicesAsync(_tempContextPath, event1);
        await _manager.AddEventToIndicesAsync(_tempContextPath, event2);
        await _manager.AddEventToIndicesAsync(_tempContextPath, event3);

        // Act
        var positions = await _manager.GetPositionsByTagsAsync(_tempContextPath, [tag1, tag2]);

        // Assert
        Assert.Equal(3, positions.Length);
        Assert.Equal([1, 2, 3], positions);
    }

    [Fact]
    public async Task GetPositionsByTagsAsync_RemovesDuplicatesAsync()
    {
        // Arrange
        var tag = new Tag("Environment", "Production");

        var event1 = CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "1" }, tag);

        await _manager.AddEventToIndicesAsync(_tempContextPath, event1);

        // Act - Request same tag twice
        var positions = await _manager.GetPositionsByTagsAsync(_tempContextPath, [tag, tag]);

        // Assert
        Assert.Single(positions);
        Assert.Equal(1, positions[0]);
    }

    [Fact]
    public async Task GetPositionsByTagsAsync_WithEmptyArray_ReturnsEmptyArrayAsync()
    {
        // Act
        var positions = await _manager.GetPositionsByTagsAsync(_tempContextPath, []);

        // Assert
        Assert.NotNull(positions);
        Assert.Empty(positions);
    }

    [Fact]
    public async Task GetPositionsByTagsAsync_ReturnsSortedPositionsAsync()
    {
        // Arrange
        var tag1 = new Tag("Environment", "Production");
        var tag2 = new Tag("Region", "US-West");

        var event1 = CreateTestEvent(5, "TestEvent", new TestDomainEvent { Data = "5" }, tag1);
        var event2 = CreateTestEvent(2, "TestEvent", new TestDomainEvent { Data = "2" }, tag2);
        var event3 = CreateTestEvent(8, "TestEvent", new TestDomainEvent { Data = "8" }, tag1);

        await _manager.AddEventToIndicesAsync(_tempContextPath, event1);
        await _manager.AddEventToIndicesAsync(_tempContextPath, event2);
        await _manager.AddEventToIndicesAsync(_tempContextPath, event3);

        // Act
        var positions = await _manager.GetPositionsByTagsAsync(_tempContextPath, [tag1, tag2]);

        // Assert
        Assert.Equal([2, 5, 8], positions);
    }

    [Fact]
    public async Task GetPositionsByTagsAsync_WithNullContextPath_ThrowsArgumentNullExceptionAsync()
    {
        // Arrange
        var tag = new Tag("Environment", "Production");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.GetPositionsByTagsAsync(null!, [tag]));
    }

    [Fact]
    public async Task GetPositionsByTagsAsync_WithNullArray_ThrowsArgumentNullExceptionAsync()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.GetPositionsByTagsAsync(_tempContextPath, null!));
    }

    // ========================================================================
    // EventTypeIndexExists Tests
    // ========================================================================

    [Fact]
    public async Task EventTypeIndexExists_ReturnsTrueForExistingIndexAsync()
    {
        // Arrange
        await _manager.AddEventToIndicesAsync(_tempContextPath, CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "1" }));

        // Act
        var exists = _manager.EventTypeIndexExists(_tempContextPath, "TestEvent");

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public void EventTypeIndexExists_ReturnsFalseForNonExistentIndex()
    {
        // Act
        var exists = _manager.EventTypeIndexExists(_tempContextPath, "NonExistent");

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public void EventTypeIndexExists_WithNullContextPath_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _manager.EventTypeIndexExists(null!, "TestEvent"));
    }

    [Fact]
    public void EventTypeIndexExists_WithNullEventType_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _manager.EventTypeIndexExists(_tempContextPath, null!));
    }

    // ========================================================================
    // TagIndexExists Tests
    // ========================================================================

    [Fact]
    public async Task TagIndexExists_ReturnsTrueForExistingIndexAsync()
    {
        // Arrange
        var tag = new Tag("Environment", "Production");
        var event1 = CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "1" }, tag);
        await _manager.AddEventToIndicesAsync(_tempContextPath, event1);

        // Act
        var exists = _manager.TagIndexExists(_tempContextPath, tag);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public void TagIndexExists_ReturnsFalseForNonExistentIndex()
    {
        // Arrange
        var tag = new Tag("NonExistent", "Value");

        // Act
        var exists = _manager.TagIndexExists(_tempContextPath, tag);

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public void TagIndexExists_WithNullContextPath_ThrowsArgumentNullException()
    {
        // Arrange
        var tag = new Tag("Environment", "Production");

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _manager.TagIndexExists(null!, tag));
    }

    [Fact]
    public void TagIndexExists_WithNullTag_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _manager.TagIndexExists(_tempContextPath, null!));
    }

    // ========================================================================
    // Integration Tests
    // ========================================================================

    [Fact]
    public async Task Integration_ComplexScenario_AllIndicesWorkAsync()
    {
        // Arrange - Create events with various types and tags
        var event1 = CreateTestEvent(1, "OrderCreated", new TestDomainEvent { Data = "1" },
            new Tag("Environment", "Production"), new Tag("Region", "US-West"));
        var event2 = CreateTestEvent(2, "OrderShipped", new TestDomainEvent { Data = "2" },
            new Tag("Environment", "Production"));
        var event3 = CreateTestEvent(3, "OrderCreated", new TestDomainEvent { Data = "3" },
            new Tag("Environment", "Development"));

        // Act
        await _manager.AddEventToIndicesAsync(_tempContextPath, event1);
        await _manager.AddEventToIndicesAsync(_tempContextPath, event2);
        await _manager.AddEventToIndicesAsync(_tempContextPath, event3);

        // Assert - EventType queries
        var orderCreated = await _manager.GetPositionsByEventTypeAsync(_tempContextPath, "OrderCreated");
        var orderShipped = await _manager.GetPositionsByEventTypeAsync(_tempContextPath, "OrderShipped");
        Assert.Equal([1, 3], orderCreated);
        Assert.Equal([2], orderShipped);

        // Assert - Tag queries
        var prodTag = new Tag("Environment", "Production");
        var devTag = new Tag("Environment", "Development");
        var regionTag = new Tag("Region", "US-West");

        var prodPositions = await _manager.GetPositionsByTagAsync(_tempContextPath, prodTag);
        var devPositions = await _manager.GetPositionsByTagAsync(_tempContextPath, devTag);
        var regionPositions = await _manager.GetPositionsByTagAsync(_tempContextPath, regionTag);

        Assert.Equal([1, 2], prodPositions);
        Assert.Equal([3], devPositions);
        Assert.Equal([1], regionPositions);

        // Assert - Multi-type query
        var allOrders = await _manager.GetPositionsByEventTypesAsync(_tempContextPath, ["OrderCreated", "OrderShipped"]);
        Assert.Equal([1, 2, 3], allOrders);

        // Assert - Multi-tag query
        var allEnvs = await _manager.GetPositionsByTagsAsync(_tempContextPath, [prodTag, devTag]);
        Assert.Equal([1, 2, 3], allEnvs);
    }

    // ========================================================================
    // Helper Methods
    // ========================================================================

    private static SequencedEvent CreateTestEvent(long position, string eventType, IEvent domainEvent, params Tag[] tags)
    {
        return new SequencedEvent
        {
            Position = position,
            Event = new DomainEvent
            {
                EventType = eventType,
                Event = domainEvent,
                Tags = [..tags]
            },
            Metadata = new Metadata()
        };
    }
}
