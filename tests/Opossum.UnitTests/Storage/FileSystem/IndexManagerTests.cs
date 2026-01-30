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
    public async Task AddEventToIndicesAsync_AddsToEventTypeIndex()
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
    public async Task AddEventToIndicesAsync_AddsToTagIndices()
    {
        // Arrange
        var sequencedEvent = CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "test" });
        sequencedEvent.Event.Tags.Add(new Tag { Key = "Environment", Value = "Production" });
        sequencedEvent.Event.Tags.Add(new Tag { Key = "Region", Value = "US-West" });

        // Act
        await _manager.AddEventToIndicesAsync(_tempContextPath, sequencedEvent);

        // Assert
        var tag1 = new Tag { Key = "Environment", Value = "Production" };
        var tag2 = new Tag { Key = "Region", Value = "US-West" };
        
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
    public async Task AddEventToIndicesAsync_WithNoTags_OnlyAddsToEventTypeIndex()
    {
        // Arrange
        var sequencedEvent = CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "test" });
        sequencedEvent.Event.Tags.Clear();

        // Act
        await _manager.AddEventToIndicesAsync(_tempContextPath, sequencedEvent);

        // Assert
        Assert.True(_manager.EventTypeIndexExists(_tempContextPath, "TestEvent"));
    }

    [Fact]
    public async Task AddEventToIndicesAsync_WithNullContextPath_ThrowsArgumentNullException()
    {
        // Arrange
        var sequencedEvent = CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "test" });

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.AddEventToIndicesAsync(null!, sequencedEvent));
    }

    [Fact]
    public async Task AddEventToIndicesAsync_WithNullSequencedEvent_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.AddEventToIndicesAsync(_tempContextPath, null!));
    }

    // ========================================================================
    // GetPositionsByEventTypeAsync Tests
    // ========================================================================

    [Fact]
    public async Task GetPositionsByEventTypeAsync_ReturnsCorrectPositions()
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
    public async Task GetPositionsByEventTypeAsync_WithNonExistentType_ReturnsEmptyArray()
    {
        // Act
        var positions = await _manager.GetPositionsByEventTypeAsync(_tempContextPath, "NonExistent");

        // Assert
        Assert.NotNull(positions);
        Assert.Empty(positions);
    }

    [Fact]
    public async Task GetPositionsByEventTypeAsync_WithNullContextPath_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.GetPositionsByEventTypeAsync(null!, "TestEvent"));
    }

    [Fact]
    public async Task GetPositionsByEventTypeAsync_WithNullEventType_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.GetPositionsByEventTypeAsync(_tempContextPath, null!));
    }

    [Fact]
    public async Task GetPositionsByEventTypeAsync_WithEmptyEventType_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _manager.GetPositionsByEventTypeAsync(_tempContextPath, ""));
    }

    // ========================================================================
    // GetPositionsByEventTypesAsync Tests
    // ========================================================================

    [Fact]
    public async Task GetPositionsByEventTypesAsync_ReturnsUnionOfPositions()
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
    public async Task GetPositionsByEventTypesAsync_RemovesDuplicates()
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
    public async Task GetPositionsByEventTypesAsync_WithEmptyArray_ReturnsEmptyArray()
    {
        // Act
        var positions = await _manager.GetPositionsByEventTypesAsync(_tempContextPath, []);

        // Assert
        Assert.NotNull(positions);
        Assert.Empty(positions);
    }

    [Fact]
    public async Task GetPositionsByEventTypesAsync_ReturnsSortedPositions()
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
    public async Task GetPositionsByEventTypesAsync_WithNullContextPath_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.GetPositionsByEventTypesAsync(null!, ["EventA"]));
    }

    [Fact]
    public async Task GetPositionsByEventTypesAsync_WithNullArray_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.GetPositionsByEventTypesAsync(_tempContextPath, null!));
    }

    // ========================================================================
    // GetPositionsByTagAsync Tests
    // ========================================================================

    [Fact]
    public async Task GetPositionsByTagAsync_ReturnsCorrectPositions()
    {
        // Arrange
        var tag1 = new Tag { Key = "Environment", Value = "Production" };
        var tag2 = new Tag { Key = "Environment", Value = "Development" };
        
        var event1 = CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "1" });
        event1.Event.Tags.Add(tag1);
        
        var event2 = CreateTestEvent(2, "TestEvent", new TestDomainEvent { Data = "2" });
        event2.Event.Tags.Add(tag1);
        
        var event3 = CreateTestEvent(3, "TestEvent", new TestDomainEvent { Data = "3" });
        event3.Event.Tags.Add(tag2);

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
    public async Task GetPositionsByTagAsync_WithNonExistentTag_ReturnsEmptyArray()
    {
        // Arrange
        var tag = new Tag { Key = "NonExistent", Value = "Value" };

        // Act
        var positions = await _manager.GetPositionsByTagAsync(_tempContextPath, tag);

        // Assert
        Assert.NotNull(positions);
        Assert.Empty(positions);
    }

    [Fact]
    public async Task GetPositionsByTagAsync_WithNullContextPath_ThrowsArgumentNullException()
    {
        // Arrange
        var tag = new Tag { Key = "Environment", Value = "Production" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.GetPositionsByTagAsync(null!, tag));
    }

    [Fact]
    public async Task GetPositionsByTagAsync_WithNullTag_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.GetPositionsByTagAsync(_tempContextPath, null!));
    }

    // ========================================================================
    // GetPositionsByTagsAsync Tests
    // ========================================================================

    [Fact]
    public async Task GetPositionsByTagsAsync_ReturnsUnionOfPositions()
    {
        // Arrange
        var tag1 = new Tag { Key = "Environment", Value = "Production" };
        var tag2 = new Tag { Key = "Region", Value = "US-West" };
        
        var event1 = CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "1" });
        event1.Event.Tags.Add(tag1);
        
        var event2 = CreateTestEvent(2, "TestEvent", new TestDomainEvent { Data = "2" });
        event2.Event.Tags.Add(tag2);
        
        var event3 = CreateTestEvent(3, "TestEvent", new TestDomainEvent { Data = "3" });
        event3.Event.Tags.Add(tag1);

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
    public async Task GetPositionsByTagsAsync_RemovesDuplicates()
    {
        // Arrange
        var tag = new Tag { Key = "Environment", Value = "Production" };
        
        var event1 = CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "1" });
        event1.Event.Tags.Add(tag);

        await _manager.AddEventToIndicesAsync(_tempContextPath, event1);

        // Act - Request same tag twice
        var positions = await _manager.GetPositionsByTagsAsync(_tempContextPath, [tag, tag]);

        // Assert
        Assert.Single(positions);
        Assert.Equal(1, positions[0]);
    }

    [Fact]
    public async Task GetPositionsByTagsAsync_WithEmptyArray_ReturnsEmptyArray()
    {
        // Act
        var positions = await _manager.GetPositionsByTagsAsync(_tempContextPath, []);

        // Assert
        Assert.NotNull(positions);
        Assert.Empty(positions);
    }

    [Fact]
    public async Task GetPositionsByTagsAsync_ReturnsSortedPositions()
    {
        // Arrange
        var tag1 = new Tag { Key = "Environment", Value = "Production" };
        var tag2 = new Tag { Key = "Region", Value = "US-West" };
        
        var event1 = CreateTestEvent(5, "TestEvent", new TestDomainEvent { Data = "5" });
        event1.Event.Tags.Add(tag1);
        
        var event2 = CreateTestEvent(2, "TestEvent", new TestDomainEvent { Data = "2" });
        event2.Event.Tags.Add(tag2);
        
        var event3 = CreateTestEvent(8, "TestEvent", new TestDomainEvent { Data = "8" });
        event3.Event.Tags.Add(tag1);

        await _manager.AddEventToIndicesAsync(_tempContextPath, event1);
        await _manager.AddEventToIndicesAsync(_tempContextPath, event2);
        await _manager.AddEventToIndicesAsync(_tempContextPath, event3);

        // Act
        var positions = await _manager.GetPositionsByTagsAsync(_tempContextPath, [tag1, tag2]);

        // Assert
        Assert.Equal([2, 5, 8], positions);
    }

    [Fact]
    public async Task GetPositionsByTagsAsync_WithNullContextPath_ThrowsArgumentNullException()
    {
        // Arrange
        var tag = new Tag { Key = "Environment", Value = "Production" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.GetPositionsByTagsAsync(null!, [tag]));
    }

    [Fact]
    public async Task GetPositionsByTagsAsync_WithNullArray_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.GetPositionsByTagsAsync(_tempContextPath, null!));
    }

    // ========================================================================
    // EventTypeIndexExists Tests
    // ========================================================================

    [Fact]
    public async Task EventTypeIndexExists_ReturnsTrueForExistingIndex()
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
    public async Task TagIndexExists_ReturnsTrueForExistingIndex()
    {
        // Arrange
        var tag = new Tag { Key = "Environment", Value = "Production" };
        var event1 = CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "1" });
        event1.Event.Tags.Add(tag);
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
        var tag = new Tag { Key = "NonExistent", Value = "Value" };

        // Act
        var exists = _manager.TagIndexExists(_tempContextPath, tag);

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public void TagIndexExists_WithNullContextPath_ThrowsArgumentNullException()
    {
        // Arrange
        var tag = new Tag { Key = "Environment", Value = "Production" };

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
    public async Task Integration_ComplexScenario_AllIndicesWork()
    {
        // Arrange - Create events with various types and tags
        var event1 = CreateTestEvent(1, "OrderCreated", new TestDomainEvent { Data = "1" });
        event1.Event.Tags.Add(new Tag { Key = "Environment", Value = "Production" });
        event1.Event.Tags.Add(new Tag { Key = "Region", Value = "US-West" });

        var event2 = CreateTestEvent(2, "OrderShipped", new TestDomainEvent { Data = "2" });
        event2.Event.Tags.Add(new Tag { Key = "Environment", Value = "Production" });

        var event3 = CreateTestEvent(3, "OrderCreated", new TestDomainEvent { Data = "3" });
        event3.Event.Tags.Add(new Tag { Key = "Environment", Value = "Development" });

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
        var prodTag = new Tag { Key = "Environment", Value = "Production" };
        var devTag = new Tag { Key = "Environment", Value = "Development" };
        var regionTag = new Tag { Key = "Region", Value = "US-West" };
        
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

    private static SequencedEvent CreateTestEvent(long position, string eventType, IEvent domainEvent)
    {
        return new SequencedEvent
        {
            Position = position,
            Event = new DomainEvent
            {
                EventType = eventType,
                Event = domainEvent,
                Tags = []
            },
            Metadata = new Metadata()
        };
    }
}
