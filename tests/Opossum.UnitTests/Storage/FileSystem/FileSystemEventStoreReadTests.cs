using Opossum.Core;
using Opossum.Configuration;
using Opossum.Storage.FileSystem;
using Opossum.UnitTests.Helpers;

namespace Opossum.UnitTests.Storage.FileSystem;

public class FileSystemEventStoreReadTests : IDisposable
{
    private readonly OpossumOptions _options;
    private readonly FileSystemEventStore _store;
    private readonly string _tempRootPath;

    public FileSystemEventStoreReadTests()
    {
        _tempRootPath = Path.Combine(Path.GetTempPath(), $"FileSystemEventStoreReadTests_{Guid.NewGuid():N}");
        _options = new OpossumOptions
        {
            RootPath = _tempRootPath
        };
        _options.UseStore("TestContext");

        _store = new FileSystemEventStore(_options);
    }

    public void Dispose()
    {
        TestDirectoryHelper.ForceDelete(_tempRootPath);
    }

    // ========================================================================
    // ReadAsync - Basic Tests
    // ========================================================================

    [Fact]
    public async Task ReadAsync_WithQueryAll_ReturnsAllEventsAsync()
    {
        // Arrange - Append 3 events
        await AppendTestEventsAsync(3);

        // Act
        var events = await _store.ReadAsync(Query.All(), null);

        // Assert
        Assert.Equal(3, events.Length);
        Assert.Equal(1, events[0].Position);
        Assert.Equal(2, events[1].Position);
        Assert.Equal(3, events[2].Position);
    }

    [Fact]
    public async Task ReadAsync_WithQueryAll_ReturnsInAscendingOrderAsync()
    {
        // Arrange
        await AppendTestEventsAsync(5);

        // Act
        var events = await _store.ReadAsync(Query.All(), null);

        // Assert
        for (int i = 0; i < events.Length - 1; i++)
        {
            Assert.True(events[i].Position < events[i + 1].Position);
        }
    }

    [Fact]
    public async Task ReadAsync_WithNoEvents_ReturnsEmptyArrayAsync()
    {
        // Act
        var events = await _store.ReadAsync(Query.All(), null);

        // Assert
        Assert.NotNull(events);
        Assert.Empty(events);
    }

