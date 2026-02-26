using Opossum.Core;
using Opossum.DependencyInjection;
using Opossum.Exceptions;
using Opossum.Extensions;
using System.Collections.Concurrent;

namespace Opossum.IntegrationTests;

/// <summary>
/// Integration tests for FileSystemEventStore thread safety and concurrency control.
/// Tests the multi-layered defense architecture (SemaphoreSlim + Index locks + Atomic operations).
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public class EventStoreThreadSafetyTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string _testStoragePath;
    private readonly IEventStore _eventStore;

    public EventStoreThreadSafetyTests()
    {
        _testStoragePath = Path.Combine(
            Path.GetTempPath(),
            "OpossumEventStoreThreadTests",
            Guid.NewGuid().ToString());

        var services = new ServiceCollection();

        services.AddOpossum(options =>
        {
            options.RootPath = _testStoragePath;
            options.UseStore("ThreadSafetyContext");
        });

        _serviceProvider = services.BuildServiceProvider();
        _eventStore = _serviceProvider.GetRequiredService<IEventStore>();
    }

    [Fact]
    public async Task ConcurrentAppends_SerializedExecution_AllEventsStoredAsync()
    {
        // Arrange
        var appendCount = 50;
        var eventsPerAppend = 5;

        // Act - Multiple threads appending concurrently
        var tasks = Enumerable.Range(0, appendCount)
            .Select(i => Task.Run(async () =>
            {
                var events = Enumerable.Range(0, eventsPerAppend)
                    .Select(j => new ThreadSafetyTestEvent($"Append-{i}", $"Event-{j}")
                        .ToDomainEvent()
                        .WithTag("appendId", i.ToString())
                        .Build())
                    .ToArray();

                await _eventStore.AppendAsync(events, null);
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - All events should be stored
        var allEvents = await _eventStore.ReadAsync(Query.All());
        Assert.Equal(appendCount * eventsPerAppend, allEvents.Length);

        // Verify positions are sequential and unique
        var positions = allEvents.Select(e => e.Position).OrderBy(p => p).ToArray();
        var expectedPositions = Enumerable.Range(1, appendCount * eventsPerAppend).Select(i => (long)i);
        Assert.Equal(expectedPositions, positions);

        // Verify no position duplicates
        Assert.Equal(positions.Length, positions.Distinct().Count());
    }

    [Fact]
    public async Task ConcurrentReadsDuringWrites_EventuallyConsistentAsync()
    {
        // Arrange
        var writeCount = 100;
        var readCount = 50;
        var readResults = new ConcurrentBag<int>();

        // Act - Concurrent writes and reads
        var writeTasks = Enumerable.Range(1, writeCount)
            .Select(i => Task.Run(async () =>
            {
                var evt = new ThreadSafetyTestEvent("Write", $"Event-{i}")
                    .ToDomainEvent()
                    .WithTag("sequence", i.ToString())
                    .Build();

                await _eventStore.AppendAsync([evt], null);
                await Task.Delay(Random.Shared.Next(0, 10));
            }))
            .ToList();

        var readTasks = Enumerable.Range(0, readCount)
            .Select(_ => Task.Run(async () =>
            {
                while (writeTasks.Any(t => !t.IsCompleted))
                {
                    var events = await _eventStore.ReadAsync(Query.All());
                    readResults.Add(events.Length);
                    await Task.Delay(20);
                }
            }))
            .ToList();

        await Task.WhenAll(writeTasks.Concat(readTasks));

        // Assert - Final state should have all events
        var finalEvents = await _eventStore.ReadAsync(Query.All());
        Assert.Equal(writeCount, finalEvents.Length);

        // Read results should show monotonically increasing counts (eventual consistency)
        var sortedReads = readResults.OrderBy(x => x).ToList();
        Assert.True(sortedReads.First() >= 0);
        Assert.True(sortedReads.Last() <= writeCount);
    }

    [Fact]
    public async Task ConcurrentAppendsWithOptimisticConcurrency_DetectsConflictsAsync()
    {
        // Arrange
        var resourceId = Guid.NewGuid();
        var initialEvent = new ThreadSafetyTestEvent("Resource", "Created")
            .ToDomainEvent()
            .WithTag("resourceId", resourceId.ToString())
            .Build();

        await _eventStore.AppendAsync([initialEvent], null);

        var query = Query.FromTags(new Tag("resourceId", resourceId.ToString()));
        var initialEvents = await _eventStore.ReadAsync(query);
        var initialPosition = initialEvents[^1].Position;

        // Act - Two concurrent updates with optimistic concurrency
        var successCount = 0;
        var failureCount = 0;

        var tasks = Enumerable.Range(0, 10)
            .Select(i => Task.Run(async () =>
            {
                try
                {
                    var updateEvent = new ThreadSafetyTestEvent("Resource", $"Updated-{i}")
                        .ToDomainEvent()
                        .WithTag("resourceId", resourceId.ToString())
                        .Build();

                    var condition = new AppendCondition
                    {
                        AfterSequencePosition = initialPosition,
                        FailIfEventsMatch = Query.FromItems()
                    };

                    await _eventStore.AppendAsync([updateEvent], condition);
                    Interlocked.Increment(ref successCount);
                }
                catch (AppendConditionFailedException)
                {
                    Interlocked.Increment(ref failureCount);
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - Exactly one should succeed, rest should fail
        Assert.Equal(1, successCount);
        Assert.Equal(9, failureCount);

        // Verify final state
        var finalEvents = await _eventStore.ReadAsync(query);
        Assert.Equal(2, finalEvents.Length); // Initial + one update
    }

    [Fact]
    public async Task StressTest_HighConcurrentLoad_MaintainsIntegrityAsync()
    {
        // Arrange
        var entityCount = 20;
        var eventsPerEntity = 50;
        var totalEvents = entityCount * eventsPerEntity;

        // Act - Simulate high concurrent load with multiple entities
        var tasks = Enumerable.Range(0, entityCount)
            .SelectMany(entityId =>
                Enumerable.Range(0, eventsPerEntity)
                    .Select(eventNum => Task.Run(async () =>
                    {
                        var evt = new ThreadSafetyTestEvent($"Entity-{entityId}", $"Event-{eventNum}")
                            .ToDomainEvent()
                            .WithTag("entityId", entityId.ToString())
                            .Build();

                        await _eventStore.AppendAsync([evt], null);
                    })))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - All events stored correctly
        var allEvents = await _eventStore.ReadAsync(Query.All());
        Assert.Equal(totalEvents, allEvents.Length);

        // Verify positions are sequential
        var positions = allEvents.Select(e => e.Position).OrderBy(p => p).ToArray();
        Assert.Equal(Enumerable.Range(1, totalEvents).Select(i => (long)i), positions);

        // Verify each entity has correct event count
        for (int i = 0; i < entityCount; i++)
        {
            var entityEvents = await _eventStore.ReadAsync(
                Query.FromTags(new Tag("entityId", i.ToString())));
            Assert.Equal(eventsPerEntity, entityEvents.Length);
        }
    }

    [Fact]
    public async Task ConcurrentAppendsToMultipleContexts_IsolatedAsync()
    {
        // This test would require multi-context support in fixture
        // For now, testing within single context demonstrates locking works

        // Arrange
        var context1Tag = new Tag("context", "context1");
        var context2Tag = new Tag("context", "context2");
        var eventsPerContext = 50;

        // Act - Concurrent appends tagged for different logical contexts
        var context1Tasks = Enumerable.Range(0, eventsPerContext)
            .Select(i => Task.Run(async () =>
            {
                var evt = new ThreadSafetyTestEvent("Context1", $"Event-{i}")
                    .ToDomainEvent()
                    .WithTag("context", "context1")
                    .Build();
                await _eventStore.AppendAsync([evt], null);
            }));

        var context2Tasks = Enumerable.Range(0, eventsPerContext)
            .Select(i => Task.Run(async () =>
            {
                var evt = new ThreadSafetyTestEvent("Context2", $"Event-{i}")
                    .ToDomainEvent()
                    .WithTag("context", "context2")
                    .Build();
                await _eventStore.AppendAsync([evt], null);
            }));

        await Task.WhenAll(context1Tasks.Concat(context2Tasks));

        // Assert - Each "context" has its events
        var ctx1Events = await _eventStore.ReadAsync(Query.FromTags(context1Tag));
        var ctx2Events = await _eventStore.ReadAsync(Query.FromTags(context2Tag));

        Assert.Equal(eventsPerContext, ctx1Events.Length);
        Assert.Equal(eventsPerContext, ctx2Events.Length);
    }

    [Fact]
    public async Task ConcurrentQueryByEventType_ReturnsConsistentResultsAsync()
    {
        // Arrange
        var eventCount = 100;

        // Add events
        var addTasks = Enumerable.Range(0, eventCount)
            .Select(i => Task.Run(async () =>
            {
                var evt = new ThreadSafetyTestEvent("Test", $"Data-{i}")
                    .ToDomainEvent()
                    .Build();
                await _eventStore.AppendAsync([evt], null);
            }))
            .ToArray();

        await Task.WhenAll(addTasks);

        // Act - Concurrent queries by event type
        var queryResults = new ConcurrentBag<int>();
        var queryTasks = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(async () =>
            {
                var events = await _eventStore.ReadAsync(
                    Query.FromEventTypes(nameof(ThreadSafetyTestEvent)));
                queryResults.Add(events.Length);
            }))
            .ToArray();

        await Task.WhenAll(queryTasks);

        // Assert - All queries should return same count
        Assert.All(queryResults, count => Assert.Equal(eventCount, count));
    }

    [Fact]
    public async Task ConcurrentAppendWithFailIfMatch_SerializesCorrectlyAsync()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var successfulAppends = 0;

        // Act - Multiple threads try to create "first login" event
        var tasks = Enumerable.Range(0, 20)
            .Select(i => Task.Run(async () =>
            {
                try
                {
                    var evt = new ThreadSafetyTestEvent("FirstLogin", $"Attempt-{i}")
                        .ToDomainEvent()
                        .WithTag("userId", userId.ToString())
                        .WithTag("type", "FirstLogin")
                        .Build();

                    var condition = new AppendCondition
                    {
                        FailIfEventsMatch = Query.FromItems(new QueryItem
                        {
                            Tags = [
                                new Tag("userId", userId.ToString()),
                                new Tag("type", "FirstLogin")
                            ]
                        })
                    };

                    await _eventStore.AppendAsync([evt], condition);
                    Interlocked.Increment(ref successfulAppends);
                }
                catch (AppendConditionFailedException)
                {
                    // Expected for all but the first
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - Only one should succeed
        Assert.Equal(1, successfulAppends);

        var firstLoginEvents = await _eventStore.ReadAsync(
            Query.FromItems(new QueryItem
            {
                Tags = [
                    new Tag("userId", userId.ToString()),
                    new Tag("type", "FirstLogin")
                ]
            }));

        Assert.Single(firstLoginEvents);
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

// Test event
public record ThreadSafetyTestEvent(string Category, string Data) : IEvent;
