using Opossum.Core;
using Opossum.Configuration;
using Opossum.Exceptions;
using Opossum.Storage.FileSystem;
using Opossum.UnitTests.Helpers;

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
        _options.UseStore("TestContext");

        _store = new FileSystemEventStore(_options);
    }

    public void Dispose()
    {
        TestDirectoryHelper.ForceDelete(_tempRootPath);
    }

    // ========================================================================
    // AppendAsync - Basic Tests
    // ========================================================================

    [Fact]
    public async Task AppendAsync_WithSingleEvent_SuccessfullyAppendsEventAsync()
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
    public async Task AppendAsync_WithMultipleEvents_AssignsSequentialPositionsAsync()
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

        // Assert - read back to verify positions were assigned sequentially
        var result = await _store.ReadAsync(Query.All(), null);
        Assert.Equal(3, result.Length);
        Assert.Equal(1, result[0].Position);
        Assert.Equal(2, result[1].Position);
        Assert.Equal(3, result[2].Position);
    }

    [Fact]
    public async Task AppendAsync_WritesAllEventFilesAsync()
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
    public async Task AppendAsync_UpdatesLedgerAsync()
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

        var result = await _store.ReadAsync(Query.All(), null);
        Assert.Equal(3, result[^1].Position);
    }

    [Fact]
    public async Task AppendAsync_UpdatesIndicesAsync()
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
    public async Task AppendAsync_WithTags_UpdatesTagIndicesAsync()
    {
        // Arrange
        var events = new[] { CreateTestEvent("TestEvent", new TestDomainEvent { Data = "1" }) };
        events[0].Event = events[0].Event with { Tags = [new Tag("Environment", "Production")] };

        // Act
        await _store.AppendAsync(events, null);

        // Assert
        var tagIndexPath = Path.Combine(_tempRootPath, "TestContext", "Indices", "Tags", "Environment_Production.json");
        Assert.True(File.Exists(tagIndexPath));
    }

    [Fact]
    public async Task AppendAsync_SetsTimestampIfNotProvidedAsync()
    {
        // Arrange
        var beforeAppend = DateTimeOffset.UtcNow;
        var events = new[] { CreateTestEvent("TestEvent", new TestDomainEvent { Data = "test" }) };
        Assert.Equal(default, events[0].Metadata.Timestamp); // Not set

        // Act
        await _store.AppendAsync(events, null);

        // Assert - read back to verify timestamp was set
        var afterAppend = DateTimeOffset.UtcNow;
        var result = await _store.ReadAsync(Query.All(), null);
        Assert.Single(result);
        Assert.NotEqual(default, result[0].Metadata.Timestamp);
        Assert.True(result[0].Metadata.Timestamp >= beforeAppend);
        Assert.True(result[0].Metadata.Timestamp <= afterAppend);
    }

    [Fact]
    public async Task AppendAsync_PreservesExistingTimestampAsync()
    {
        // Arrange
        var specificTime = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var events = new[] { CreateTestEvent("TestEvent", new TestDomainEvent { Data = "test" }) };
        events[0].Metadata = events[0].Metadata with { Timestamp = specificTime };

        // Act
        await _store.AppendAsync(events, null);

        // Assert - read back to verify timestamp was preserved
        var result = await _store.ReadAsync(Query.All(), null);
        Assert.Single(result);
        Assert.Equal(specificTime, result[0].Metadata.Timestamp);
    }

    // ========================================================================
    // AppendAsync - Validation Tests
    // ========================================================================

    [Fact]
    public async Task AppendAsync_WithNullEvents_ThrowsArgumentNullExceptionAsync()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _store.AppendAsync(null!, null));
    }

    [Fact]
    public async Task AppendAsync_WithEmptyArray_ThrowsArgumentExceptionAsync()
    {
        // Arrange
        var events = Array.Empty<NewEvent>();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _store.AppendAsync(events, null));
    }

    [Fact]
    public async Task AppendAsync_WithNullEvent_ThrowsArgumentExceptionAsync()
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
    public async Task AppendAsync_WithEmptyEventType_ThrowsArgumentExceptionAsync()
    {
        // Arrange
        var events = new[] { CreateTestEvent("", new TestDomainEvent { Data = "test" }) };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _store.AppendAsync(events, null));
        Assert.Contains("Event at index 0 has empty EventType", ex.Message);
    }

    [Fact]
    public async Task AppendAsync_WithNoContextsConfigured_ThrowsInvalidOperationExceptionAsync()
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
    public async Task AppendAsync_MultipleSequentialAppends_MaintainsContinuousSequenceAsync()
    {
        // Arrange & Act
        var batch1 = new[] { CreateTestEvent("Event1", new TestDomainEvent { Data = "1" }) };
        await _store.AppendAsync(batch1, null);

        var batch2 = new[] { CreateTestEvent("Event2", new TestDomainEvent { Data = "2" }) };
        await _store.AppendAsync(batch2, null);

        var batch3 = new[] { CreateTestEvent("Event3", new TestDomainEvent { Data = "3" }) };
        await _store.AppendAsync(batch3, null);

        // Assert - read back to verify continuous sequence
        var result = await _store.ReadAsync(Query.All(), null);
        Assert.Equal(3, result.Length);
        Assert.Equal(1, result[0].Position);
        Assert.Equal(2, result[1].Position);
        Assert.Equal(3, result[2].Position);
    }

    [Fact]
    public async Task AppendAsync_LargerBatch_AssignsCorrectPositionsAsync()
    {
        // Arrange
        var events = new NewEvent[10];
        for (int i = 0; i < 10; i++)
        {
            events[i] = CreateTestEvent($"Event{i}", new TestDomainEvent { Data = i.ToString() });
        }

        // Act
        await _store.AppendAsync(events, null);

        // Assert - read back to verify positions
        var result = await _store.ReadAsync(Query.All(), null);
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(i + 1, result[i].Position);
        }
    }

    // ========================================================================
    // AppendAsync - AppendCondition Tests
    // ========================================================================

    [Fact]
    public async Task AppendAsync_WithAfterSequencePositionCondition_SuccessAsync()
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

        // Assert - read back to verify position 3 was assigned
        var result = await _store.ReadAsync(Query.FromEventTypes("Event3"), null);
        Assert.Single(result);
        Assert.Equal(3, result[0].Position);
    }

    [Fact]
    public async Task AppendAsync_WithAfterSequencePositionCondition_FailsOnMismatchAsync()
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
    public async Task AppendAsync_WithFailIfEventsMatchCondition_SuccessWhenNoMatchAsync()
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

        // Assert - read back to verify position 2 was assigned
        var result = await _store.ReadAsync(Query.FromEventTypes("Event2"), null);
        Assert.Single(result);
        Assert.Equal(2, result[0].Position);
    }

    [Fact]
    public async Task AppendAsync_WithFailIfEventsMatchCondition_FailsWhenMatchesAsync()
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
    public async Task AppendAsync_WithTagMatchCondition_SuccessWhenNoMatchAsync()
    {
        // Arrange - Append event with different tag
        var initialEvents = new[] { CreateTestEvent("Event1", new TestDomainEvent { Data = "1" }) };
        initialEvents[0].Event = initialEvents[0].Event with { Tags = [new Tag("Status", "Completed")] };
        await _store.AppendAsync(initialEvents, null);

        // Act - Append with condition checking for different tag
        var condition = new AppendCondition
        {
            FailIfEventsMatch = Query.FromTags(new Tag("Status", "Pending"))
        };
        var newEvents = new[] { CreateTestEvent("Event2", new TestDomainEvent { Data = "2" }) };
        await _store.AppendAsync(newEvents, condition);

        // Assert - read back to verify position 2 was assigned
        var result = await _store.ReadAsync(Query.FromEventTypes("Event2"), null);
        Assert.Single(result);
        Assert.Equal(2, result[0].Position);
    }

    [Fact]
    public async Task AppendAsync_WithTagMatchCondition_FailsWhenMatchesAsync()
    {
        // Arrange - Append event with specific tag
        var initialEvents = new[] { CreateTestEvent("Event1", new TestDomainEvent { Data = "1" }) };
        initialEvents[0].Event = initialEvents[0].Event with { Tags = [new Tag("Status", "Pending")] };
        await _store.AppendAsync(initialEvents, null);

        // Act & Assert - Append with condition checking for same tag
        var condition = new AppendCondition
        {
            FailIfEventsMatch = Query.FromTags(new Tag("Status", "Pending"))
        };
        var newEvents = new[] { CreateTestEvent("Event2", new TestDomainEvent { Data = "2" }) };

        await Assert.ThrowsAsync<ConcurrencyException>(
            () => _store.AppendAsync(newEvents, condition));
    }

    [Fact]
    public async Task AppendAsync_WithBothConditions_SuccessWhenAllPassAsync()
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

        // Assert - read back to verify position 2 was assigned
        var result = await _store.ReadAsync(Query.FromEventTypes("Event2"), null);
        Assert.Single(result);
        Assert.Equal(2, result[0].Position);
    }

    // ========================================================================
    // Integration Tests
    // ========================================================================

    [Fact]
    public async Task Integration_CompleteWorkflow_AllComponentsWorkAsync()
    {
        // Arrange - Create various events with different types and tags
        var event1 = CreateTestEvent("OrderCreated", new TestDomainEvent { Data = "Order123" });
        event1.Event = event1.Event with { Tags = [new Tag("Environment", "Production"), new Tag("Region", "US-West")] };

        var event2 = CreateTestEvent("OrderShipped", new TestDomainEvent { Data = "Order123" });
        event2.Event = event2.Event with { Tags = [new Tag("Environment", "Production")] };

        var event3 = CreateTestEvent("OrderCreated", new TestDomainEvent { Data = "Order456" });
        event3.Event = event3.Event with { Tags = [new Tag("Environment", "Development")] };

        // Act - Append in batches
        await _store.AppendAsync([event1], null);
        await _store.AppendAsync([event2, event3], null);

        // Assert - read back to verify positions
        var all = await _store.ReadAsync(Query.All(), null);
        Assert.Equal(1, all[0].Position);
        Assert.Equal(2, all[1].Position);
        Assert.Equal(3, all[2].Position);

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
    public async Task EventStore_WithFlushTrue_EventsAreDurableAsync()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), $"FlushTest_{Guid.NewGuid():N}");
        var options = new OpossumOptions
        {
            RootPath = tempPath,
            FlushEventsImmediately = true // Production mode
        };
        options.UseStore("ProductionContext");

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
            TestDirectoryHelper.ForceDelete(tempPath);
        }
    }

    [Fact]
    public async Task EventStore_WithFlushFalse_EventsStillPersistedAsync()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), $"NoFlushTest_{Guid.NewGuid():N}");
        var options = new OpossumOptions
        {
            RootPath = tempPath,
            FlushEventsImmediately = false // Test mode (faster)
        };
        options.UseStore("TestContext");

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
            TestDirectoryHelper.ForceDelete(tempPath);
        }
    }

    [Fact]
    public async Task EventStore_DefaultFlushSetting_IsTrueAsync()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), $"DefaultFlushTest_{Guid.NewGuid():N}");
        var options = new OpossumOptions { RootPath = tempPath };
        // Note: NOT setting FlushEventsImmediately - should default to true
        options.UseStore("DefaultContext");

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
            TestDirectoryHelper.ForceDelete(tempPath);
        }
    }

    // ========================================================================
    // Critical Fix Tests - Descending Order & Query.All() Performance
    // ========================================================================

    [Fact]
    public async Task ReadAsync_WithDescendingOrder_ReturnsEventsInReverseOrderAsync()
    {
        // Arrange - Add 10 events
        var events = new[]
        {
            CreateTestEvent("Event1", new TestDomainEvent { Data = "1" }),
            CreateTestEvent("Event2", new TestDomainEvent { Data = "2" }),
            CreateTestEvent("Event3", new TestDomainEvent { Data = "3" }),
            CreateTestEvent("Event4", new TestDomainEvent { Data = "4" }),
            CreateTestEvent("Event5", new TestDomainEvent { Data = "5" }),
            CreateTestEvent("Event6", new TestDomainEvent { Data = "6" }),
            CreateTestEvent("Event7", new TestDomainEvent { Data = "7" }),
            CreateTestEvent("Event8", new TestDomainEvent { Data = "8" }),
            CreateTestEvent("Event9", new TestDomainEvent { Data = "9" }),
            CreateTestEvent("Event10", new TestDomainEvent { Data = "10" })
        };
        await _store.AppendAsync(events, null);

        // Act - Read in descending order
        var query = Query.All();
        var result = await _store.ReadAsync(query, [ReadOption.Descending]);

        // Assert - Events should be in reverse order
        Assert.Equal(10, result.Length);
        Assert.Equal(10, result[0].Position); // Newest first
        Assert.Equal(9, result[1].Position);
        Assert.Equal(8, result[2].Position);
        Assert.Equal(1, result[9].Position); // Oldest last

        // Verify data is correct
        Assert.Equal("10", ((TestDomainEvent)result[0].Event.Event).Data);
        Assert.Equal("1", ((TestDomainEvent)result[9].Event.Event).Data);
    }

    [Fact]
    public async Task ReadAsync_WithDescendingOrder_AndEventTypeQuery_ReturnsCorrectOrderAsync()
    {
        // Arrange - Add events with different types
        var events = new[]
        {
            CreateTestEvent("TypeA", new TestDomainEvent { Data = "A1" }),
            CreateTestEvent("TypeB", new TestDomainEvent { Data = "B1" }),
            CreateTestEvent("TypeA", new TestDomainEvent { Data = "A2" }),
            CreateTestEvent("TypeB", new TestDomainEvent { Data = "B2" }),
            CreateTestEvent("TypeA", new TestDomainEvent { Data = "A3" })
        };
        await _store.AppendAsync(events, null);

        // Act - Query TypeA in descending order
        var query = Query.FromEventTypes(["TypeA"]);
        var result = await _store.ReadAsync(query, [ReadOption.Descending]);

        // Assert - Should get TypeA events in reverse order
        Assert.Equal(3, result.Length);
        Assert.Equal(5, result[0].Position); // TypeA - A3
        Assert.Equal(3, result[1].Position); // TypeA - A2
        Assert.Equal(1, result[2].Position); // TypeA - A1

        Assert.Equal("A3", ((TestDomainEvent)result[0].Event.Event).Data);
        Assert.Equal("A2", ((TestDomainEvent)result[1].Event.Event).Data);
        Assert.Equal("A1", ((TestDomainEvent)result[2].Event.Event).Data);
    }

    [Fact]
    public async Task ReadAsync_QueryAll_HandlesLargeDatasetsAsync()
    {
        // Arrange - Add 1000 events
        var events = new NewEvent[1000];
        for (int i = 0; i < 1000; i++)
        {
            events[i] = CreateTestEvent($"Event{i}", new TestDomainEvent { Data = $"Data{i}" });
        }
        await _store.AppendAsync(events, null);

        // Act - Query all events
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var query = Query.All();
        var result = await _store.ReadAsync(query, null);
        sw.Stop();

        // Assert - All events returned
        Assert.Equal(1000, result.Length);
        Assert.Equal(1, result[0].Position);
        Assert.Equal(1000, result[999].Position);

        // Performance assertion - should be faster with batched reading
        // Note: This is a sanity check, not a precise benchmark
        Assert.True(sw.ElapsedMilliseconds < 5000,
            $"Query.All() for 1000 events took {sw.ElapsedMilliseconds}ms, expected <5000ms");
    }

    [Fact]
    public async Task ReadAsync_QueryAll_WithDescending_HandlesLargeDatasetsAsync()
    {
        // Arrange - Add 1000 events
        var events = new NewEvent[1000];
        for (int i = 0; i < 1000; i++)
        {
            events[i] = CreateTestEvent($"Event{i}", new TestDomainEvent { Data = $"Data{i}" });
        }
        await _store.AppendAsync(events, null);

        // Act - Query all events in descending order
        var query = Query.All();
        var result = await _store.ReadAsync(query, [ReadOption.Descending]);

        // Assert - All events returned in reverse order
        Assert.Equal(1000, result.Length);
        Assert.Equal(1000, result[0].Position); // Newest first
        Assert.Equal(1, result[999].Position); // Oldest last

        // Verify data integrity
        Assert.Equal("Data999", ((TestDomainEvent)result[0].Event.Event).Data);
        Assert.Equal("Data0", ((TestDomainEvent)result[999].Event.Event).Data);
    }

    [Fact]
    public async Task ReadAsync_Descending_WithSmallResultSet_WorksCorrectlyAsync()
    {
        // Arrange - Add 3 events
        var events = new[]
        {
            CreateTestEvent("Event1", new TestDomainEvent { Data = "1" }),
            CreateTestEvent("Event2", new TestDomainEvent { Data = "2" }),
            CreateTestEvent("Event3", new TestDomainEvent { Data = "3" })
        };
        await _store.AppendAsync(events, null);

        // Act
        var query = Query.All();
        var result = await _store.ReadAsync(query, [ReadOption.Descending]);

        // Assert
        Assert.Equal(3, result.Length);
        Assert.Equal(3, result[0].Position);
        Assert.Equal(2, result[1].Position);
        Assert.Equal(1, result[2].Position);
    }

    [Fact]
    public async Task ReadAsync_Descending_WithEmptyResult_ReturnsEmptyArrayAsync()
    {
        // Arrange - No events

        // Act
        var query = Query.FromEventTypes(["NonExistent"]);
        var result = await _store.ReadAsync(query, [ReadOption.Descending]);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadAsync_Descending_WithTagQuery_ReturnsCorrectOrderAsync()
    {
        // Arrange - Add events with tags
        var event1 = CreateTestEvent("Event1", new TestDomainEvent { Data = "1" });
        event1.Event = event1.Event with { Tags = [new Tag("Priority", "High")] };

        var event2 = CreateTestEvent("Event2", new TestDomainEvent { Data = "2" });
        event2.Event = event2.Event with { Tags = [new Tag("Priority", "Low")] };

        var event3 = CreateTestEvent("Event3", new TestDomainEvent { Data = "3" });
        event3.Event = event3.Event with { Tags = [new Tag("Priority", "High")] };

        await _store.AppendAsync([event1, event2, event3], null);

        // Act - Query High priority in descending order
        var query = Query.FromTags([new Tag("Priority", "High")]);
        var result = await _store.ReadAsync(query, [ReadOption.Descending]);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal(3, result[0].Position); // Event3 (newest High)
        Assert.Equal(1, result[1].Position); // Event1 (oldest High)
    }

    // ========================================================================
    // Helper Methods
    // ========================================================================

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
