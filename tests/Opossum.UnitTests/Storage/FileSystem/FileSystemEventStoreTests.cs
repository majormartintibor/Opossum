using Opossum.Core;
using Opossum.Configuration;
using Opossum.Exceptions;
using Opossum.Storage.FileSystem;

namespace Opossum.UnitTests.Storage.FileSystem;

public class FileSystemEventStoreTests : IDisposable
{
    private readonly OpossumOptions _options;
    private readonly FileSystemEventStore _store;
    private readonly string _tempRootPath;

    public FileSystemEventStoreTests()
    {
        _tempRootPath = Path.Combine(Path.GetTempPath(), $"FileSystemEventStoreTests_{Guid.NewGuid():N}");
        _options = new OpossumOptions
        {
            RootPath = _tempRootPath,
            FlushEventsImmediately = false // Faster tests
        };
        _options.AddContext("TestContext");

        _store = new FileSystemEventStore(_options);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRootPath))
        {
            Directory.Delete(_tempRootPath, recursive: true);
        }
    }

    // ========================================================================
    // AppendAsync - Basic Tests
    // ========================================================================

    [Fact]
    public async Task AppendAsync_WithSingleEvent_SuccessfullyAppendsEvent()
    {
        // Arrange
        var events = new[] { CreateTestEvent("TestEvent", new TestDomainEvent { Data = "test" }) };

        // Act
        await _store.AppendAsync(events, null);

        // Assert
        var contextPath = Path.Combine(_tempRootPath, "TestContext");
        var eventsPath = Path.Combine(contextPath, "events");
        var eventFile = Path.Combine(eventsPath, "0000000001.json");
        Assert.True(File.Exists(eventFile));
    }

    [Fact]
    public async Task AppendAsync_WithMultipleEvents_AssignsSequentialPositions()
    {
        // Arrange
        var events = new[]
        {
            CreateTestEvent("Event1", new TestDomainEvent { Data = "1" }),
            CreateTestEvent("Event2", new TestDomainEvent { Data = "2" }),
            CreateTestEvent("Event3", new TestDomainEvent { Data = "3" })
        };

        // Act
        await _store.AppendAsync(events, null);

        // Assert
        Assert.Equal(1, events[0].Position);
        Assert.Equal(2, events[1].Position);
        Assert.Equal(3, events[2].Position);
    }

    [Fact]
    public async Task AppendAsync_WritesAllEventFiles()
    {
        // Arrange
        var events = new[]
        {
            CreateTestEvent("Event1", new TestDomainEvent { Data = "1" }),
            CreateTestEvent("Event2", new TestDomainEvent { Data = "2" }),
            CreateTestEvent("Event3", new TestDomainEvent { Data = "3" })
        };

        // Act
        await _store.AppendAsync(events, null);

        // Assert
        var eventsPath = Path.Combine(_tempRootPath, "TestContext", "events");
        Assert.True(File.Exists(Path.Combine(eventsPath, "0000000001.json")));
        Assert.True(File.Exists(Path.Combine(eventsPath, "0000000002.json")));
        Assert.True(File.Exists(Path.Combine(eventsPath, "0000000003.json")));
    }

    [Fact]
    public async Task AppendAsync_UpdatesLedger()
    {
        // Arrange
        var events = new[]
        {
            CreateTestEvent("Event1", new TestDomainEvent { Data = "1" }),
            CreateTestEvent("Event2", new TestDomainEvent { Data = "2" })
        };

        // Act
        await _store.AppendAsync(events, null);

        // Assert - Verify ledger by checking next append starts at position 3
        var moreEvents = new[] { CreateTestEvent("Event3", new TestDomainEvent { Data = "3" }) };
        await _store.AppendAsync(moreEvents, null);
        Assert.Equal(3, moreEvents[0].Position);
    }

    [Fact]
    public async Task AppendAsync_UpdatesIndices()
    {
        // Arrange
        var events = new[]
        {
            CreateTestEvent("TestEvent", new TestDomainEvent { Data = "1" })
        };

        // Act
        await _store.AppendAsync(events, null);

        // Assert - Verify index files exist
        var indexPath = Path.Combine(_tempRootPath, "TestContext", "Indices");
        var eventTypeIndexPath = Path.Combine(indexPath, "EventType", "TestEvent.json");
        Assert.True(File.Exists(eventTypeIndexPath));
    }

    [Fact]
    public async Task AppendAsync_WithTags_UpdatesTagIndices()
    {
        // Arrange
        var events = new[] { CreateTestEvent("TestEvent", new TestDomainEvent { Data = "1" }) };
        events[0].Event.Tags.Add(new Tag { Key = "Environment", Value = "Production" });

        // Act
        await _store.AppendAsync(events, null);

        // Assert
        var tagIndexPath = Path.Combine(_tempRootPath, "TestContext", "Indices", "Tags", "Environment_Production.json");
        Assert.True(File.Exists(tagIndexPath));
    }

    [Fact]
    public async Task AppendAsync_SetsTimestampIfNotProvided()
    {
        // Arrange
        var beforeAppend = DateTimeOffset.UtcNow;
        var events = new[] { CreateTestEvent("TestEvent", new TestDomainEvent { Data = "test" }) };
        Assert.Equal(default(DateTimeOffset), events[0].Metadata.Timestamp); // Not set

        // Act
        await _store.AppendAsync(events, null);

        // Assert
        var afterAppend = DateTimeOffset.UtcNow;
        Assert.NotEqual(default(DateTimeOffset), events[0].Metadata.Timestamp);
        Assert.True(events[0].Metadata.Timestamp >= beforeAppend);
        Assert.True(events[0].Metadata.Timestamp <= afterAppend);
    }

    [Fact]
    public async Task AppendAsync_PreservesExistingTimestamp()
    {
        // Arrange
        var specificTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var events = new[] { CreateTestEvent("TestEvent", new TestDomainEvent { Data = "test" }) };
        events[0].Metadata.Timestamp = specificTime;

        // Act
        await _store.AppendAsync(events, null);

        // Assert
        Assert.Equal(specificTime, events[0].Metadata.Timestamp);
    }

    // ========================================================================
    // AppendAsync - Validation Tests
    // ========================================================================

    [Fact]
    public async Task AppendAsync_WithNullEvents_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _store.AppendAsync(null!, null));
    }

    [Fact]
    public async Task AppendAsync_WithEmptyArray_ThrowsArgumentException()
    {
        // Arrange
        var events = Array.Empty<SequencedEvent>();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _store.AppendAsync(events, null));
    }

    [Fact]
    public async Task AppendAsync_WithNullEvent_ThrowsArgumentException()
    {
        // Arrange
        var events = new[] { CreateTestEvent("TestEvent", new TestDomainEvent { Data = "test" }) };
        events[0].Event = null!;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _store.AppendAsync(events, null));
        Assert.Contains("Event at index 0 has null Event property", ex.Message);
    }

    [Fact]
    public async Task AppendAsync_WithEmptyEventType_ThrowsArgumentException()
    {
        // Arrange
        var events = new[] { CreateTestEvent("", new TestDomainEvent { Data = "test" }) };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _store.AppendAsync(events, null));
        Assert.Contains("Event at index 0 has empty EventType", ex.Message);
    }

    [Fact]
    public async Task AppendAsync_WithNoContextsConfigured_ThrowsInvalidOperationException()
    {
        // Arrange
        var optionsNoContext = new OpossumOptions { RootPath = _tempRootPath };
        var storeNoContext = new FileSystemEventStore(optionsNoContext);
        var events = new[] { CreateTestEvent("TestEvent", new TestDomainEvent { Data = "test" }) };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => storeNoContext.AppendAsync(events, null));
    }

    // ========================================================================
    // AppendAsync - Concurrency Tests
    // ========================================================================

    [Fact]
    public async Task AppendAsync_MultipleSequentialAppends_MaintainsContinuousSequence()
    {
        // Arrange & Act
        var batch1 = new[] { CreateTestEvent("Event1", new TestDomainEvent { Data = "1" }) };
        await _store.AppendAsync(batch1, null);

        var batch2 = new[] { CreateTestEvent("Event2", new TestDomainEvent { Data = "2" }) };
        await _store.AppendAsync(batch2, null);

        var batch3 = new[] { CreateTestEvent("Event3", new TestDomainEvent { Data = "3" }) };
        await _store.AppendAsync(batch3, null);

        // Assert
        Assert.Equal(1, batch1[0].Position);
        Assert.Equal(2, batch2[0].Position);
        Assert.Equal(3, batch3[0].Position);
    }

    [Fact]
    public async Task AppendAsync_LargerBatch_AssignsCorrectPositions()
    {
        // Arrange
        var events = new SequencedEvent[10];
        for (int i = 0; i < 10; i++)
        {
            events[i] = CreateTestEvent($"Event{i}", new TestDomainEvent { Data = i.ToString() });
        }

        // Act
        await _store.AppendAsync(events, null);

        // Assert
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(i + 1, events[i].Position);
        }
    }

    // ========================================================================
    // AppendAsync - AppendCondition Tests
    // ========================================================================

    [Fact]
    public async Task AppendAsync_WithAfterSequencePositionCondition_Success()
    {
        // Arrange - Append initial events
        var initialEvents = new[]
        {
            CreateTestEvent("Event1", new TestDomainEvent { Data = "1" }),
            CreateTestEvent("Event2", new TestDomainEvent { Data = "2" })
        };
        await _store.AppendAsync(initialEvents, null);

        // Act - Append with correct position condition
        var condition = new AppendCondition
        {
            FailIfEventsMatch = Query.FromEventTypes(), // Empty query
            AfterSequencePosition = 2
        };
        var newEvents = new[] { CreateTestEvent("Event3", new TestDomainEvent { Data = "3" }) };
        await _store.AppendAsync(newEvents, condition);

        // Assert
        Assert.Equal(3, newEvents[0].Position);
    }

    [Fact]
    public async Task AppendAsync_WithAfterSequencePositionCondition_FailsOnMismatch()
    {
        // Arrange - Append initial events
        var initialEvents = new[]
        {
            CreateTestEvent("Event1", new TestDomainEvent { Data = "1" }),
            CreateTestEvent("Event2", new TestDomainEvent { Data = "2" })
        };
        await _store.AppendAsync(initialEvents, null);

        // Append one more event to create a position mismatch
        var additionalEvent = new[] { CreateTestEvent("Event2b", new TestDomainEvent { Data = "2b" }) };
        await _store.AppendAsync(additionalEvent, null);
        // Current position is now 3

        // Act & Assert - Try to append with stale position (0) and a query that matches existing events
        // This should fail because events matching the query exist after position 0
        var condition = new AppendCondition
        {
            FailIfEventsMatch = Query.FromEventTypes("Event1", "Event2", "Event2b"), // Matches existing events
            AfterSequencePosition = 0 // Stale position - we know events exist after this
        };
        var newEvents = new[] { CreateTestEvent("Event3", new TestDomainEvent { Data = "3" }) };

        await Assert.ThrowsAsync<ConcurrencyException>(
            () => _store.AppendAsync(newEvents, condition));
    }

    [Fact]
    public async Task AppendAsync_WithFailIfEventsMatchCondition_SuccessWhenNoMatch()
    {
        // Arrange - Append initial event
        var initialEvents = new[] { CreateTestEvent("Event1", new TestDomainEvent { Data = "1" }) };
        await _store.AppendAsync(initialEvents, null);

        // Act - Append with condition that doesn't match
        var condition = new AppendCondition
        {
            FailIfEventsMatch = Query.FromEventTypes("NonExistentEvent")
        };
        var newEvents = new[] { CreateTestEvent("Event2", new TestDomainEvent { Data = "2" }) };
        await _store.AppendAsync(newEvents, condition);

        // Assert
        Assert.Equal(2, newEvents[0].Position);
    }

    [Fact]
    public async Task AppendAsync_WithFailIfEventsMatchCondition_FailsWhenMatches()
    {
        // Arrange - Append initial event
        var initialEvents = new[] { CreateTestEvent("ConflictingEvent", new TestDomainEvent { Data = "1" }) };
        await _store.AppendAsync(initialEvents, null);

        // Act & Assert - Append with condition that matches existing event
        var condition = new AppendCondition
        {
            FailIfEventsMatch = Query.FromEventTypes("ConflictingEvent")
        };
        var newEvents = new[] { CreateTestEvent("Event2", new TestDomainEvent { Data = "2" }) };
        
        await Assert.ThrowsAsync<ConcurrencyException>(
            () => _store.AppendAsync(newEvents, condition));
    }

    [Fact]
    public async Task AppendAsync_WithTagMatchCondition_SuccessWhenNoMatch()
    {
        // Arrange - Append event with different tag
        var initialEvents = new[] { CreateTestEvent("Event1", new TestDomainEvent { Data = "1" }) };
        initialEvents[0].Event.Tags.Add(new Tag { Key = "Status", Value = "Completed" });
        await _store.AppendAsync(initialEvents, null);

        // Act - Append with condition checking for different tag
        var condition = new AppendCondition
        {
            FailIfEventsMatch = Query.FromTags(new Tag { Key = "Status", Value = "Pending" })
        };
        var newEvents = new[] { CreateTestEvent("Event2", new TestDomainEvent { Data = "2" }) };
        await _store.AppendAsync(newEvents, condition);

        // Assert
        Assert.Equal(2, newEvents[0].Position);
    }

    [Fact]
    public async Task AppendAsync_WithTagMatchCondition_FailsWhenMatches()
    {
        // Arrange - Append event with specific tag
        var initialEvents = new[] { CreateTestEvent("Event1", new TestDomainEvent { Data = "1" }) };
        initialEvents[0].Event.Tags.Add(new Tag { Key = "Status", Value = "Pending" });
        await _store.AppendAsync(initialEvents, null);

        // Act & Assert - Append with condition checking for same tag
        var condition = new AppendCondition
        {
            FailIfEventsMatch = Query.FromTags(new Tag { Key = "Status", Value = "Pending" })
        };
        var newEvents = new[] { CreateTestEvent("Event2", new TestDomainEvent { Data = "2" }) };
        
        await Assert.ThrowsAsync<ConcurrencyException>(
            () => _store.AppendAsync(newEvents, condition));
    }

    [Fact]
    public async Task AppendAsync_WithBothConditions_SuccessWhenAllPass()
    {
        // Arrange - Append initial event
        var initialEvents = new[] { CreateTestEvent("Event1", new TestDomainEvent { Data = "1" }) };
        await _store.AppendAsync(initialEvents, null);

        // Act - Both conditions should pass
        var condition = new AppendCondition
        {
            AfterSequencePosition = 1,
            FailIfEventsMatch = Query.FromEventTypes("NonExistentEvent")
        };
        var newEvents = new[] { CreateTestEvent("Event2", new TestDomainEvent { Data = "2" }) };
        await _store.AppendAsync(newEvents, condition);

        // Assert
        Assert.Equal(2, newEvents[0].Position);
    }

    // ========================================================================
    // Integration Tests
    // ========================================================================

    [Fact]
    public async Task Integration_CompleteWorkflow_AllComponentsWork()
    {
        // Arrange - Create various events with different types and tags
        var event1 = CreateTestEvent("OrderCreated", new TestDomainEvent { Data = "Order123" });
        event1.Event.Tags.Add(new Tag { Key = "Environment", Value = "Production" });
        event1.Event.Tags.Add(new Tag { Key = "Region", Value = "US-West" });

        var event2 = CreateTestEvent("OrderShipped", new TestDomainEvent { Data = "Order123" });
        event2.Event.Tags.Add(new Tag { Key = "Environment", Value = "Production" });

        var event3 = CreateTestEvent("OrderCreated", new TestDomainEvent { Data = "Order456" });
        event3.Event.Tags.Add(new Tag { Key = "Environment", Value = "Development" });

        // Act - Append in batches
        await _store.AppendAsync(new[] { event1 }, null);
        await _store.AppendAsync([event2, event3], null);

        // Assert - Verify positions
        Assert.Equal(1, event1.Position);
        Assert.Equal(2, event2.Position);
        Assert.Equal(3, event3.Position);

        // Verify files exist
        var eventsPath = Path.Combine(_tempRootPath, "TestContext", "events");
        Assert.True(File.Exists(Path.Combine(eventsPath, "0000000001.json")));
        Assert.True(File.Exists(Path.Combine(eventsPath, "0000000002.json")));
        Assert.True(File.Exists(Path.Combine(eventsPath, "0000000003.json")));

        // Verify indices exist
        var indexPath = Path.Combine(_tempRootPath, "TestContext", "Indices");
        Assert.True(File.Exists(Path.Combine(indexPath, "EventType", "OrderCreated.json")));
        Assert.True(File.Exists(Path.Combine(indexPath, "EventType", "OrderShipped.json")));
        Assert.True(File.Exists(Path.Combine(indexPath, "Tags", "Environment_Production.json")));
        Assert.True(File.Exists(Path.Combine(indexPath, "Tags", "Environment_Development.json")));
        Assert.True(File.Exists(Path.Combine(indexPath, "Tags", "Region_US-West.json")));
    }

    // ========================================================================
    // Flush Configuration Tests
    // ========================================================================

    [Fact]
    public async Task EventStore_WithFlushTrue_EventsAreDurable()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), $"FlushTest_{Guid.NewGuid():N}");
        var options = new OpossumOptions
        {
            RootPath = tempPath,
            FlushEventsImmediately = true // Production mode
        };
        options.AddContext("ProductionContext");

        var store = new FileSystemEventStore(options);
        var events = new[] { CreateTestEvent("CriticalEvent", new TestDomainEvent { Data = "important" }) };

        try
        {
            // Act
            await store.AppendAsync(events, null);

            // Assert
            var eventPath = Path.Combine(tempPath, "ProductionContext", "events", "0000000001.json");
            Assert.True(File.Exists(eventPath));

            // Event should be readable (flushed to disk)
            var query = Query.FromEventTypes(["CriticalEvent"]);
            var readEvents = await store.ReadAsync(query, null);
            Assert.Single(readEvents);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EventStore_WithFlushFalse_EventsStillPersisted()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), $"NoFlushTest_{Guid.NewGuid():N}");
        var options = new OpossumOptions
        {
            RootPath = tempPath,
            FlushEventsImmediately = false // Test mode (faster)
        };
        options.AddContext("TestContext");

        var store = new FileSystemEventStore(options);
        var events = new[] { CreateTestEvent("TestEvent", new TestDomainEvent { Data = "test" }) };

        try
        {
            // Act
            await store.AppendAsync(events, null);

            // Assert
            // Events should still exist (in page cache or disk)
            var query = Query.FromEventTypes(["TestEvent"]);
            var readEvents = await store.ReadAsync(query, null);
            Assert.Single(readEvents);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EventStore_DefaultFlushSetting_IsTrue()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), $"DefaultFlushTest_{Guid.NewGuid():N}");
        var options = new OpossumOptions { RootPath = tempPath };
        // Note: NOT setting FlushEventsImmediately - should default to true
        options.AddContext("DefaultContext");

        try
        {
            // Act & Assert
            Assert.True(options.FlushEventsImmediately, 
                "Default FlushEventsImmediately should be true for production safety");

            var store = new FileSystemEventStore(options);
            var events = new[] { CreateTestEvent("DefaultEvent", new TestDomainEvent { Data = "default" }) };

            await store.AppendAsync(events, null);

            // Should work correctly with default flush setting
            var query = Query.All();
            var readEvents = await store.ReadAsync(query, null);
            Assert.Single(readEvents);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, recursive: true);
            }
        }
    }

    // ========================================================================
    // Helper Methods
    // ========================================================================

    private static SequencedEvent CreateTestEvent(string eventType, IEvent domainEvent)
    {
        return new SequencedEvent
        {
            Position = 0, // Will be assigned by AppendAsync
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