    [Fact]
    public async Task ReadAsync_WithNullQuery_ThrowsArgumentNullExceptionAsync()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _store.ReadAsync(null!, null));
    }

    [Fact]
    public async Task ReadAsync_WithNoContextsConfigured_ThrowsInvalidOperationExceptionAsync()
    {
        // Arrange
        var optionsNoContext = new OpossumOptions { RootPath = _tempRootPath };
        var storeNoContext = new FileSystemEventStore(optionsNoContext);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => storeNoContext.ReadAsync(Query.All(), null));
    }

    // ========================================================================
    // ReadAsync - EventType Filtering Tests
    // ========================================================================

    [Fact]
    public async Task ReadAsync_WithSingleEventType_ReturnsMatchingEventsAsync()
    {
        // Arrange
        await AppendEventWithTypeAsync("OrderCreated");
        await AppendEventWithTypeAsync("OrderShipped");
        await AppendEventWithTypeAsync("OrderCreated");

        // Act
        var events = await _store.ReadAsync(Query.FromEventTypes("OrderCreated"), null);

        // Assert
        Assert.Equal(2, events.Length);
        Assert.Equal("OrderCreated", events[0].Event.EventType);
        Assert.Equal("OrderCreated", events[1].Event.EventType);
        Assert.Equal(1, events[0].Position);
        Assert.Equal(3, events[1].Position);
    }

    [Fact]
    public async Task ReadAsync_WithMultipleEventTypes_ReturnsUnionOfMatchesAsync()
    {
        // Arrange
        await AppendEventWithTypeAsync("OrderCreated");
        await AppendEventWithTypeAsync("OrderShipped");
        await AppendEventWithTypeAsync("OrderCompleted");
        await AppendEventWithTypeAsync("OrderCreated");

        // Act
        var events = await _store.ReadAsync(
            Query.FromEventTypes("OrderCreated", "OrderShipped"),
            null);

        // Assert
        Assert.Equal(3, events.Length);
        Assert.Equal(1, events[0].Position);
        Assert.Equal(2, events[1].Position);
        Assert.Equal(4, events[2].Position);
    }

    [Fact]
    public async Task ReadAsync_WithNonExistentEventType_ReturnsEmptyAsync()
    {
        // Arrange
        await AppendEventWithTypeAsync("OrderCreated");

        // Act
        var events = await _store.ReadAsync(Query.FromEventTypes("NonExistent"), null);

        // Assert
        Assert.Empty(events);
    }

    // ========================================================================
    // ReadAsync - Tag Filtering Tests
    // ========================================================================

    [Fact]
    public async Task ReadAsync_WithSingleTag_ReturnsMatchingEventsAsync()
    {
        // Arrange
        var tag = new Tag("Environment", "Production");
        await AppendEventWithTagsAsync(tag);
        await AppendEventWithTagsAsync(new Tag("Environment", "Development"));
        await AppendEventWithTagsAsync(tag);

        // Act
        var events = await _store.ReadAsync(Query.FromTags(tag), null);

        // Assert
        Assert.Equal(2, events.Length);
        Assert.Equal(1, events[0].Position);
        Assert.Equal(3, events[1].Position);
    }

    [Fact]
    public async Task ReadAsync_WithMultipleTags_ReturnsIntersectionOfMatchesAsync()
    {
        // Arrange
        var tag1 = new Tag("Environment", "Production");
        var tag2 = new Tag("Region", "US-West");

        await AppendEventWithTagsAsync(tag1); // Only tag1
        await AppendEventWithTagsAsync(tag1, tag2); // Both tags
        await AppendEventWithTagsAsync(tag2); // Only tag2
        await AppendEventWithTagsAsync(tag1, tag2); // Both tags

        // Act - AND logic: must have BOTH tags
        var events = await _store.ReadAsync(Query.FromTags(tag1, tag2), null);

        // Assert
        Assert.Equal(2, events.Length);
        Assert.Equal(2, events[0].Position);
        Assert.Equal(4, events[1].Position);
    }

    [Fact]
    public async Task ReadAsync_WithNonExistentTag_ReturnsEmptyAsync()
    {
        // Arrange
        await AppendEventWithTagsAsync(new Tag("Environment", "Production"));

        // Act
        var events = await _store.ReadAsync(
            Query.FromTags(new Tag("NonExistent", "Value")),
            null);

        // Assert
        Assert.Empty(events);
    }

    // ========================================================================
    // ReadAsync - Complex Query Tests
    // ========================================================================

    [Fact]
    public async Task ReadAsync_WithEventTypesAndTags_ReturnsIntersectionAsync()
    {
        // Arrange
        var tag = new Tag("Environment", "Production");

        // Event 1: OrderCreated + Production tag
        var event1 = CreateTestEvent("OrderCreated", new TestDomainEvent { Data = "1" });
        event1.Event = event1.Event with { Tags = [tag] };
        await _store.AppendAsync([event1], null);

        // Event 2: OrderShipped + Production tag
        var event2 = CreateTestEvent("OrderShipped", new TestDomainEvent { Data = "2" });
        event2.Event = event2.Event with { Tags = [tag] };
        await _store.AppendAsync([event2], null);

        // Event 3: OrderCreated (no tag)
        await AppendEventWithTypeAsync("OrderCreated");

        // Act - Query for OrderCreated AND Production tag
        var queryItem = new QueryItem
        {
            EventTypes = ["OrderCreated"],
            Tags = [tag]
        };
        var events = await _store.ReadAsync(Query.FromItems(queryItem), null);

        // Assert - Only Event 1 matches
        Assert.Single(events);
        Assert.Equal(1, events[0].Position);
        Assert.Equal("OrderCreated", events[0].Event.EventType);
    }

    [Fact]
    public async Task ReadAsync_WithMultipleQueryItems_ReturnsUnionAsync()
    {
        // Arrange
        var prodTag = new Tag("Environment", "Production");
        var devTag = new Tag("Environment", "Development");

        await AppendEventWithTypeAsync("OrderCreated"); // Pos 1
        await AppendEventWithTagsAsync(prodTag); // Pos 2
        await AppendEventWithTagsAsync(devTag); // Pos 3
        await AppendEventWithTypeAsync("OrderShipped"); // Pos 4

        // Act - Query for: OrderCreated OR Production tag OR Development tag
        var queryItem1 = new QueryItem { EventTypes = ["OrderCreated"] };
        var queryItem2 = new QueryItem { Tags = [prodTag] };
        var queryItem3 = new QueryItem { Tags = [devTag] };

        var events = await _store.ReadAsync(
            Query.FromItems(queryItem1, queryItem2, queryItem3),
            null);

        // Assert - Positions 1, 2, 3 match (not 4)
        Assert.Equal(3, events.Length);
        Assert.Equal(1, events[0].Position);
        Assert.Equal(2, events[1].Position);
        Assert.Equal(3, events[2].Position);
    }

    [Fact]
    public async Task ReadAsync_WithComplexQuery_ReturnsCorrectMatchesAsync()
    {
        // Arrange - Create diverse events
        var prodTag = new Tag("Environment", "Production");
        var usWestTag = new Tag("Region", "US-West");

        // Event 1: OrderCreated + Production
        var event1 = CreateTestEvent("OrderCreated", new TestDomainEvent { Data = "1" });
        event1.Event = event1.Event with { Tags = [prodTag] };
        await _store.AppendAsync([event1], null);

        // Event 2: OrderShipped + Production + US-West
        var event2 = CreateTestEvent("OrderShipped", new TestDomainEvent { Data = "2" });
        event2.Event = event2.Event with { Tags = [prodTag, usWestTag] };
        await _store.AppendAsync([event2], null);

        // Event 3: CustomerRegistered + Production
        var event3 = CreateTestEvent("CustomerRegistered", new TestDomainEvent { Data = "3" });
        event3.Event = event3.Event with { Tags = [prodTag] };
        await _store.AppendAsync([event3], null);

        // Event 4: OrderCreated (no tags)
        await AppendEventWithTypeAsync("OrderCreated");

        // Act - Query: (OrderCreated OR OrderShipped) AND Production
        var queryItem = new QueryItem
        {
            EventTypes = ["OrderCreated", "OrderShipped"],
            Tags = [prodTag]
        };
        var events = await _store.ReadAsync(Query.FromItems(queryItem), null);

        // Assert - Events 1 and 2 match
        Assert.Equal(2, events.Length);
        Assert.Equal(1, events[0].Position);
        Assert.Equal(2, events[1].Position);
    }

    // ========================================================================
    // ReadAsync - ReadOption Tests
    // ========================================================================

    [Fact]
    public async Task ReadAsync_WithDescendingOption_ReturnsReversedOrderAsync()
    {
        // Arrange
        await AppendTestEventsAsync(5);

        // Act
        var events = await _store.ReadAsync(Query.All(), [ReadOption.Descending]);

        // Assert
        Assert.Equal(5, events.Length);
        Assert.Equal(5, events[0].Position);
        Assert.Equal(4, events[1].Position);
        Assert.Equal(3, events[2].Position);
        Assert.Equal(2, events[3].Position);
        Assert.Equal(1, events[4].Position);
    }

    [Fact]
    public async Task ReadAsync_WithDescendingAndFilter_ReturnsFilteredDescendingAsync()
    {
        // Arrange
        await AppendEventWithTypeAsync("OrderCreated"); // Pos 1
        await AppendEventWithTypeAsync("OrderShipped"); // Pos 2
        await AppendEventWithTypeAsync("OrderCreated"); // Pos 3
        await AppendEventWithTypeAsync("OrderShipped"); // Pos 4
        await AppendEventWithTypeAsync("OrderCreated"); // Pos 5

        // Act
        var events = await _store.ReadAsync(
            Query.FromEventTypes("OrderCreated"),
            [ReadOption.Descending]);

        // Assert
        Assert.Equal(3, events.Length);
        Assert.Equal(5, events[0].Position);
        Assert.Equal(3, events[1].Position);
        Assert.Equal(1, events[2].Position);
    }

    [Fact]
    public async Task ReadAsync_WithNullOptions_ReturnsAscendingAsync()
    {
        // Arrange
        await AppendTestEventsAsync(3);

        // Act
        var events = await _store.ReadAsync(Query.All(), null);

        // Assert
        Assert.Equal(1, events[0].Position);
        Assert.Equal(2, events[1].Position);
        Assert.Equal(3, events[2].Position);
    }

    [Fact]
    public async Task ReadAsync_WithEmptyOptions_ReturnsAscendingAsync()
    {
        // Arrange
        await AppendTestEventsAsync(3);

        // Act
        var events = await _store.ReadAsync(Query.All(), []);

        // Assert
        Assert.Equal(1, events[0].Position);
        Assert.Equal(2, events[1].Position);
        Assert.Equal(3, events[2].Position);
    }

    // ========================================================================
    // ReadAsync - Data Preservation Tests
    // ========================================================================

    [Fact]
    public async Task ReadAsync_PreservesEventDataAsync()
    {
        // Arrange
        var originalEvent = CreateTestEvent("TestEvent", new TestDomainEvent { Data = "Important Data" });
        originalEvent.Event = originalEvent.Event with { Tags = [new Tag("Key1", "Value1")] };
        await _store.AppendAsync([originalEvent], null);

        // Act
        var events = await _store.ReadAsync(Query.All(), null);

        // Assert
        Assert.Single(events);
        Assert.Equal("TestEvent", events[0].Event.EventType);
        var eventData = (TestDomainEvent)events[0].Event.Event;
        Assert.Equal("Important Data", eventData.Data);
        Assert.Single(events[0].Event.Tags);
        Assert.Equal("Key1", events[0].Event.Tags[0].Key);
        Assert.Equal("Value1", events[0].Event.Tags[0].Value);
    }

    [Fact]
    public async Task ReadAsync_PreservesMetadataAsync()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var originalEvent = CreateTestEvent("TestEvent", new TestDomainEvent { Data = "test" });
        originalEvent.Metadata = new Metadata { CorrelationId = correlationId, Timestamp = timestamp };

        await _store.AppendAsync([originalEvent], null);

        // Act
        var events = await _store.ReadAsync(Query.All(), null);

        // Assert
        Assert.Single(events);
        Assert.Equal(correlationId, events[0].Metadata.CorrelationId);
        Assert.Equal(timestamp, events[0].Metadata.Timestamp);
    }

    // ========================================================================
    // Integration Tests
    // ========================================================================

    [Fact]
    public async Task Integration_AppendAndRead_CompleteWorkflowAsync()
    {
        // Arrange - Create diverse events
        var prodTag = new Tag("Environment", "Production");
        var devTag = new Tag("Environment", "Development");
        var usWestTag = new Tag("Region", "US-West");

        var event1 = CreateTestEvent("OrderCreated", new TestDomainEvent { Data = "Order1" });
        event1.Event = event1.Event with { Tags = [prodTag, usWestTag] };

        var event2 = CreateTestEvent("OrderShipped", new TestDomainEvent { Data = "Order1" });
        event2.Event = event2.Event with { Tags = [prodTag] };

        var event3 = CreateTestEvent("OrderCreated", new TestDomainEvent { Data = "Order2" });
        event3.Event = event3.Event with { Tags = [devTag] };

        var event4 = CreateTestEvent("CustomerRegistered", new TestDomainEvent { Data = "Customer1" });
        event4.Event = event4.Event with { Tags = [prodTag] };

        // Act - Append all events
        await _store.AppendAsync([event1, event2, event3, event4], null);

        // Assert - Query all
        var allEvents = await _store.ReadAsync(Query.All(), null);
        Assert.Equal(4, allEvents.Length);

        // Assert - Query by EventType
        var orderEvents = await _store.ReadAsync(Query.FromEventTypes("OrderCreated"), null);
        Assert.Equal(2, orderEvents.Length);

        // Assert - Query by Tag
        var prodEvents = await _store.ReadAsync(Query.FromTags(prodTag), null);
        Assert.Equal(3, prodEvents.Length);

        // Assert - Complex query: OrderCreated AND Production
        var queryItem = new QueryItem
        {
            EventTypes = ["OrderCreated"],
            Tags = [prodTag]
        };
        var complexResult = await _store.ReadAsync(Query.FromItems(queryItem), null);
        Assert.Single(complexResult);
        Assert.Equal(1, complexResult[0].Position);

        // Assert - Descending order
        var descEvents = await _store.ReadAsync(Query.All(), [ReadOption.Descending]);
        Assert.Equal(4, descEvents[0].Position);
        Assert.Equal(1, descEvents[3].Position);
    }

    // ========================================================================
    // ReadAsync - fromPosition Tests
    // ========================================================================

    [Fact]
    public async Task ReadAsync_WithFromPositionNull_ReturnsAllEventsAsync()
    {
        // Arrange
        await AppendTestEventsAsync(5);

        // Act
        var events = await _store.ReadAsync(Query.All(), null, fromPosition: null);

        // Assert
        Assert.Equal(5, events.Length);
        Assert.Equal(1, events[0].Position);
        Assert.Equal(5, events[4].Position);
    }

    [Fact]
    public async Task ReadAsync_WithFromPositionZero_ReturnsAllEventsAsync()
    {
        // Arrange
        await AppendTestEventsAsync(5);

        // Act
        var events = await _store.ReadAsync(Query.All(), null, fromPosition: 0);

        // Assert — position 0 is below position 1, so all events are returned
        Assert.Equal(5, events.Length);
    }

    [Fact]
    public async Task ReadAsync_WithFromPositionInMiddle_ReturnsOnlyEventsAfterPositionAsync()
    {
        // Arrange – append 5 events at positions 1–5
        await AppendTestEventsAsync(5);

        // Act – ask for events after position 3
        var events = await _store.ReadAsync(Query.All(), null, fromPosition: 3);

        // Assert – only positions 4 and 5 returned
        Assert.Equal(2, events.Length);
        Assert.Equal(4, events[0].Position);
        Assert.Equal(5, events[1].Position);
    }

    [Fact]
    public async Task ReadAsync_WithFromPositionAtLastEvent_ReturnsEmptyAsync()
    {
        // Arrange
        await AppendTestEventsAsync(3);

        // Act
        var events = await _store.ReadAsync(Query.All(), null, fromPosition: 3);

        // Assert
        Assert.Empty(events);
    }

    [Fact]
    public async Task ReadAsync_WithFromPositionBeyondLastEvent_ReturnsEmptyAsync()
    {
        // Arrange
        await AppendTestEventsAsync(3);

        // Act
        var events = await _store.ReadAsync(Query.All(), null, fromPosition: 100);

        // Assert
        Assert.Empty(events);
    }

    [Fact]
    public async Task ReadAsync_WithFromPositionAndEventTypeFilter_ReturnsOnlyMatchingEventsAfterPositionAsync()
    {
        // Arrange – positions 1–5: alternating OrderCreated / OrderShipped
        await AppendEventWithTypeAsync("OrderCreated"); // pos 1
        await AppendEventWithTypeAsync("OrderShipped"); // pos 2
        await AppendEventWithTypeAsync("OrderCreated"); // pos 3
        await AppendEventWithTypeAsync("OrderShipped"); // pos 4
        await AppendEventWithTypeAsync("OrderCreated"); // pos 5

        // Act – OrderCreated events after position 2
        var events = await _store.ReadAsync(
            Query.FromEventTypes("OrderCreated"), null, fromPosition: 2);

        // Assert – positions 3 and 5 match
        Assert.Equal(2, events.Length);
        Assert.Equal(3, events[0].Position);
        Assert.Equal(5, events[1].Position);
    }

    [Fact]
    public async Task ReadAsync_WithFromPositionAndTagFilter_ReturnsOnlyMatchingEventsAfterPositionAsync()
    {
        // Arrange
        var tag = new Tag("env", "prod");
        await AppendEventWithTagsAsync(tag);                              // pos 1
        await AppendEventWithTagsAsync(new Tag("env", "dev")); // pos 2
        await AppendEventWithTagsAsync(tag);                              // pos 3
        await AppendEventWithTagsAsync(tag);                              // pos 4

        // Act – prod-tagged events after position 1
        var events = await _store.ReadAsync(Query.FromTags(tag), null, fromPosition: 1);

        // Assert – positions 3 and 4
        Assert.Equal(2, events.Length);
        Assert.Equal(3, events[0].Position);
        Assert.Equal(4, events[1].Position);
    }

    [Fact]
    public async Task ReadAsync_WithFromPositionOnEmptyStore_ReturnsEmptyAsync()
    {
        // Act
        var events = await _store.ReadAsync(Query.All(), null, fromPosition: 0);

        // Assert
        Assert.Empty(events);
    }

    [Fact]
    public async Task ReadAsync_WithFromPositionAndDescendingOption_ReturnsFilteredEventsDescendingAsync()
    {
        // Arrange
        await AppendTestEventsAsync(5);

        // Act – events after position 2, descending
        var events = await _store.ReadAsync(Query.All(), [ReadOption.Descending], fromPosition: 2);

        // Assert – positions 3, 4, 5 in descending order
        Assert.Equal(3, events.Length);
        Assert.Equal(5, events[0].Position);
        Assert.Equal(4, events[1].Position);
        Assert.Equal(3, events[2].Position);
    }

    // ========================================================================
    // Helper Methods
    // ========================================================================

    private async Task AppendTestEventsAsync(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var evt = CreateTestEvent($"Event{i + 1}", new TestDomainEvent { Data = $"Data{i + 1}" });
            await _store.AppendAsync([evt], null);
        }
    }

    private async Task AppendEventWithTypeAsync(string eventType)
    {
        var evt = CreateTestEvent(eventType, new TestDomainEvent { Data = "test" });
        await _store.AppendAsync([evt], null);
    }

    private async Task AppendEventWithTagsAsync(params Tag[] tags)
    {
        var evt = CreateTestEvent("TestEvent", new TestDomainEvent { Data = "test" });
        evt.Event = evt.Event with { Tags = tags };
        await _store.AppendAsync([evt], null);
    }

    private static NewEvent CreateTestEvent(string eventType, IEvent domainEvent)
    {
        return new NewEvent
        {
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
