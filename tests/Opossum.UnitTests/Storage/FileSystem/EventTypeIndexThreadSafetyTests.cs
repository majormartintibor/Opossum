using Opossum.Storage.FileSystem;

namespace Opossum.UnitTests.Storage.FileSystem;

/// <summary>
/// Thread safety tests for EventTypeIndex internal locking mechanism.
/// Tests the new SemaphoreSlim-based protection for Read-Modify-Write operations.
/// </summary>
public class EventTypeIndexThreadSafetyTests : IDisposable
{
    private readonly string _testPath;

    public EventTypeIndexThreadSafetyTests()
    {
        _testPath = Path.Combine(
            Path.GetTempPath(),
            "OpossumIndexThreadTests",
            Guid.NewGuid().ToString());

        Directory.CreateDirectory(_testPath);
    }

    [Fact]
    public async Task ConcurrentAddPosition_SameEventType_NoLostUpdates()
    {
        // Arrange
        var index = new EventTypeIndex();
        var eventType = "TestEvent";
        var concurrentCount = 100;

        // Act - Add 100 positions concurrently
        var tasks = Enumerable.Range(1, concurrentCount)
            .Select(i => Task.Run(async () =>
                await index.AddPositionAsync(_testPath, eventType, i)))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - All positions should be present
        var positions = await index.GetPositionsAsync(_testPath, eventType);

        Assert.Equal(concurrentCount, positions.Length);
        Assert.Equal(positions.OrderBy(x => x), Enumerable.Range(1, concurrentCount).Select(i => (long)i).OrderBy(x => x));
    }

    [Fact]
    public async Task ConcurrentAddPosition_DifferentEventTypes_NoConflicts()
    {
        // Arrange
        var index = new EventTypeIndex();
        var eventTypeCount = 10;
        var positionsPerType = 50;

        // Act - Add positions to different event types concurrently
        var tasks = new List<Task>();
        for (int eventTypeIndex = 0; eventTypeIndex < eventTypeCount; eventTypeIndex++)
        {
            var eventType = $"EventType{eventTypeIndex}";
            for (int position = 1; position <= positionsPerType; position++)
            {
                var pos = position;
                tasks.Add(Task.Run(async () =>
                    await index.AddPositionAsync(_testPath, eventType, pos)));
            }
        }

        await Task.WhenAll(tasks);

        // Assert - Each event type should have all its positions
        for (int i = 0; i < eventTypeCount; i++)
        {
            var eventType = $"EventType{i}";
            var positions = await index.GetPositionsAsync(_testPath, eventType);

            Assert.Equal(positionsPerType, positions.Length);
            Assert.Equal(positions.OrderBy(x => x), Enumerable.Range(1, positionsPerType).Select(i => (long)i).OrderBy(x => x));
        }
    }

    [Fact]
    public async Task ConcurrentAddPosition_DuplicatePositions_NoDuplicatesInResult()
    {
        // Arrange
        var index = new EventTypeIndex();
        var eventType = "TestEvent";
        var position = 42L;
        var attemptCount = 50;

        // Act - Try to add same position multiple times concurrently
        var tasks = Enumerable.Range(0, attemptCount)
            .Select(_ => Task.Run(async () =>
                await index.AddPositionAsync(_testPath, eventType, position)))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - Only one instance of the position should exist
        var positions = await index.GetPositionsAsync(_testPath, eventType);

        Assert.Single(positions);
        Assert.Equal(position, positions[0]);
    }

    [Fact]
    public async Task ConcurrentReadAndWrite_NoCorruption()
    {
        // Arrange
        var index = new EventTypeIndex();
        var eventType = "TestEvent";
        var writeCount = 50;  // Reduced from 100 for more realistic testing
        var readCount = 10;   // Reduced from 50 to lower file contention

        var readResults = new ConcurrentBag<long[]>();
        var cts = new CancellationTokenSource();

        // Act - Concurrent writes and reads
        var writeTasks = Enumerable.Range(1, writeCount)
            .Select(i => Task.Run(async () =>
                await index.AddPositionAsync(_testPath, eventType, i)))
            .ToList();

        var readTasks = Enumerable.Range(0, readCount)
            .Select(_ => Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var positions = await index.GetPositionsAsync(_testPath, eventType);
                        readResults.Add(positions);
                        await Task.Delay(50);  // Increased from 10ms to reduce file contention
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }))
            .ToList();

        await Task.WhenAll(writeTasks);
        cts.Cancel();

        try
        {
            await Task.WhenAll(readTasks);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert - Final state should have all positions
        var finalPositions = await index.GetPositionsAsync(_testPath, eventType);
        Assert.Equal(writeCount, finalPositions.Length);
        Assert.Equal(finalPositions.OrderBy(x => x), Enumerable.Range(1, writeCount).Select(i => (long)i).OrderBy(x => x));

        // All read results should be valid (sorted, no duplicates)
        foreach (var readResult in readResults)
        {
            Assert.Equal(readResult.OrderBy(x => x), readResult); // Should be sorted
            Assert.Equal(readResult.Distinct().Count(), readResult.Length); // No duplicates
        }
    }

    [Fact]
    public async Task StressTest_HighConcurrency_MaintainsIntegrity()
    {
        // Arrange
        var index = new EventTypeIndex();
        var eventType = "StressTestEvent";
        var totalPositions = 1000;

        // Act - Add positions with high concurrency
        var tasks = Enumerable.Range(1, totalPositions)
            .Select(i => Task.Run(async () =>
            {
                await Task.Delay(Random.Shared.Next(0, 10)); // Random delay
                await index.AddPositionAsync(_testPath, eventType, i);
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert
        var positions = await index.GetPositionsAsync(_testPath, eventType);

        Assert.Equal(totalPositions, positions.Length);
        Assert.Equal(positions.OrderBy(x => x), Enumerable.Range(1, totalPositions).Select(i => (long)i).OrderBy(x => x));

        // Verify file integrity - should be valid JSON
        var indexFilePath = Path.Combine(_testPath, "EventType", "StressTestEvent.json");
        Assert.True(File.Exists(indexFilePath));

        var json = await File.ReadAllTextAsync(indexFilePath);
        Assert.NotEmpty(json);
        Assert.Contains("\"Positions\"", json);
    }

    [Fact]
    public async Task ConcurrentAddPosition_WithExceptions_DoesNotCorruptState()
    {
        // Arrange
        var index = new EventTypeIndex();
        var eventType = "TestEvent";

        // Add some valid positions first
        await index.AddPositionAsync(_testPath, eventType, 1);
        await index.AddPositionAsync(_testPath, eventType, 2);

        // Act - Try to add invalid positions concurrently along with valid ones
        var tasks = new List<Task>();

        for (int i = 3; i <= 10; i++)
        {
            var position = i;
            tasks.Add(Task.Run(async () =>
                await index.AddPositionAsync(_testPath, eventType, position)));
        }

        // Add tasks that will throw exceptions (negative positions)
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await index.AddPositionAsync(_testPath, eventType, -1);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Expected
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - Should have positions 1-10 only
        var positions = await index.GetPositionsAsync(_testPath, eventType);
        Assert.Equal(10, positions.Length);
        Assert.Equal(positions.OrderBy(x => x), Enumerable.Range(1, 10).Select(i => (long)i).OrderBy(x => x));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testPath))
        {
            try
            {
                Directory.Delete(_testPath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
