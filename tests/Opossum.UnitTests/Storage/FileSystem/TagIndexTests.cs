using Opossum.Core;
using Opossum.Storage.FileSystem;

namespace Opossum.UnitTests.Storage.FileSystem;

public class TagIndexTests : IDisposable
{
    private readonly TagIndex _index;
    private readonly string _tempIndexPath;

    public TagIndexTests()
    {
        _index = new TagIndex();
        _tempIndexPath = Path.Combine(Path.GetTempPath(), $"TagIndexTests_{Guid.NewGuid():N}");
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
        // Arrange
        var tag = new Tag { Key = "Environment", Value = "Production" };

        // Act
        await _index.AddPositionAsync(_tempIndexPath, tag, 1);

        // Assert
        Assert.True(_index.IndexExists(_tempIndexPath, tag));
    }

    [Fact]
    public async Task AddPositionAsync_CreatesIndexDirectory()
    {
        // Arrange
        var tag = new Tag { Key = "Environment", Value = "Production" };

        // Act
        await _index.AddPositionAsync(_tempIndexPath, tag, 1);

        // Assert
        var tagDir = Path.Combine(_tempIndexPath, "tag");
        Assert.True(Directory.Exists(tagDir));
    }

    [Fact]
    public async Task AddPositionAsync_AddsPosition()
    {
        // Arrange
        var tag = new Tag { Key = "Environment", Value = "Production" };

        // Act
        await _index.AddPositionAsync(_tempIndexPath, tag, 1);

        // Assert
        var positions = await _index.GetPositionsAsync(_tempIndexPath, tag);
        Assert.Single(positions);
        Assert.Equal(1, positions[0]);
    }

    [Fact]
    public async Task AddPositionAsync_AddsMultiplePositions()
    {
        // Arrange
        var tag = new Tag { Key = "Environment", Value = "Production" };

        // Act
        await _index.AddPositionAsync(_tempIndexPath, tag, 1);
        await _index.AddPositionAsync(_tempIndexPath, tag, 5);
        await _index.AddPositionAsync(_tempIndexPath, tag, 3);

        // Assert
        var positions = await _index.GetPositionsAsync(_tempIndexPath, tag);
        Assert.Equal(3, positions.Length);
        Assert.Equal([1, 3, 5], positions); // Should be sorted
    }

    [Fact]
    public async Task AddPositionAsync_IgnoresDuplicatePositions()
    {
        // Arrange
        var tag = new Tag { Key = "Environment", Value = "Production" };

        // Act
        await _index.AddPositionAsync(_tempIndexPath, tag, 1);
        await _index.AddPositionAsync(_tempIndexPath, tag, 1);
        await _index.AddPositionAsync(_tempIndexPath, tag, 1);

        // Assert
        var positions = await _index.GetPositionsAsync(_tempIndexPath, tag);
        Assert.Single(positions);
        Assert.Equal(1, positions[0]);
    }

    [Fact]
    public async Task AddPositionAsync_WithNullIndexPath_ThrowsArgumentNullException()
    {
        // Arrange
        var tag = new Tag { Key = "Environment", Value = "Production" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _index.AddPositionAsync(null!, tag, 1));
    }

