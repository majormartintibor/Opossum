using Opossum.Core;
using Opossum.Extensions;

namespace Opossum.UnitTests.Extensions;

/// <summary>
/// Unit tests for EventStoreExtensions.
/// These tests verify the extension methods correctly transform and delegate to the core IEventStore methods.
/// Uses a simple stub implementation instead of mocking to follow unit test principles.
/// </summary>
public class EventStoreExtensionsTests
{

    #region AppendAsync - Single Event Tests

    [Fact]
    public async Task AppendAsync_SingleEvent_CallsCoreMethodWithArray()
    {
        // Arrange
        var stub = new EventStoreStub();
        var newEvent = CreateTestEvent(1);

        // Act
        await stub.AppendAsync(newEvent);

        // Assert
        Assert.NotNull(stub.LastAppendedEvents);
        Assert.Single(stub.LastAppendedEvents);
        Assert.Same(newEvent, stub.LastAppendedEvents[0]);
    }

    [Fact]
    public async Task AppendAsync_SingleEvent_PassesNullCondition()
    {
        // Arrange
        var stub = new EventStoreStub();
        var newEvent = CreateTestEvent(1);

        // Act
        await stub.AppendAsync(newEvent);

        // Assert
        Assert.Null(stub.LastAppendCondition);
    }

    [Fact]
    public async Task AppendAsync_SingleEventWithCondition_PassesCondition()
    {
        // Arrange
        var stub = new EventStoreStub();
        var newEvent = CreateTestEvent(1);
        var appendCondition = new AppendCondition
        {
            FailIfEventsMatch = Query.All(),
            AfterSequencePosition = 10
        };

        // Act
        await stub.AppendAsync(newEvent, appendCondition);

        // Assert
        Assert.Same(appendCondition, stub.LastAppendCondition);
    }

