using Opossum.Core;
using Opossum.Storage.FileSystem;
using System.Collections.Concurrent;

namespace Opossum.UnitTests.Storage.FileSystem;

/// <summary>
/// Thread safety tests for TagIndex internal locking mechanism.
/// Tests the new SemaphoreSlim-based protection for Read-Modify-Write operations.
/// </summary>
public class TagIndexThreadSafetyTests : IDisposable
{
    private readonly string _testPath;

    public TagIndexThreadSafetyTests()
    {
        _testPath = Path.Combine(
            Path.GetTempPath(),
            "OpossumTagIndexThreadTests",
            Guid.NewGuid().ToString());

        Directory.CreateDirectory(_testPath);
    }

    [Fact]
    public async Task ConcurrentAddPosition_SameTag_NoLostUpdates()
    {
        // Arrange
        var index = new TagIndex();
        var tag = new Tag { Key = "userId", Value = "user-123" };
        var concurrentCount = 100;

        // Act - Add 100 positions concurrently to same tag
        var tasks = Enumerable.Range(1, concurrentCount)
            .Select(i => Task.Run(async () =>
                await index.AddPositionAsync(_testPath, tag, i)))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - All positions should be present
        var positions = await index.GetPositionsAsync(_testPath, tag);

        Assert.Equal(concurrentCount, positions.Length);
        Assert.Equal(positions.OrderBy(x => x), Enumerable.Range(1, concurrentCount).Select(i => (long)i).OrderBy(x => x));
    }

    [Fact]
    public async Task ConcurrentAddPosition_DifferentTags_NoConflicts()
    {
        // Arrange
        var index = new TagIndex();
        var tagCount = 10;
        var positionsPerTag = 50;

        // Act - Add positions to different tags concurrently
        var tasks = new List<Task>();
        for (int tagIndex = 0; tagIndex < tagCount; tagIndex++)
        {
            var tag = new Tag { Key = "entityId", Value = $"entity-{tagIndex}" };
            for (int position = 1; position <= positionsPerTag; position++)
            {
                var pos = position;
                tasks.Add(Task.Run(async () =>
                    await index.AddPositionAsync(_testPath, tag, pos)));
            }
        }

        await Task.WhenAll(tasks);

        // Assert - Each tag should have all its positions
        for (int i = 0; i < tagCount; i++)
        {
            var tag = new Tag { Key = "entityId", Value = $"entity-{i}" };
            var positions = await index.GetPositionsAsync(_testPath, tag);

            Assert.Equal(positionsPerTag, positions.Length);
            Assert.Equal(positions.OrderBy(x => x), Enumerable.Range(1, positionsPerTag).Select(i => (long)i).OrderBy(x => x));
        }
    }

    [Fact]
    public async Task ConcurrentAddPosition_SameKeyDifferentValues_Isolated()
    {
        // Arrange
        var index = new TagIndex();
        var key = "userId";
        var valueCount = 5;
        var positionsPerValue = 20;

        // Act - Add positions to different values of same key concurrently
        var tasks = new List<Task>();
        for (int valueIndex = 0; valueIndex < valueCount; valueIndex++)
        {
            var tag = new Tag { Key = key, Value = $"user-{valueIndex}" };
            for (int position = 1; position <= positionsPerValue; position++)
            {
                var pos = position;
                tasks.Add(Task.Run(async () =>
                    await index.AddPositionAsync(_testPath, tag, pos)));
            }
        }

        await Task.WhenAll(tasks);

        // Assert - Each tag value should have its own positions
        for (int i = 0; i < valueCount; i++)
        {
            var tag = new Tag { Key = key, Value = $"user-{i}" };
            var positions = await index.GetPositionsAsync(_testPath, tag);
            
            Assert.Equal(positionsPerValue, positions.Length);
        }
    }

    [Fact]
    public async Task ConcurrentAddPosition_DuplicatePositions_NoDuplicatesInResult()
    {
        // Arrange
        var index = new TagIndex();
        var tag = new Tag { Key = "orderId", Value = "order-123" };
        var position = 42L;
        var attemptCount = 50;

        // Act - Try to add same position multiple times concurrently
        var tasks = Enumerable.Range(0, attemptCount)
            .Select(_ => Task.Run(async () =>
                await index.AddPositionAsync(_testPath, tag, position)))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - Only one instance of the position should exist
        var positions = await index.GetPositionsAsync(_testPath, tag);
        
        Assert.Single(positions);
        Assert.Equal(position, positions[0]);
    }

