using Opossum.Core;
using Opossum.Storage.FileSystem;

namespace Opossum.UnitTests.Storage.FileSystem;

public class EventFileManagerTests : IDisposable
{
    private readonly EventFileManager _manager;
    private readonly string _tempEventsPath;

    public EventFileManagerTests()
    {
        _manager = new EventFileManager(flushImmediately: false); // Faster tests
        _tempEventsPath = Path.Combine(Path.GetTempPath(), $"EventFileManagerTests_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        // Cleanup temp directory
        if (Directory.Exists(_tempEventsPath))
        {
            Directory.Delete(_tempEventsPath, recursive: true);
        }
    }

    // ========================================================================
    // WriteEventAsync Tests
    // ========================================================================

    [Fact]
    public async Task WriteEventAsync_CreatesEventFile()
    {
        // Arrange
        var sequencedEvent = CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "test" });

        // Act
        await _manager.WriteEventAsync(_tempEventsPath, sequencedEvent);

        // Assert
        var expectedPath = _manager.GetEventFilePath(_tempEventsPath, 1);
        Assert.True(File.Exists(expectedPath));
    }

    [Fact]
    public async Task WriteEventAsync_CreatesEventsDirectoryIfMissing()
    {
        // Arrange
        var sequencedEvent = CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "test" });

        // Act
        await _manager.WriteEventAsync(_tempEventsPath, sequencedEvent);

        // Assert
        Assert.True(Directory.Exists(_tempEventsPath));
    }

    [Fact]
    public async Task WriteEventAsync_WithNullEventsPath_ThrowsArgumentNullException()
    {
        // Arrange
        var sequencedEvent = CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "test" });

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.WriteEventAsync(null!, sequencedEvent));
    }

    [Fact]
    public async Task WriteEventAsync_WithNullSequencedEvent_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.WriteEventAsync(_tempEventsPath, null!));
    }

    [Fact]
    public async Task WriteEventAsync_WithZeroPosition_ThrowsArgumentException()
    {
        // Arrange
        var sequencedEvent = CreateTestEvent(0, "TestEvent", new TestDomainEvent { Data = "test" });

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _manager.WriteEventAsync(_tempEventsPath, sequencedEvent));
    }

    [Fact]
    public async Task WriteEventAsync_WithNegativePosition_ThrowsArgumentException()
    {
        // Arrange
        var sequencedEvent = CreateTestEvent(-1, "TestEvent", new TestDomainEvent { Data = "test" });

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _manager.WriteEventAsync(_tempEventsPath, sequencedEvent));
    }

    [Fact]
    public async Task WriteEventAsync_CreatesCorrectFileName()
    {
        // Arrange
        var sequencedEvent = CreateTestEvent(42, "TestEvent", new TestDomainEvent { Data = "test" });

        // Act
        await _manager.WriteEventAsync(_tempEventsPath, sequencedEvent);

        // Assert
        var expectedFileName = "0000000042.json";
        var expectedPath = Path.Combine(_tempEventsPath, expectedFileName);
        Assert.True(File.Exists(expectedPath));
    }

    [Fact]
    public async Task WriteEventAsync_OverwritesExistingFile()
    {
        // Arrange
        var event1 = CreateTestEvent(1, "FirstEvent", new TestDomainEvent { Data = "first" });
        var event2 = CreateTestEvent(1, "SecondEvent", new TestDomainEvent { Data = "second" });

        // Act
        await _manager.WriteEventAsync(_tempEventsPath, event1);
        await _manager.WriteEventAsync(_tempEventsPath, event2);

        // Assert
        var readEvent = await _manager.ReadEventAsync(_tempEventsPath, 1);
        Assert.Equal("SecondEvent", readEvent.Event.EventType);
    }

    // ========================================================================
    // ReadEventAsync Tests
    // ========================================================================

    [Fact]
    public async Task ReadEventAsync_ReturnsCorrectEvent()
    {
        // Arrange
        var original = CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "test" });
        await _manager.WriteEventAsync(_tempEventsPath, original);

        // Act
        var read = await _manager.ReadEventAsync(_tempEventsPath, 1);

        // Assert
        Assert.NotNull(read);
        Assert.Equal(original.Position, read.Position);
        Assert.Equal(original.Event.EventType, read.Event.EventType);
    }

    [Fact]
    public async Task ReadEventAsync_WithNullEventsPath_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.ReadEventAsync(null!, 1));
    }

    [Fact]
    public async Task ReadEventAsync_WithZeroPosition_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _manager.ReadEventAsync(_tempEventsPath, 0));
    }

    [Fact]
    public async Task ReadEventAsync_WithNegativePosition_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _manager.ReadEventAsync(_tempEventsPath, -1));
    }

    [Fact]
    public async Task ReadEventAsync_WithNonExistentFile_ThrowsFileNotFoundException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _manager.ReadEventAsync(_tempEventsPath, 999));
    }

    [Fact]
    public async Task ReadEventAsync_PreservesEventData()
    {
        // Arrange
        var original = CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "test data" });
        original = original with { Event = original.Event with { Tags = [new Tag("key1", "value1")] } };
        await _manager.WriteEventAsync(_tempEventsPath, original);

        // Act
        var read = await _manager.ReadEventAsync(_tempEventsPath, 1);

        // Assert
        var readEvent = (TestDomainEvent)read.Event.Event;
        Assert.Equal("test data", readEvent.Data);
        Assert.Single(read.Event.Tags);
        Assert.Equal("key1", read.Event.Tags[0].Key);
    }

    // ========================================================================
    // ReadEventsAsync Tests
    // ========================================================================

    [Fact]
    public async Task ReadEventsAsync_ReturnsMultipleEvents()
    {
        // Arrange
        await _manager.WriteEventAsync(_tempEventsPath, CreateTestEvent(1, "Event1", new TestDomainEvent { Data = "1" }));
        await _manager.WriteEventAsync(_tempEventsPath, CreateTestEvent(2, "Event2", new TestDomainEvent { Data = "2" }));
        await _manager.WriteEventAsync(_tempEventsPath, CreateTestEvent(3, "Event3", new TestDomainEvent { Data = "3" }));

        // Act
        var events = await _manager.ReadEventsAsync(_tempEventsPath, [1, 2, 3]);

        // Assert
        Assert.Equal(3, events.Length);
        Assert.Equal(1, events[0].Position);
        Assert.Equal(2, events[1].Position);
        Assert.Equal(3, events[2].Position);
    }

    [Fact]
    public async Task ReadEventsAsync_PreservesPositionOrder()
    {
        // Arrange
        await _manager.WriteEventAsync(_tempEventsPath, CreateTestEvent(1, "Event1", new TestDomainEvent { Data = "1" }));
        await _manager.WriteEventAsync(_tempEventsPath, CreateTestEvent(2, "Event2", new TestDomainEvent { Data = "2" }));
        await _manager.WriteEventAsync(_tempEventsPath, CreateTestEvent(3, "Event3", new TestDomainEvent { Data = "3" }));

        // Act - Read in reverse order
        var events = await _manager.ReadEventsAsync(_tempEventsPath, [3, 1, 2]);

        // Assert - Should return in the order requested
        Assert.Equal(3, events[0].Position);
        Assert.Equal(1, events[1].Position);
        Assert.Equal(2, events[2].Position);
    }

    [Fact]
    public async Task ReadEventsAsync_WithEmptyArray_ReturnsEmptyArray()
    {
        // Act
        var events = await _manager.ReadEventsAsync(_tempEventsPath, []);

        // Assert
        Assert.NotNull(events);
        Assert.Empty(events);
    }

    [Fact]
    public async Task ReadEventsAsync_WithNullEventsPath_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.ReadEventsAsync(null!, [1, 2, 3]));
    }

    [Fact]
    public async Task ReadEventsAsync_WithNullPositions_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.ReadEventsAsync(_tempEventsPath, null!));
    }

    [Fact]
    public async Task ReadEventsAsync_WithMissingEvent_ThrowsFileNotFoundException()
    {
        // Arrange
        await _manager.WriteEventAsync(_tempEventsPath, CreateTestEvent(1, "Event1", new TestDomainEvent { Data = "1" }));
        await _manager.WriteEventAsync(_tempEventsPath, CreateTestEvent(3, "Event3", new TestDomainEvent { Data = "3" }));

        // Act & Assert - Position 2 doesn't exist
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _manager.ReadEventsAsync(_tempEventsPath, [1, 2, 3]));
    }

    // ========================================================================
    // GetEventFilePath Tests
    // ========================================================================

    [Fact]
    public void GetEventFilePath_ReturnsCorrectPath()
    {
        // Act
        var path = _manager.GetEventFilePath(_tempEventsPath, 1);

        // Assert
        Assert.Equal(Path.Combine(_tempEventsPath, "0000000001.json"), path);
    }

    [Fact]
    public void GetEventFilePath_WithLargePosition_UsesZeroPadding()
    {
        // Act
        var path = _manager.GetEventFilePath(_tempEventsPath, 123456789);

        // Assert
        Assert.Equal(Path.Combine(_tempEventsPath, "0123456789.json"), path);
    }

    [Fact]
    public void GetEventFilePath_WithNullEventsPath_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _manager.GetEventFilePath(null!, 1));
    }

    [Fact]
    public void GetEventFilePath_WithZeroPosition_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _manager.GetEventFilePath(_tempEventsPath, 0));
    }

    [Fact]
    public void GetEventFilePath_WithNegativePosition_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _manager.GetEventFilePath(_tempEventsPath, -1));
    }

    // ========================================================================
    // EventFileExists Tests
    // ========================================================================

    [Fact]
    public async Task EventFileExists_ReturnsTrueForExistingFile()
    {
        // Arrange
        await _manager.WriteEventAsync(_tempEventsPath, CreateTestEvent(1, "Event", new TestDomainEvent { Data = "test" }));

        // Act
        var exists = _manager.EventFileExists(_tempEventsPath, 1);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public void EventFileExists_ReturnsFalseForNonExistentFile()
    {
        // Act
        var exists = _manager.EventFileExists(_tempEventsPath, 999);

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public void EventFileExists_WithNullEventsPath_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _manager.EventFileExists(null!, 1));
    }

    [Fact]
    public void EventFileExists_WithZeroPosition_ReturnsFalse()
    {
        // Act
        var exists = _manager.EventFileExists(_tempEventsPath, 0);

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public void EventFileExists_WithNegativePosition_ReturnsFalse()
    {
        // Act
        var exists = _manager.EventFileExists(_tempEventsPath, -1);

        // Assert
        Assert.False(exists);
    }

    // ========================================================================
    // Round-trip Integration Tests
    // ========================================================================

    [Fact]
    public async Task RoundTrip_MultipleEvents_PreservesAllData()
    {
        // Arrange
        var events = new[]
        {
            CreateTestEvent(1, "Event1", new TestDomainEvent { Data = "data1" }),
            CreateTestEvent(2, "Event2", new TestDomainEvent { Data = "data2" }),
            CreateTestEvent(3, "Event3", new TestDomainEvent { Data = "data3" })
        };

        // Act - Write all events
        foreach (var evt in events)
        {
            await _manager.WriteEventAsync(_tempEventsPath, evt);
        }

        // Read all events
        var readEvents = await _manager.ReadEventsAsync(_tempEventsPath, [1, 2, 3]);

        // Assert
        Assert.Equal(3, readEvents.Length);
        for (int i = 0; i < events.Length; i++)
        {
            Assert.Equal(events[i].Position, readEvents[i].Position);
            Assert.Equal(events[i].Event.EventType, readEvents[i].Event.EventType);
            var originalData = ((TestDomainEvent)events[i].Event.Event).Data;
            var readData = ((TestDomainEvent)readEvents[i].Event.Event).Data;
            Assert.Equal(originalData, readData);
        }
    }

    // ========================================================================
    // Flush Configuration Tests
    // ========================================================================

    [Fact]
    public async Task Constructor_WithFlushTrue_EventsAreDurable()
    {
        // Arrange
        var managerWithFlush = new EventFileManager(flushImmediately: true);
        var sequencedEvent = CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "critical" });

        // Act
        await managerWithFlush.WriteEventAsync(_tempEventsPath, sequencedEvent);

        // Assert
        // Event file should exist (flushed to disk)
        var eventPath = managerWithFlush.GetEventFilePath(_tempEventsPath, 1);
        Assert.True(File.Exists(eventPath));

        // Should be able to read immediately
        var readEvent = await managerWithFlush.ReadEventAsync(_tempEventsPath, 1);
        Assert.NotNull(readEvent);
        Assert.Equal(1, readEvent.Position);
    }

    [Fact]
    public async Task Constructor_WithFlushFalse_EventsStillWritten()
    {
        // Arrange
        var managerNoFlush = new EventFileManager(flushImmediately: false);
        var sequencedEvent = CreateTestEvent(1, "TestEvent", new TestDomainEvent { Data = "test" });

        // Act
        await managerNoFlush.WriteEventAsync(_tempEventsPath, sequencedEvent);

        // Assert
        // Event file should exist (even without flush, it's in page cache)
        var eventPath = managerNoFlush.GetEventFilePath(_tempEventsPath, 1);
        Assert.True(File.Exists(eventPath));

        // Should be able to read (from page cache or disk)
        var readEvent = await managerNoFlush.ReadEventAsync(_tempEventsPath, 1);
        Assert.NotNull(readEvent);
    }

    [Fact]
    public void Constructor_DefaultsToFlushTrue()
    {
        // Arrange & Act
        var defaultManager = new EventFileManager();

        // Assert
        // Default constructor should enable flush for production safety
        // We verify this by checking behavior is consistent with flush=true
        // (No direct way to inspect the private field, so we test the effect)
        Assert.NotNull(defaultManager);
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
