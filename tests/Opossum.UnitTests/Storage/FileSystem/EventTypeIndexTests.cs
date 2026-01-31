using Opossum.Core;
using Opossum.Storage.FileSystem;

namespace Opossum.UnitTests.Storage.FileSystem;

public class EventTypeIndexTests : IDisposable
{
    private readonly EventTypeIndex _index;
    private readonly string _tempIndexPath;

    public EventTypeIndexTests()
    {
        _index = new EventTypeIndex();
        _tempIndexPath = Path.Combine(Path.GetTempPath(), $"EventTypeIndexTests_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempIndexPath))
        {
            Directory.Delete(_tempIndexPath, recursive: true);
        }
    }

    // ========================================================================
    // AddPositionAsync Tests
    // ========================================================================

    [Fact]
    public async Task AddPositionAsync_CreatesIndexFile()
    {
        // Act
        await _index.AddPositionAsync(_tempIndexPath, "TestEvent", 1);

        // Assert
        Assert.True(_index.IndexExists(_tempIndexPath, "TestEvent"));
    }

    [Fact]
    public async Task AddPositionAsync_CreatesIndexDirectory()
    {
        // Act
        await _index.AddPositionAsync(_tempIndexPath, "TestEvent", 1);

        // Assert
        var eventTypeDir = Path.Combine(_tempIndexPath, "EventType");
        Assert.True(Directory.Exists(eventTypeDir));
    }

    [Fact]
    public async Task AddPositionAsync_AddsPosition()
    {
        // Act
        await _index.AddPositionAsync(_tempIndexPath, "TestEvent", 1);

        // Assert
        var positions = await _index.GetPositionsAsync(_tempIndexPath, "TestEvent");
        Assert.Single(positions);
        Assert.Equal(1, positions[0]);
    }

    [Fact]
    public async Task AddPositionAsync_AddsMultiplePositions()
    {
        // Act
        await _index.AddPositionAsync(_tempIndexPath, "TestEvent", 1);
        await _index.AddPositionAsync(_tempIndexPath, "TestEvent", 5);
        await _index.AddPositionAsync(_tempIndexPath, "TestEvent", 3);

        // Assert
        var positions = await _index.GetPositionsAsync(_tempIndexPath, "TestEvent");
        Assert.Equal(3, positions.Length);
        Assert.Equal([1, 3, 5], positions); // Should be sorted
    }

    [Fact]
    public async Task AddPositionAsync_IgnoresDuplicatePositions()
    {
        // Act
        await _index.AddPositionAsync(_tempIndexPath, "TestEvent", 1);
        await _index.AddPositionAsync(_tempIndexPath, "TestEvent", 1);
        await _index.AddPositionAsync(_tempIndexPath, "TestEvent", 1);

        // Assert
        var positions = await _index.GetPositionsAsync(_tempIndexPath, "TestEvent");
        Assert.Single(positions);
        Assert.Equal(1, positions[0]);
    }

    [Fact]
    public async Task AddPositionAsync_WithNullIndexPath_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _index.AddPositionAsync(null!, "TestEvent", 1));
    }

    [Fact]
    public async Task AddPositionAsync_WithNullEventType_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _index.AddPositionAsync(_tempIndexPath, null!, 1));
    }

    [Fact]
    public async Task AddPositionAsync_WithEmptyEventType_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _index.AddPositionAsync(_tempIndexPath, "", 1));
    }

    [Fact]
    public async Task AddPositionAsync_WithWhitespaceEventType_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _index.AddPositionAsync(_tempIndexPath, "   ", 1));
    }

    [Fact]
    public async Task AddPositionAsync_WithZeroPosition_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _index.AddPositionAsync(_tempIndexPath, "TestEvent", 0));
    }

    [Fact]
    public async Task AddPositionAsync_WithNegativePosition_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _index.AddPositionAsync(_tempIndexPath, "TestEvent", -1));
    }

    // ========================================================================
    // GetPositionsAsync Tests
    // ========================================================================

    [Fact]
    public async Task GetPositionsAsync_WithNonExistentIndex_ReturnsEmptyArray()
    {
        // Act
        var positions = await _index.GetPositionsAsync(_tempIndexPath, "NonExistent");

        // Assert
        Assert.NotNull(positions);
        Assert.Empty(positions);
    }

    [Fact]
    public async Task GetPositionsAsync_ReturnsPositionsInSortedOrder()
    {
        // Arrange
        await _index.AddPositionAsync(_tempIndexPath, "TestEvent", 10);
        await _index.AddPositionAsync(_tempIndexPath, "TestEvent", 2);
        await _index.AddPositionAsync(_tempIndexPath, "TestEvent", 7);

        // Act
        var positions = await _index.GetPositionsAsync(_tempIndexPath, "TestEvent");

        // Assert
        Assert.Equal([2, 7, 10], positions);
    }

    [Fact]
    public async Task GetPositionsAsync_WithMultipleEventTypes_ReturnsCorrectPositions()
    {
        // Arrange
        await _index.AddPositionAsync(_tempIndexPath, "EventA", 1);
        await _index.AddPositionAsync(_tempIndexPath, "EventB", 2);
        await _index.AddPositionAsync(_tempIndexPath, "EventA", 3);

        // Act
        var positionsA = await _index.GetPositionsAsync(_tempIndexPath, "EventA");
        var positionsB = await _index.GetPositionsAsync(_tempIndexPath, "EventB");

        // Assert
        Assert.Equal([1, 3], positionsA);
        Assert.Equal([2], positionsB);
    }

    [Fact]
    public async Task GetPositionsAsync_WithNullIndexPath_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _index.GetPositionsAsync(null!, "TestEvent"));
    }

    [Fact]
    public async Task GetPositionsAsync_WithNullEventType_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _index.GetPositionsAsync(_tempIndexPath, null!));
    }

    [Fact]
    public async Task GetPositionsAsync_WithEmptyEventType_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _index.GetPositionsAsync(_tempIndexPath, ""));
    }

    // ========================================================================
    // IndexExists Tests
    // ========================================================================

    [Fact]
    public async Task IndexExists_ReturnsTrueForExistingIndex()
    {
        // Arrange
        await _index.AddPositionAsync(_tempIndexPath, "TestEvent", 1);

        // Act
        var exists = _index.IndexExists(_tempIndexPath, "TestEvent");

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public void IndexExists_ReturnsFalseForNonExistentIndex()
    {
        // Act
        var exists = _index.IndexExists(_tempIndexPath, "NonExistent");

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public void IndexExists_WithNullIndexPath_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _index.IndexExists(null!, "TestEvent"));
    }

    [Fact]
    public void IndexExists_WithNullEventType_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _index.IndexExists(_tempIndexPath, null!));
    }

    // ========================================================================
    // Safe File Name Tests
    // ========================================================================

    [Fact]
    public async Task AddPositionAsync_WithSpecialCharactersInEventType_CreatesSafeFileName()
    {
        // Arrange
        var eventType = "Test/Event:With*Special?Characters";

        // Act
        await _index.AddPositionAsync(_tempIndexPath, eventType, 1);

        // Assert
        Assert.True(_index.IndexExists(_tempIndexPath, eventType));
        var positions = await _index.GetPositionsAsync(_tempIndexPath, eventType);
        Assert.Single(positions);
    }

    [Fact]
    public async Task AddPositionAsync_WithDotInEventType_CreatesSafeFileName()
    {
        // Arrange
        var eventType = "Namespace.Event.Created";

        // Act
        await _index.AddPositionAsync(_tempIndexPath, eventType, 1);

        // Assert
        Assert.True(_index.IndexExists(_tempIndexPath, eventType));
    }

    // ========================================================================
    // Corruption Handling Tests
    // ========================================================================

    [Fact]
    public async Task GetPositionsAsync_WithCorruptedIndexFile_ReturnsEmptyArray()
    {
        // Arrange
        var eventTypeDir = Path.Combine(_tempIndexPath, "eventtype");
        Directory.CreateDirectory(eventTypeDir);
        var indexFile = Path.Combine(eventTypeDir, "TestEvent.json");
        await File.WriteAllTextAsync(indexFile, "{ invalid json }");

        // Act
        var positions = await _index.GetPositionsAsync(_tempIndexPath, "TestEvent");

        // Assert
        Assert.NotNull(positions);
        Assert.Empty(positions);
    }

    [Fact]
    public async Task AddPositionAsync_AfterCorruption_RebuildsIndex()
    {
        // Arrange
        var eventTypeDir = Path.Combine(_tempIndexPath, "eventtype");
        Directory.CreateDirectory(eventTypeDir);
        var indexFile = Path.Combine(eventTypeDir, "TestEvent.json");
        await File.WriteAllTextAsync(indexFile, "{ invalid json }");

        // Act - Add new position, should rebuild index
        await _index.AddPositionAsync(_tempIndexPath, "TestEvent", 5);

        // Assert
        var positions = await _index.GetPositionsAsync(_tempIndexPath, "TestEvent");
        Assert.Single(positions);
        Assert.Equal(5, positions[0]);
    }

    // ========================================================================
    // Concurrent Access Tests
    // ========================================================================

    [Fact]
    public async Task AddPositionAsync_MultipleSequentialWrites_AllPositionsAdded()
    {
        // Arrange
        var expectedPositions = Enumerable.Range(1, 20).Select(x => (long)x).ToArray();

        // Act - Add positions sequentially
        foreach (var position in expectedPositions)
        {
            await _index.AddPositionAsync(_tempIndexPath, "TestEvent", position);
        }

        // Assert
        var positions = await _index.GetPositionsAsync(_tempIndexPath, "TestEvent");
        Assert.Equal(20, positions.Length);
        Assert.Equal(expectedPositions, positions);
    }
}
