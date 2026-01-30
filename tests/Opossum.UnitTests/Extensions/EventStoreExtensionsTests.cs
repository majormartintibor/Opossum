using Moq;
using Opossum.Core;
using Opossum.Extensions;

namespace Opossum.UnitTests.Extensions;

/// <summary>
/// Unit tests for EventStoreExtensions
/// </summary>
public class EventStoreExtensionsTests
{
    private readonly Mock<IEventStore> _mockEventStore;

    public EventStoreExtensionsTests()
    {
        _mockEventStore = new Mock<IEventStore>();
    }

    #region AppendAsync - Single Event Tests

    [Fact]
    public async Task AppendAsync_SingleEvent_CallsCoreMethodWithArray()
    {
        // Arrange
        var sequencedEvent = CreateTestEvent(1);
        SequencedEvent[]? capturedEvents = null;

        _mockEventStore
            .Setup(x => x.AppendAsync(It.IsAny<SequencedEvent[]>(), It.IsAny<AppendCondition?>()))
            .Callback<SequencedEvent[], AppendCondition?>((events, _) => capturedEvents = events)
            .Returns(Task.CompletedTask);

        // Act
        await _mockEventStore.Object.AppendAsync(sequencedEvent);

        // Assert
        _mockEventStore.Verify(x => x.AppendAsync(
            It.IsAny<SequencedEvent[]>(),
            It.IsAny<AppendCondition?>()), Times.Once);

        Assert.NotNull(capturedEvents);
        Assert.Single(capturedEvents);
        Assert.Same(sequencedEvent, capturedEvents[0]);
    }

    [Fact]
    public async Task AppendAsync_SingleEvent_PassesNullCondition()
    {
        // Arrange
        var sequencedEvent = CreateTestEvent(1);
        AppendCondition? capturedCondition = null;

        _mockEventStore
            .Setup(x => x.AppendAsync(It.IsAny<SequencedEvent[]>(), It.IsAny<AppendCondition?>()))
            .Callback<SequencedEvent[], AppendCondition?>((_, condition) => capturedCondition = condition)
            .Returns(Task.CompletedTask);

        // Act
        await _mockEventStore.Object.AppendAsync(sequencedEvent);

        // Assert
        Assert.Null(capturedCondition);
    }

    [Fact]
    public async Task AppendAsync_SingleEventWithCondition_PassesCondition()
    {
        // Arrange
        var sequencedEvent = CreateTestEvent(1);
        var appendCondition = new AppendCondition 
        { 
            FailIfEventsMatch = Query.All(),
            AfterSequencePosition = 10 
        };
        AppendCondition? capturedCondition = null;

        _mockEventStore
            .Setup(x => x.AppendAsync(It.IsAny<SequencedEvent[]>(), It.IsAny<AppendCondition?>()))
            .Callback<SequencedEvent[], AppendCondition?>((_, condition) => capturedCondition = condition)
            .Returns(Task.CompletedTask);

        // Act
        await _mockEventStore.Object.AppendAsync(sequencedEvent, appendCondition);

        // Assert
        Assert.Same(appendCondition, capturedCondition);
    }