    [Fact]
    public async Task AddPositionAsync_WithNullTag_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _index.AddPositionAsync(_tempIndexPath, null!, 1));
    }

    [Fact]
    public async Task AddPositionAsync_WithNullTagKey_ThrowsArgumentNullException()
    {
        // Arrange
        var tag = new Tag { Key = null!, Value = "Production" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _index.AddPositionAsync(_tempIndexPath, tag, 1));
    }

    [Fact]
    public async Task AddPositionAsync_WithEmptyTagKey_ThrowsArgumentException()
    {
        // Arrange
        var tag = new Tag { Key = "", Value = "Production" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _index.AddPositionAsync(_tempIndexPath, tag, 1));
    }

    [Fact]
    public async Task AddPositionAsync_WithWhitespaceTagKey_ThrowsArgumentException()
    {
        // Arrange
        var tag = new Tag { Key = "   ", Value = "Production" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _index.AddPositionAsync(_tempIndexPath, tag, 1));
    }

    [Fact]
    public async Task AddPositionAsync_WithZeroPosition_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var tag = new Tag { Key = "Environment", Value = "Production" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _index.AddPositionAsync(_tempIndexPath, tag, 0));
    }

    [Fact]
    public async Task AddPositionAsync_WithNegativePosition_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var tag = new Tag { Key = "Environment", Value = "Production" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _index.AddPositionAsync(_tempIndexPath, tag, -1));
    }

    [Fact]
    public async Task AddPositionAsync_WithNullTagValue_WorksCorrectly()
    {
        // Arrange
        var tag = new Tag { Key = "Status", Value = null };

        // Act
        await _index.AddPositionAsync(_tempIndexPath, tag, 1);

        // Assert
        var positions = await _index.GetPositionsAsync(_tempIndexPath, tag);
        Assert.Single(positions);
    }

    // ========================================================================
    // GetPositionsAsync Tests
    // ========================================================================

    [Fact]
    public async Task GetPositionsAsync_WithNonExistentIndex_ReturnsEmptyArray()
    {
        // Arrange
        var tag = new Tag { Key = "NonExistent", Value = "Value" };

        // Act
        var positions = await _index.GetPositionsAsync(_tempIndexPath, tag);

        // Assert
        Assert.NotNull(positions);
        Assert.Empty(positions);
    }

    [Fact]
    public async Task GetPositionsAsync_ReturnsPositionsInSortedOrder()
    {
        // Arrange
        var tag = new Tag { Key = "Environment", Value = "Production" };
        await _index.AddPositionAsync(_tempIndexPath, tag, 10);
        await _index.AddPositionAsync(_tempIndexPath, tag, 2);
        await _index.AddPositionAsync(_tempIndexPath, tag, 7);

        // Act
        var positions = await _index.GetPositionsAsync(_tempIndexPath, tag);

        // Assert
        Assert.Equal([2, 7, 10], positions);
    }

    [Fact]
    public async Task GetPositionsAsync_WithMultipleTags_ReturnsCorrectPositions()
    {
        // Arrange
        var tagA = new Tag { Key = "Environment", Value = "Production" };
        var tagB = new Tag { Key = "Environment", Value = "Development" };
        await _index.AddPositionAsync(_tempIndexPath, tagA, 1);
        await _index.AddPositionAsync(_tempIndexPath, tagB, 2);
        await _index.AddPositionAsync(_tempIndexPath, tagA, 3);

        // Act
        var positionsA = await _index.GetPositionsAsync(_tempIndexPath, tagA);
        var positionsB = await _index.GetPositionsAsync(_tempIndexPath, tagB);

        // Assert
        Assert.Equal([1, 3], positionsA);
        Assert.Equal([2], positionsB);
    }

    [Fact]
    public async Task GetPositionsAsync_WithDifferentKeys_ReturnsCorrectPositions()
    {
        // Arrange
        var tagA = new Tag { Key = "Environment", Value = "Production" };
        var tagB = new Tag { Key = "Region", Value = "Production" };
        await _index.AddPositionAsync(_tempIndexPath, tagA, 1);
        await _index.AddPositionAsync(_tempIndexPath, tagB, 2);

        // Act
        var positionsA = await _index.GetPositionsAsync(_tempIndexPath, tagA);
        var positionsB = await _index.GetPositionsAsync(_tempIndexPath, tagB);

        // Assert
        Assert.Equal([1], positionsA);
        Assert.Equal([2], positionsB);
    }

    [Fact]
    public async Task GetPositionsAsync_WithNullIndexPath_ThrowsArgumentNullException()
    {
        // Arrange
        var tag = new Tag { Key = "Environment", Value = "Production" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _index.GetPositionsAsync(null!, tag));
    }

    [Fact]
    public async Task GetPositionsAsync_WithNullTag_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _index.GetPositionsAsync(_tempIndexPath, null!));
    }

    [Fact]
    public async Task GetPositionsAsync_WithNullTagKey_ThrowsArgumentNullException()
    {
        // Arrange
        var tag = new Tag { Key = null!, Value = "Production" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _index.GetPositionsAsync(_tempIndexPath, tag));
    }

    // ========================================================================
    // IndexExists Tests
    // ========================================================================

    [Fact]
    public async Task IndexExists_ReturnsTrueForExistingIndex()
    {
        // Arrange
        var tag = new Tag { Key = "Environment", Value = "Production" };
        await _index.AddPositionAsync(_tempIndexPath, tag, 1);

        // Act
        var exists = _index.IndexExists(_tempIndexPath, tag);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public void IndexExists_ReturnsFalseForNonExistentIndex()
    {
        // Arrange
        var tag = new Tag { Key = "NonExistent", Value = "Value" };

        // Act
        var exists = _index.IndexExists(_tempIndexPath, tag);

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public void IndexExists_WithNullIndexPath_ThrowsArgumentNullException()
    {
        // Arrange
        var tag = new Tag { Key = "Environment", Value = "Production" };

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _index.IndexExists(null!, tag));
    }

    [Fact]
    public void IndexExists_WithNullTag_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _index.IndexExists(_tempIndexPath, null!));
    }

    // ========================================================================
    // Safe File Name Tests
    // ========================================================================

    [Fact]
    public async Task AddPositionAsync_WithSpecialCharactersInTag_CreatesSafeFileName()
    {
        // Arrange
        var tag = new Tag { Key = "Test/Key:With*Special?Characters", Value = "Value/With:Special*Chars" };

        // Act
        await _index.AddPositionAsync(_tempIndexPath, tag, 1);

        // Assert
        Assert.True(_index.IndexExists(_tempIndexPath, tag));
        var positions = await _index.GetPositionsAsync(_tempIndexPath, tag);
        Assert.Single(positions);
    }

    [Fact]
    public async Task AddPositionAsync_WithDotInTag_CreatesSafeFileName()
    {
        // Arrange
        var tag = new Tag { Key = "Namespace.Event", Value = "Value.With.Dots" };

        // Act
        await _index.AddPositionAsync(_tempIndexPath, tag, 1);

        // Assert
        Assert.True(_index.IndexExists(_tempIndexPath, tag));
    }

    // ========================================================================
    // Corruption Handling Tests
    // ========================================================================

    [Fact]
    public async Task GetPositionsAsync_WithCorruptedIndexFile_ReturnsEmptyArray()
    {
        // Arrange
        var tag = new Tag { Key = "Environment", Value = "Production" };
        var tagDir = Path.Combine(_tempIndexPath, "tag");
        Directory.CreateDirectory(tagDir);
        var indexFile = Path.Combine(tagDir, "Environment_Production.json");
        await File.WriteAllTextAsync(indexFile, "{ invalid json }");

        // Act
        var positions = await _index.GetPositionsAsync(_tempIndexPath, tag);

        // Assert
        Assert.NotNull(positions);
        Assert.Empty(positions);
    }

    [Fact]
    public async Task AddPositionAsync_AfterCorruption_RebuildsIndex()
    {
        // Arrange
        var tag = new Tag { Key = "Environment", Value = "Production" };
        var tagDir = Path.Combine(_tempIndexPath, "tag");
        Directory.CreateDirectory(tagDir);
        var indexFile = Path.Combine(tagDir, "Environment_Production.json");
        await File.WriteAllTextAsync(indexFile, "{ invalid json }");

        // Act - Add new position, should rebuild index
        await _index.AddPositionAsync(_tempIndexPath, tag, 5);

        // Assert
        var positions = await _index.GetPositionsAsync(_tempIndexPath, tag);
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
        var tag = new Tag { Key = "Environment", Value = "Production" };
        var expectedPositions = Enumerable.Range(1, 20).Select(x => (long)x).ToArray();

        // Act - Add positions sequentially
        foreach (var position in expectedPositions)
        {
            await _index.AddPositionAsync(_tempIndexPath, tag, position);
        }

        // Assert
        var positions = await _index.GetPositionsAsync(_tempIndexPath, tag);
        Assert.Equal(20, positions.Length);
        Assert.Equal(expectedPositions, positions);
    }
}