    [Fact]
    public async Task AppendAsync_SingleEvent_ThrowsIfEventStoreIsNull()
    {
        // Arrange
        IEventStore? nullStore = null;
        var newEvent = CreateTestEvent(1);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await nullStore!.AppendAsync(newEvent));
    }

    [Fact]
    public async Task AppendAsync_SingleEvent_ThrowsIfEventIsNull()
    {
        // Arrange
        var stub = new EventStoreStub();
        NewEvent? nullEvent = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await stub.AppendAsync(nullEvent!));
    }

    #endregion

    #region AppendAsync - Array Without Condition Tests

    [Fact]
    public async Task AppendAsync_ArrayWithoutCondition_CallsCoreMethodWithNullCondition()
    {
        // Arrange
        var stub = new EventStoreStub();
        var events = new[]
        {
            CreateTestEvent(1),
            CreateTestEvent(2),
            CreateTestEvent(3)
        };

        // Act
        await stub.AppendAsync(events);

        // Assert
        Assert.Null(stub.LastAppendCondition);
        Assert.Same(events, stub.LastAppendedEvents);
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
        var stub = new EventStoreStub();
        NewEvent[]? nullEvents = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await stub.AppendAsync(nullEvents!));
    }

    #endregion

    #region ReadAsync - Single ReadOption Tests

    [Fact]
    public async Task ReadAsync_SingleReadOption_CallsCoreMethodWithArray()
    {
        // Arrange
        var stub = new EventStoreStub();
        var query = Query.All();
        var readOption = ReadOption.Descending;

        // Act
        await stub.ReadAsync(query, readOption);

        // Assert
        Assert.NotNull(stub.LastReadOptions);
        Assert.Single(stub.LastReadOptions);
        Assert.Equal(ReadOption.Descending, stub.LastReadOptions[0]);
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
        var stub = new EventStoreStub();
        Query? nullQuery = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await stub.ReadAsync(nullQuery!, ReadOption.Descending));
    }

    #endregion

    #region ReadAsync - No Options Tests

    [Fact]
    public async Task ReadAsync_NoOptions_CallsCoreMethodWithNull()
    {
        // Arrange
        var stub = new EventStoreStub();
        var query = Query.All();

        // Act
        await stub.ReadAsync(query);

        // Assert
        Assert.Null(stub.LastReadOptions);
        Assert.Same(query, stub.LastQuery);
    }

    [Fact]
    public async Task ReadAsync_NoOptions_ReturnsEventsFromCoreMethod()
    {
        // Arrange
        var query = Query.All();
        var expectedEvents = new[]
        {
            CreateSequencedEvent(1),
            CreateSequencedEvent(2)
        };
        var stub = new EventStoreStub { EventsToReturn = expectedEvents };

        // Act
        var result = await stub.ReadAsync(query);

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
        var stub = new EventStoreStub();
        Query? nullQuery = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await stub.ReadAsync(nullQuery!));
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
        NewEvent newEvent = @event.ToDomainEvent()
            .WithTag("key1", "value1");

        // Assert
        Assert.NotNull(newEvent);
        Assert.Equal("TestEventData", newEvent.Event.EventType);
        Assert.Single(newEvent.Event.Tags);
    }

    [Fact]
    public void DomainEventBuilder_ImplicitConversion_ThrowsIfBuilderIsNull()
    {
        // Arrange
        DomainEventBuilder? nullBuilder = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
        {
            NewEvent _ = nullBuilder!;
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
        var stub = new EventStoreStub();
        var @event = new TestEventData { Id = Guid.NewGuid() };

        // Act
        await stub.AppendEventAsync(@event);

        // Assert
        Assert.NotNull(stub.LastAppendedEvents);
        Assert.Single(stub.LastAppendedEvents);
        Assert.Equal("TestEventData", stub.LastAppendedEvents[0].Event.EventType);
        Assert.Same(@event, stub.LastAppendedEvents[0].Event.Event);
    }

    [Fact]
    public async Task AppendEventAsync_WithTags_AttachesTags()
    {
        // Arrange
        var stub = new EventStoreStub();
        var @event = new TestEventData { Id = Guid.NewGuid() };
        var tags = new[]
        {
            new Tag { Key = "studentId", Value = "123" },
            new Tag { Key = "email", Value = "test@example.com" }
        };

        // Act
        await stub.AppendEventAsync(@event, tags: tags);

        // Assert
        Assert.NotNull(stub.LastAppendedEvents);
        Assert.Equal(2, stub.LastAppendedEvents[0].Event.Tags.Count);
        Assert.Equal("studentId", stub.LastAppendedEvents[0].Event.Tags[0].Key);
        Assert.Equal("email", stub.LastAppendedEvents[0].Event.Tags[1].Key);
    }

    [Fact]
    public async Task AppendEventAsync_WithMetadata_UsesProvidedMetadata()
    {
        // Arrange
        var stub = new EventStoreStub();
        var @event = new TestEventData { Id = Guid.NewGuid() };
        var customMetadata = new Metadata
        {
            Timestamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            CorrelationId = Guid.NewGuid()
        };

        // Act
        await stub.AppendEventAsync(@event, metadata: customMetadata);

        // Assert
        Assert.NotNull(stub.LastAppendedEvents);
        Assert.Same(customMetadata, stub.LastAppendedEvents[0].Metadata);
    }

    [Fact]
    public async Task AppendEventAsync_WithCondition_PassesCondition()
    {
        // Arrange
        var stub = new EventStoreStub();
        var @event = new TestEventData { Id = Guid.NewGuid() };
        var condition = new AppendCondition
        {
            FailIfEventsMatch = Query.All(),
            AfterSequencePosition = 10
        };

        // Act
        await stub.AppendEventAsync(@event, condition: condition);

        // Assert
        Assert.Same(condition, stub.LastAppendCondition);
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
        var stub = new EventStoreStub();
        IEvent? nullEvent = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await stub.AppendEventAsync(nullEvent!));
    }

    #endregion

    #region AppendEventsAsync Tests

    [Fact]
    public async Task AppendEventsAsync_WithMinimalParameters_CreatesSequencedEvents()
    {
        // Arrange
        var stub = new EventStoreStub();
        var events = new IEvent[]
        {
            new TestEventData { Id = Guid.NewGuid() },
            new TestEventData { Id = Guid.NewGuid() }
        };

        // Act
        await stub.AppendEventsAsync(events);

        // Assert
        Assert.NotNull(stub.LastAppendedEvents);
        Assert.Equal(2, stub.LastAppendedEvents.Length);
        Assert.Equal("TestEventData", stub.LastAppendedEvents[0].Event.EventType);
        Assert.Equal("TestEventData", stub.LastAppendedEvents[1].Event.EventType);
    }

    [Fact]
    public async Task AppendEventsAsync_WithSharedMetadata_UsesSameMetadataForAll()
    {
        // Arrange
        var stub = new EventStoreStub();
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

        // Act
        await stub.AppendEventsAsync(events, metadata: sharedMetadata);

        // Assert
        Assert.NotNull(stub.LastAppendedEvents);
        Assert.Same(sharedMetadata, stub.LastAppendedEvents[0].Metadata);
        Assert.Same(sharedMetadata, stub.LastAppendedEvents[1].Metadata);
    }

    [Fact]
    public async Task AppendEventsAsync_WithSharedTags_UsesTagsForAllEvents()
    {
        // Arrange
        var stub = new EventStoreStub();
        var events = new IEvent[]
        {
            new TestEventData { Id = Guid.NewGuid() },
            new TestEventData { Id = Guid.NewGuid() }
        };
        var sharedTags = new[] { new Tag { Key = "batch", Value = "import-2024" } };

        // Act
        await stub.AppendEventsAsync(events, tags: sharedTags);

        // Assert
        Assert.NotNull(stub.LastAppendedEvents);
        Assert.Single(stub.LastAppendedEvents[0].Event.Tags);
        Assert.Single(stub.LastAppendedEvents[1].Event.Tags);
        Assert.Equal("batch", stub.LastAppendedEvents[0].Event.Tags[0].Key);
        Assert.Equal("batch", stub.LastAppendedEvents[1].Event.Tags[0].Key);
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
        var stub = new EventStoreStub();
        IEvent[]? nullEvents = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await stub.AppendEventsAsync(nullEvents!));
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task Extensions_WorkWithStubEventStore()
    {
        // Arrange
        var stub = new EventStoreStub
        {
            EventsToReturn = [CreateSequencedEvent(1), CreateSequencedEvent(2)]
        };
        var singleAppendEvent = CreateTestEvent(1);
        var arrayAppendEvents = new[] { CreateTestEvent(1), CreateTestEvent(2) };
        var query = Query.All();

        // Act - Use all extension methods
        await stub.AppendAsync(singleAppendEvent); // Single event
        await stub.AppendAsync(arrayAppendEvents); // Array without condition
        var readResult1 = await stub.ReadAsync(query); // No options
        var readResult2 = await stub.ReadAsync(query, ReadOption.Descending); // Single option

        // Assert - Verify all methods were called correctly
        Assert.Same(stub.EventsToReturn, readResult1);
        Assert.Same(stub.EventsToReturn, readResult2);
        Assert.Same(arrayAppendEvents, stub.LastAppendedEvents);
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

    private static NewEvent CreateTestEvent(long position)
    {
        return new NewEvent
        {
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

    private static SequencedEvent CreateSequencedEvent(long position)
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

    #endregion

    #region Test Helpers

    /// <summary>
    /// Simple stub implementation of IEventStore for unit testing extension methods.
    /// Captures method calls and parameters for verification without using mocking frameworks.
    /// </summary>
    private class EventStoreStub : IEventStore
    {
        public NewEvent[]? LastAppendedEvents { get; private set; }
        public AppendCondition? LastAppendCondition { get; private set; }
        public Query? LastQuery { get; private set; }
        public ReadOption[]? LastReadOptions { get; private set; }
        public SequencedEvent[] EventsToReturn { get; set; } = [];

        public Task AppendAsync(NewEvent[] events, AppendCondition? condition)
        {
            LastAppendedEvents = events;
            LastAppendCondition = condition;
            return Task.CompletedTask;
        }

        public long? LastFromPosition { get; private set; }

        public Task<SequencedEvent[]> ReadAsync(Query query, ReadOption[]? readOptions, long? fromPosition = null)
        {
            LastQuery = query;
            LastReadOptions = readOptions;
            LastFromPosition = fromPosition;
            return Task.FromResult(EventsToReturn);
        }
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

        public Task AppendAsync(NewEvent[] events, AppendCondition? condition)
        {
            _onAppend();
            return Task.CompletedTask;
        }

        public Task<SequencedEvent[]> ReadAsync(Query query, ReadOption[]? readOptions, long? fromPosition = null)
        {
            return Task.FromResult(Array.Empty<SequencedEvent>());
        }
    }

    #endregion
}