    [Fact]
    public async Task AppendAsync_SingleEvent_ThrowsIfEventStoreIsNull()
    {
        // Arrange
        IEventStore? nullStore = null;
        var sequencedEvent = CreateTestEvent(1);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await nullStore!.AppendAsync(sequencedEvent));
    }

    [Fact]
    public async Task AppendAsync_SingleEvent_ThrowsIfEventIsNull()
    {
        // Arrange
        SequencedEvent? nullEvent = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _mockEventStore.Object.AppendAsync(nullEvent!));
    }

    #endregion

    #region AppendAsync - Array Without Condition Tests

    [Fact]
    public async Task AppendAsync_ArrayWithoutCondition_CallsCoreMethodWithNullCondition()
    {
        // Arrange
        var events = new[]
        {
            CreateTestEvent(1),
            CreateTestEvent(2),
            CreateTestEvent(3)
        };
        AppendCondition? capturedCondition = null;

        _mockEventStore
            .Setup(x => x.AppendAsync(It.IsAny<SequencedEvent[]>(), It.IsAny<AppendCondition?>()))
            .Callback<SequencedEvent[], AppendCondition?>((_, condition) => capturedCondition = condition)
            .Returns(Task.CompletedTask);

        // Act
        await _mockEventStore.Object.AppendAsync(events);

        // Assert
        Assert.Null(capturedCondition);
        _mockEventStore.Verify(x => x.AppendAsync(events, null), Times.Once);
    }

    [Fact]
    public async Task AppendAsync_ArrayWithoutCondition_ThrowsIfEventStoreIsNull()
    {
        // Arrange
        IEventStore? nullStore = null;
        var events = new[] { CreateTestEvent(1) };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await nullStore!.AppendAsync(events));
    }

    [Fact]
    public async Task AppendAsync_ArrayWithoutCondition_ThrowsIfEventsIsNull()
    {
        // Arrange
        SequencedEvent[]? nullEvents = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _mockEventStore.Object.AppendAsync(nullEvents!));
    }

    #endregion

    #region ReadAsync - Single ReadOption Tests

    [Fact]
    public async Task ReadAsync_SingleReadOption_CallsCoreMethodWithArray()
    {
        // Arrange
        var query = Query.All();
        var readOption = ReadOption.Descending;
        ReadOption[]? capturedOptions = null;

        _mockEventStore
            .Setup(x => x.ReadAsync(It.IsAny<Query>(), It.IsAny<ReadOption[]?>()))
            .Callback<Query, ReadOption[]?>((_, options) => capturedOptions = options)
            .ReturnsAsync([]);

        // Act
        await _mockEventStore.Object.ReadAsync(query, readOption);

        // Assert
        Assert.NotNull(capturedOptions);
        Assert.Single(capturedOptions);
        Assert.Equal(ReadOption.Descending, capturedOptions[0]);
    }

    [Fact]
    public async Task ReadAsync_SingleReadOption_ThrowsIfEventStoreIsNull()
    {
        // Arrange
        IEventStore? nullStore = null;
        var query = Query.All();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await nullStore!.ReadAsync(query, ReadOption.Descending));
    }

    [Fact]
    public async Task ReadAsync_SingleReadOption_ThrowsIfQueryIsNull()
    {
        // Arrange
        Query? nullQuery = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _mockEventStore.Object.ReadAsync(nullQuery!, ReadOption.Descending));
    }

    #endregion

    #region ReadAsync - No Options Tests

    [Fact]
    public async Task ReadAsync_NoOptions_CallsCoreMethodWithNull()
    {
        // Arrange
        var query = Query.All();
        ReadOption[]? capturedOptions = null;

        _mockEventStore
            .Setup(x => x.ReadAsync(It.IsAny<Query>(), It.IsAny<ReadOption[]?>()))
            .Callback<Query, ReadOption[]?>((_, options) => capturedOptions = options)
            .ReturnsAsync([]);

        // Act
        await _mockEventStore.Object.ReadAsync(query);

        // Assert
        Assert.Null(capturedOptions);
        _mockEventStore.Verify(x => x.ReadAsync(query, null), Times.Once);
    }

    [Fact]
    public async Task ReadAsync_NoOptions_ReturnsEventsFromCoreMethod()
    {
        // Arrange
        var query = Query.All();
        var expectedEvents = new[]
        {
            CreateTestEvent(1),
            CreateTestEvent(2)
        };

        _mockEventStore
            .Setup(x => x.ReadAsync(It.IsAny<Query>(), It.IsAny<ReadOption[]?>()))
            .ReturnsAsync(expectedEvents);

        // Act
        var result = await _mockEventStore.Object.ReadAsync(query);

        // Assert
        Assert.Same(expectedEvents, result);
    }

    [Fact]
    public async Task ReadAsync_NoOptions_ThrowsIfEventStoreIsNull()
    {
        // Arrange
        IEventStore? nullStore = null;
        var query = Query.All();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await nullStore!.ReadAsync(query));
    }

    [Fact]
    public async Task ReadAsync_NoOptions_ThrowsIfQueryIsNull()
    {
        // Arrange
        Query? nullQuery = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _mockEventStore.Object.ReadAsync(nullQuery!));
    }

    #endregion

    #region DomainEventBuilder Tests

    [Fact]
    public void ToDomainEvent_CreatesBuilder()
    {
        // Arrange
        var @event = new TestEventData { Id = Guid.NewGuid() };

        // Act
        var builder = @event.ToDomainEvent();

        // Assert
        Assert.NotNull(builder);
    }

    [Fact]
    public void ToDomainEvent_ThrowsIfEventIsNull()
    {
        // Arrange
        IEvent? nullEvent = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => nullEvent!.ToDomainEvent());
    }

    [Fact]
    public void DomainEventBuilder_Build_CreatesSequencedEventWithEventType()
    {
        // Arrange
        var @event = new TestEventData { Id = Guid.NewGuid() };

        // Act
        var sequencedEvent = @event.ToDomainEvent().Build();

        // Assert
        Assert.Equal("TestEventData", sequencedEvent.Event.EventType);
        Assert.Same(@event, sequencedEvent.Event.Event);
    }

    [Fact]
    public void DomainEventBuilder_Build_SetsDefaultMetadata()
    {
        // Arrange
        var @event = new TestEventData { Id = Guid.NewGuid() };

        // Act
        var sequencedEvent = @event.ToDomainEvent().Build();

        // Assert
        Assert.NotNull(sequencedEvent.Metadata);
        Assert.NotNull(sequencedEvent.Metadata.CorrelationId);
        Assert.NotEqual(Guid.Empty, sequencedEvent.Metadata.CorrelationId);
        Assert.True(sequencedEvent.Metadata.Timestamp <= DateTimeOffset.UtcNow);
        Assert.True(sequencedEvent.Metadata.Timestamp >= DateTimeOffset.UtcNow.AddSeconds(-1));
    }

    [Fact]
    public void DomainEventBuilder_WithTag_AddsSingleTag()
    {
        // Arrange
        var @event = new TestEventData { Id = Guid.NewGuid() };

        // Act
        var sequencedEvent = @event.ToDomainEvent()
            .WithTag("key1", "value1")
            .Build();

        // Assert
        Assert.Single(sequencedEvent.Event.Tags);
        Assert.Equal("key1", sequencedEvent.Event.Tags[0].Key);
        Assert.Equal("value1", sequencedEvent.Event.Tags[0].Value);
    }

    [Fact]
    public void DomainEventBuilder_WithTag_AddMultipleTags()
    {
        // Arrange
        var @event = new TestEventData { Id = Guid.NewGuid() };

        // Act
        var sequencedEvent = @event.ToDomainEvent()
            .WithTag("key1", "value1")
            .WithTag("key2", "value2")
            .WithTag("key3", "value3")
            .Build();

        // Assert
        Assert.Equal(3, sequencedEvent.Event.Tags.Count);
        Assert.Equal("key1", sequencedEvent.Event.Tags[0].Key);
        Assert.Equal("key2", sequencedEvent.Event.Tags[1].Key);
        Assert.Equal("key3", sequencedEvent.Event.Tags[2].Key);
    }

    [Fact]
    public void DomainEventBuilder_WithTags_AddsMultipleTagsAtOnce()
    {
        // Arrange
        var @event = new TestEventData { Id = Guid.NewGuid() };
        var tags = new[]
        {
            new Tag { Key = "key1", Value = "value1" },
            new Tag { Key = "key2", Value = "value2" }
        };

        // Act
        var sequencedEvent = @event.ToDomainEvent()
            .WithTags(tags)
            .Build();

        // Assert
        Assert.Equal(2, sequencedEvent.Event.Tags.Count);
        Assert.Same(tags[0], sequencedEvent.Event.Tags[0]);
        Assert.Same(tags[1], sequencedEvent.Event.Tags[1]);
    }

    [Fact]
    public void DomainEventBuilder_WithMetadata_SetsCustomMetadata()
    {
        // Arrange
        var @event = new TestEventData { Id = Guid.NewGuid() };
        var customMetadata = new Metadata
        {
            Timestamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            CorrelationId = Guid.NewGuid(),
            CausationId = Guid.NewGuid(),
            OperationId = Guid.NewGuid(),
            UserId = Guid.NewGuid()
        };

        // Act
        var sequencedEvent = @event.ToDomainEvent()
            .WithMetadata(customMetadata)
            .Build();

        // Assert
        Assert.Same(customMetadata, sequencedEvent.Metadata);
    }

    [Fact]
    public void DomainEventBuilder_WithCorrelationId_SetsCorrelationId()
    {
        // Arrange
        var @event = new TestEventData { Id = Guid.NewGuid() };
        var correlationId = Guid.NewGuid();

        // Act
        var sequencedEvent = @event.ToDomainEvent()
            .WithCorrelationId(correlationId)
            .Build();

        // Assert
        Assert.Equal(correlationId, sequencedEvent.Metadata.CorrelationId);
    }

    [Fact]
    public void DomainEventBuilder_WithCausationId_SetsCausationId()
    {
        // Arrange
        var @event = new TestEventData { Id = Guid.NewGuid() };
        var causationId = Guid.NewGuid();

        // Act
        var sequencedEvent = @event.ToDomainEvent()
            .WithCausationId(causationId)
            .Build();

        // Assert
        Assert.Equal(causationId, sequencedEvent.Metadata.CausationId);
    }

    [Fact]
    public void DomainEventBuilder_WithOperationId_SetsOperationId()
    {
        // Arrange
        var @event = new TestEventData { Id = Guid.NewGuid() };
        var operationId = Guid.NewGuid();

        // Act
        var sequencedEvent = @event.ToDomainEvent()
            .WithOperationId(operationId)
            .Build();

        // Assert
        Assert.Equal(operationId, sequencedEvent.Metadata.OperationId);
    }

    [Fact]
    public void DomainEventBuilder_WithUserId_SetsUserId()
    {
        // Arrange
        var @event = new TestEventData { Id = Guid.NewGuid() };
        var userId = Guid.NewGuid();

        // Act
        var sequencedEvent = @event.ToDomainEvent()
            .WithUserId(userId)
            .Build();

        // Assert
        Assert.Equal(userId, sequencedEvent.Metadata.UserId);
    }

    [Fact]
    public void DomainEventBuilder_WithTimestamp_SetsCustomTimestamp()
    {
        // Arrange
        var @event = new TestEventData { Id = Guid.NewGuid() };
        var customTimestamp = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);

        // Act
        var sequencedEvent = @event.ToDomainEvent()
            .WithTimestamp(customTimestamp)
            .Build();

        // Assert
        Assert.Equal(customTimestamp, sequencedEvent.Metadata.Timestamp);
    }

    [Fact]
    public void DomainEventBuilder_ImplicitConversion_WorksWithoutExplicitBuild()
    {
        // Arrange
        var @event = new TestEventData { Id = Guid.NewGuid() };

        // Act
        SequencedEvent sequencedEvent = @event.ToDomainEvent()
            .WithTag("key1", "value1");

        // Assert
        Assert.NotNull(sequencedEvent);
        Assert.Equal("TestEventData", sequencedEvent.Event.EventType);
        Assert.Single(sequencedEvent.Event.Tags);
    }

    [Fact]
    public void DomainEventBuilder_ImplicitConversion_ThrowsIfBuilderIsNull()
    {
        // Arrange
        DomainEventBuilder? nullBuilder = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
        {
            SequencedEvent _ = nullBuilder!;
        });
    }

    [Fact]
    public void DomainEventBuilder_FluentChaining_WorksCorrectly()
    {
        // Arrange
        var @event = new TestEventData { Id = Guid.NewGuid() };
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

        // Act
        var sequencedEvent = @event.ToDomainEvent()
            .WithTag("studentId", "123")
            .WithTag("courseId", "456")
            .WithCorrelationId(correlationId)
            .WithCausationId(causationId)
            .WithUserId(userId)
            .WithTimestamp(timestamp)
            .Build();

        // Assert
        Assert.Equal(2, sequencedEvent.Event.Tags.Count);
        Assert.Equal(correlationId, sequencedEvent.Metadata.CorrelationId);
        Assert.Equal(causationId, sequencedEvent.Metadata.CausationId);
        Assert.Equal(userId, sequencedEvent.Metadata.UserId);
        Assert.Equal(timestamp, sequencedEvent.Metadata.Timestamp);
    }

    #endregion

    #region AppendEventAsync Tests

    [Fact]
    public async Task AppendEventAsync_WithMinimalParameters_CreatesSequencedEvent()
    {
        // Arrange
        var @event = new TestEventData { Id = Guid.NewGuid() };
        SequencedEvent[]? capturedEvents = null;

        _mockEventStore
            .Setup(x => x.AppendAsync(It.IsAny<SequencedEvent[]>(), It.IsAny<AppendCondition?>()))
            .Callback<SequencedEvent[], AppendCondition?>((events, _) => capturedEvents = events)
            .Returns(Task.CompletedTask);

        // Act
        await _mockEventStore.Object.AppendEventAsync(@event);

        // Assert
        Assert.NotNull(capturedEvents);
        Assert.Single(capturedEvents);
        Assert.Equal("TestEventData", capturedEvents[0].Event.EventType);
        Assert.Same(@event, capturedEvents[0].Event.Event);
    }

    [Fact]
    public async Task AppendEventAsync_WithTags_AttachesTags()
    {
        // Arrange
        var @event = new TestEventData { Id = Guid.NewGuid() };
        var tags = new[]
        {
            new Tag { Key = "studentId", Value = "123" },
            new Tag { Key = "email", Value = "test@example.com" }
        };
        SequencedEvent[]? capturedEvents = null;

        _mockEventStore
            .Setup(x => x.AppendAsync(It.IsAny<SequencedEvent[]>(), It.IsAny<AppendCondition?>()))
            .Callback<SequencedEvent[], AppendCondition?>((events, _) => capturedEvents = events)
            .Returns(Task.CompletedTask);

        // Act
        await _mockEventStore.Object.AppendEventAsync(@event, tags: tags);

        // Assert
        Assert.NotNull(capturedEvents);
        Assert.Equal(2, capturedEvents[0].Event.Tags.Count);
        Assert.Equal("studentId", capturedEvents[0].Event.Tags[0].Key);
        Assert.Equal("email", capturedEvents[0].Event.Tags[1].Key);
    }

    [Fact]
    public async Task AppendEventAsync_WithMetadata_UsesProvidedMetadata()
    {
        // Arrange
        var @event = new TestEventData { Id = Guid.NewGuid() };
        var customMetadata = new Metadata
        {
            Timestamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            CorrelationId = Guid.NewGuid()
        };
        SequencedEvent[]? capturedEvents = null;

        _mockEventStore
            .Setup(x => x.AppendAsync(It.IsAny<SequencedEvent[]>(), It.IsAny<AppendCondition?>()))
            .Callback<SequencedEvent[], AppendCondition?>((events, _) => capturedEvents = events)
            .Returns(Task.CompletedTask);

        // Act
        await _mockEventStore.Object.AppendEventAsync(@event, metadata: customMetadata);

        // Assert
        Assert.NotNull(capturedEvents);
        Assert.Same(customMetadata, capturedEvents[0].Metadata);
    }

    [Fact]
    public async Task AppendEventAsync_WithCondition_PassesCondition()
    {
        // Arrange
        var @event = new TestEventData { Id = Guid.NewGuid() };
        var condition = new AppendCondition 
        { 
            FailIfEventsMatch = Query.All(),
            AfterSequencePosition = 10 
        };
        AppendCondition? capturedCondition = null;

        _mockEventStore
            .Setup(x => x.AppendAsync(It.IsAny<SequencedEvent[]>(), It.IsAny<AppendCondition?>()))
            .Callback<SequencedEvent[], AppendCondition?>((_, c) => capturedCondition = c)
            .Returns(Task.CompletedTask);

        // Act
        await _mockEventStore.Object.AppendEventAsync(@event, condition: condition);

        // Assert
        Assert.Same(condition, capturedCondition);
    }

    [Fact]
    public async Task AppendEventAsync_ThrowsIfEventStoreIsNull()
    {
        // Arrange
        IEventStore? nullStore = null;
        var @event = new TestEventData { Id = Guid.NewGuid() };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await nullStore!.AppendEventAsync(@event));
    }

    [Fact]
    public async Task AppendEventAsync_ThrowsIfEventIsNull()
    {
        // Arrange
        IEvent? nullEvent = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _mockEventStore.Object.AppendEventAsync(nullEvent!));
    }

    #endregion

    #region AppendEventsAsync Tests

    [Fact]
    public async Task AppendEventsAsync_WithMinimalParameters_CreatesSequencedEvents()
    {
        // Arrange
        var events = new IEvent[]
        {
            new TestEventData { Id = Guid.NewGuid() },
            new TestEventData { Id = Guid.NewGuid() }
        };
        SequencedEvent[]? capturedEvents = null;

        _mockEventStore
            .Setup(x => x.AppendAsync(It.IsAny<SequencedEvent[]>(), It.IsAny<AppendCondition?>()))
            .Callback<SequencedEvent[], AppendCondition?>((e, _) => capturedEvents = e)
            .Returns(Task.CompletedTask);

        // Act
        await _mockEventStore.Object.AppendEventsAsync(events);

        // Assert
        Assert.NotNull(capturedEvents);
        Assert.Equal(2, capturedEvents.Length);
        Assert.Equal("TestEventData", capturedEvents[0].Event.EventType);
        Assert.Equal("TestEventData", capturedEvents[1].Event.EventType);
    }

    [Fact]
    public async Task AppendEventsAsync_WithSharedMetadata_UsesSameMetadataForAll()
    {
        // Arrange
        var events = new IEvent[]
        {
            new TestEventData { Id = Guid.NewGuid() },
            new TestEventData { Id = Guid.NewGuid() }
        };
        var sharedMetadata = new Metadata
        {
            Timestamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            CorrelationId = Guid.NewGuid()
        };
        SequencedEvent[]? capturedEvents = null;

        _mockEventStore
            .Setup(x => x.AppendAsync(It.IsAny<SequencedEvent[]>(), It.IsAny<AppendCondition?>()))
            .Callback<SequencedEvent[], AppendCondition?>((e, _) => capturedEvents = e)
            .Returns(Task.CompletedTask);

        // Act
        await _mockEventStore.Object.AppendEventsAsync(events, metadata: sharedMetadata);

        // Assert
        Assert.NotNull(capturedEvents);
        Assert.Same(sharedMetadata, capturedEvents[0].Metadata);
        Assert.Same(sharedMetadata, capturedEvents[1].Metadata);
    }

    [Fact]
    public async Task AppendEventsAsync_WithSharedTags_UsesTagsForAllEvents()
    {
        // Arrange
        var events = new IEvent[]
        {
            new TestEventData { Id = Guid.NewGuid() },
            new TestEventData { Id = Guid.NewGuid() }
        };
        var sharedTags = new[] { new Tag { Key = "batch", Value = "import-2024" } };
        SequencedEvent[]? capturedEvents = null;

        _mockEventStore
            .Setup(x => x.AppendAsync(It.IsAny<SequencedEvent[]>(), It.IsAny<AppendCondition?>()))
            .Callback<SequencedEvent[], AppendCondition?>((e, _) => capturedEvents = e)
            .Returns(Task.CompletedTask);

        // Act
        await _mockEventStore.Object.AppendEventsAsync(events, tags: sharedTags);

        // Assert
        Assert.NotNull(capturedEvents);
        Assert.Single(capturedEvents[0].Event.Tags);
        Assert.Single(capturedEvents[1].Event.Tags);
        Assert.Equal("batch", capturedEvents[0].Event.Tags[0].Key);
        Assert.Equal("batch", capturedEvents[1].Event.Tags[0].Key);
    }

    [Fact]
    public async Task AppendEventsAsync_ThrowsIfEventStoreIsNull()
    {
        // Arrange
        IEventStore? nullStore = null;
        var events = new IEvent[] { new TestEventData { Id = Guid.NewGuid() } };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await nullStore!.AppendEventsAsync(events));
    }

    [Fact]
    public async Task AppendEventsAsync_ThrowsIfEventsIsNull()
    {
        // Arrange
        IEvent[]? nullEvents = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _mockEventStore.Object.AppendEventsAsync(nullEvents!));
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task Extensions_WorkWithMockedEventStore()
    {
        // Arrange
        var events = new[] { CreateTestEvent(1), CreateTestEvent(2) };
        var query = Query.All();

        _mockEventStore
            .Setup(x => x.AppendAsync(It.IsAny<SequencedEvent[]>(), It.IsAny<AppendCondition?>()))
            .Returns(Task.CompletedTask);

        _mockEventStore
            .Setup(x => x.ReadAsync(It.IsAny<Query>(), It.IsAny<ReadOption[]?>()))
            .ReturnsAsync(events);

        // Act - Use all extension methods
        await _mockEventStore.Object.AppendAsync(events[0]); // Single event
        await _mockEventStore.Object.AppendAsync(events); // Array without condition
        var readResult1 = await _mockEventStore.Object.ReadAsync(query); // No options
        var readResult2 = await _mockEventStore.Object.ReadAsync(query, ReadOption.Descending); // Single option

        // Assert
        _mockEventStore.Verify(x => x.AppendAsync(
            It.IsAny<SequencedEvent[]>(),
            It.IsAny<AppendCondition?>()), Times.Exactly(2));

        _mockEventStore.Verify(x => x.ReadAsync(
            It.IsAny<Query>(),
            It.IsAny<ReadOption[]?>()), Times.Exactly(2));

        Assert.Same(events, readResult1);
        Assert.Same(events, readResult2);
    }

    [Fact]
    public async Task AppendAsync_SingleEvent_WorksWithRealEventStoreImplementation()
    {
        // This test verifies extensions work with any IEventStore implementation
        // Arrange
        var called = false;
        var testStore = new TestEventStore(() => called = true);
        var @event = CreateTestEvent(1);

        // Act
        await testStore.AppendAsync(@event);

        // Assert
        Assert.True(called, "Extension should call the core AppendAsync method");
    }

    #endregion

    #region Helper Methods

    private static SequencedEvent CreateTestEvent(long position)
    {
        return new SequencedEvent
        {
            Position = position,
            Event = new DomainEvent
            {
                EventType = "TestEvent",
                Event = new TestEventData { Id = Guid.NewGuid() },
                Tags = []
            },
            Metadata = new Metadata
            {
                Timestamp = DateTimeOffset.UtcNow
            }
        };
    }

    private class TestEventData : IEvent
    {
        public Guid Id { get; set; }
    }

    private class TestEventStore : IEventStore
    {
        private readonly Action _onAppend;

        public TestEventStore(Action onAppend)
        {
            _onAppend = onAppend;
        }

        public Task AppendAsync(SequencedEvent[] events, AppendCondition? condition)
        {
            _onAppend();
            return Task.CompletedTask;
        }

        public Task<SequencedEvent[]> ReadAsync(Query query, ReadOption[]? readOptions)
        {
            return Task.FromResult(Array.Empty<SequencedEvent>());
        }
    }

    #endregion
}
