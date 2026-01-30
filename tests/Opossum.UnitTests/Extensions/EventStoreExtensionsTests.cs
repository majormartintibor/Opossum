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
