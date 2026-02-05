using Opossum.Projections;

namespace Opossum.UnitTests.Projections;

public class ProjectionMetadataIndexTests : IDisposable
{
    private readonly string _tempPath;
    private readonly ProjectionMetadataIndex _index;

    public ProjectionMetadataIndexTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"ProjectionMetadataIndexTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempPath);
        _index = new ProjectionMetadataIndex();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
        {
            Directory.Delete(_tempPath, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_CreatesMetadataIndexFile()
    {
        // Arrange
        var metadata = new ProjectionMetadata
        {
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            Version = 1,
            SizeInBytes = 256
        };

        // Act
        await _index.SaveAsync(_tempPath, "test-key", metadata);

        // Assert
        var indexFile = Path.Combine(_tempPath, "Metadata", "index.json");
        Assert.True(File.Exists(indexFile));
    }

    [Fact]
    public async Task GetAsync_ReturnsNullForNonExistentKey()
    {
        // Act
        var result = await _index.GetAsync(_tempPath, "non-existent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_ReturnsMetadataForExistingKey()
    {
        // Arrange
        var metadata = new ProjectionMetadata
        {
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            Version = 5,
            SizeInBytes = 512
        };
        await _index.SaveAsync(_tempPath, "test-key", metadata);

        // Act
        var result = await _index.GetAsync(_tempPath, "test-key");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(metadata.CreatedAt, result.CreatedAt);
        Assert.Equal(metadata.Version, result.Version);
        Assert.Equal(metadata.SizeInBytes, result.SizeInBytes);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllMetadata()
    {
        // Arrange
        var metadata1 = new ProjectionMetadata
        {
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            Version = 1,
            SizeInBytes = 256
        };
        var metadata2 = new ProjectionMetadata
        {
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            Version = 2,
            SizeInBytes = 512
        };

        await _index.SaveAsync(_tempPath, "key1", metadata1);
        await _index.SaveAsync(_tempPath, "key2", metadata2);

        // Act
        var all = await _index.GetAllAsync(_tempPath);

        // Assert
        Assert.Equal(2, all.Count);
        Assert.Contains("key1", all.Keys);
        Assert.Contains("key2", all.Keys);
    }

    [Fact]
    public async Task GetUpdatedSinceAsync_FiltersCorrectly()
    {
        // Arrange
        var oldDate = DateTimeOffset.UtcNow.AddDays(-10);
        var recentDate = DateTimeOffset.UtcNow.AddDays(-1);
        var cutoffDate = DateTimeOffset.UtcNow.AddDays(-5);

        var oldMetadata = new ProjectionMetadata
        {
            CreatedAt = oldDate,
            LastUpdatedAt = oldDate,
            Version = 1,
            SizeInBytes = 256
        };
        var recentMetadata = new ProjectionMetadata
        {
            CreatedAt = recentDate,
            LastUpdatedAt = recentDate,
            Version = 1,
            SizeInBytes = 512
        };

        await _index.SaveAsync(_tempPath, "old-key", oldMetadata);
        await _index.SaveAsync(_tempPath, "recent-key", recentMetadata);

        // Act
        var results = await _index.GetUpdatedSinceAsync(_tempPath, cutoffDate);

        // Assert
        Assert.Single(results);
        Assert.Equal("recent-key", results[0].Key);
        Assert.Equal(recentMetadata.Version, results[0].Metadata.Version);
    }

    [Fact]
    public async Task DeleteAsync_RemovesMetadata()
    {
        // Arrange
        var metadata = new ProjectionMetadata
        {
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            Version = 1,
            SizeInBytes = 256
        };
        await _index.SaveAsync(_tempPath, "test-key", metadata);

        // Act
        await _index.DeleteAsync(_tempPath, "test-key");

        // Assert
        var result = await _index.GetAsync(_tempPath, "test-key");
        Assert.Null(result);
    }

    [Fact]
    public async Task ClearAsync_RemovesAllMetadata()
    {
        // Arrange
        var metadata1 = new ProjectionMetadata
        {
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            Version = 1,
            SizeInBytes = 256
        };
        var metadata2 = new ProjectionMetadata
        {
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            Version = 1,
            SizeInBytes = 512
        };

        await _index.SaveAsync(_tempPath, "key1", metadata1);
        await _index.SaveAsync(_tempPath, "key2", metadata2);

        // Act
        await _index.ClearAsync(_tempPath);

        // Assert
        var all = await _index.GetAllAsync(_tempPath);
        Assert.Empty(all);

        var indexFile = Path.Combine(_tempPath, "Metadata", "index.json");
        Assert.False(File.Exists(indexFile));
    }

    [Fact]
    public async Task ConcurrentSaves_AreThreadSafe()
    {
        // Arrange
        var tasks = new List<Task>();
        var concurrencyLevel = 10;

        // Act - Save multiple metadata entries concurrently
        for (int i = 0; i < concurrencyLevel; i++)
        {
            var key = $"key-{i}";
            var metadata = new ProjectionMetadata
            {
                CreatedAt = DateTimeOffset.UtcNow,
                LastUpdatedAt = DateTimeOffset.UtcNow,
                Version = i,
                SizeInBytes = 256 * i
            };
            tasks.Add(_index.SaveAsync(_tempPath, key, metadata));
        }

        await Task.WhenAll(tasks);

        // Assert - All entries should be saved
        var all = await _index.GetAllAsync(_tempPath);
        Assert.Equal(concurrencyLevel, all.Count);
    }
}