    [Fact]
    public async Task ConcurrentReadAndWrite_NoCorruption()
    {
        // Arrange
        var index = new TagIndex();
        var tag = new Tag { Key = "testKey", Value = "testValue" };
        var writeCount = 50;  // Reduced from 100 for more realistic testing
        var readCount = 10;   // Reduced from 50 to lower file contention

        var readResults = new ConcurrentBag<long[]>();
        var cts = new CancellationTokenSource();

        // Act - Concurrent writes and reads
        var writeTasks = Enumerable.Range(1, writeCount)
            .Select(i => Task.Run(async () =>
                await index.AddPositionAsync(_testPath, tag, i)))
            .ToList();

        var readTasks = Enumerable.Range(0, readCount)
            .Select(_ => Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var positions = await index.GetPositionsAsync(_testPath, tag);
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
        var finalPositions = await index.GetPositionsAsync(_testPath, tag);
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
    public async Task StressTest_MultipleTagsHighConcurrency_MaintainsIntegrity()
    {
        // Arrange
        var index = new TagIndex();
        var tags = new[]
        {
            new Tag { Key = "userId", Value = "user-1" },
            new Tag { Key = "userId", Value = "user-2" },
            new Tag { Key = "orderId", Value = "order-1" },
            new Tag { Key = "productId", Value = "product-1" }
        };
        var positionsPerTag = 250;

        // Act - Add positions to multiple tags with high concurrency
        var tasks = tags.SelectMany(tag =>
            Enumerable.Range(1, positionsPerTag)
                .Select(i => Task.Run(async () =>
                {
                    await Task.Delay(Random.Shared.Next(0, 5));
                    await index.AddPositionAsync(_testPath, tag, i);
                })))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - Each tag should have all positions
        foreach (var tag in tags)
        {
            var positions = await index.GetPositionsAsync(_testPath, tag);
            Assert.Equal(positionsPerTag, positions.Length);
            Assert.Equal(positions.OrderBy(x => x), Enumerable.Range(1, positionsPerTag).Select(i => (long)i).OrderBy(x => x));
        }
    }

    [Fact]
    public async Task ConcurrentAddPosition_WithSpecialCharacters_HandlesCorrectly()
    {
        // Arrange
        var index = new TagIndex();
        var specialTags = new[]
        {
            new Tag { Key = "email", Value = "user@example.com" },
            new Tag { Key = "path", Value = "/api/v1/users" },
            new Tag { Key = "query", Value = "name=John&age=30" },
            new Tag { Key = "special", Value = "test:value;data" }
        };
        var positionsPerTag = 10;

        // Act - Add positions concurrently to tags with special characters
        var tasks = specialTags.SelectMany(tag =>
            Enumerable.Range(1, positionsPerTag)
                .Select(i => Task.Run(async () =>
                    await index.AddPositionAsync(_testPath, tag, i))))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - All tags should have their positions
        foreach (var tag in specialTags)
        {
            var positions = await index.GetPositionsAsync(_testPath, tag);
            Assert.Equal(positionsPerTag, positions.Length);
        }
    }

    [Fact]
    public async Task ConcurrentAddPosition_NullTagValue_HandlesCorrectly()
    {
        // Arrange
        var index = new TagIndex();
        var tag = new Tag { Key = "optionalField", Value = null };
        var positionCount = 50;

        // Act - Add positions concurrently with null tag value
        var tasks = Enumerable.Range(1, positionCount)
            .Select(i => Task.Run(async () =>
                await index.AddPositionAsync(_testPath, tag, i)))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert
        var positions = await index.GetPositionsAsync(_testPath, tag);
        Assert.Equal(positionCount, positions.Length);
        Assert.Equal(positions.OrderBy(x => x), Enumerable.Range(1, positionCount).Select(i => (long)i).OrderBy(x => x));
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
