using Opossum.Core;
using Opossum.Projections;

namespace Opossum.UnitTests.Projections;

public class ProjectionTagIndexTests : IDisposable
{
    private readonly string _tempPath;
    private readonly ProjectionTagIndex _index;

    public ProjectionTagIndexTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"ProjectionTagIndexTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempPath);
        _index = new ProjectionTagIndex();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
        {
            Directory.Delete(_tempPath, recursive: true);
        }
    }

    [Fact]
    public async Task AddProjectionAsync_CreatesIndexFile()
    {
        // Arrange
        var tag = new Tag { Key = "Status", Value = "Active" };
        var projectionKey = "proj-1";

        // Act
        await _index.AddProjectionAsync(_tempPath, tag, projectionKey);

        // Assert
        // Index files are now stored in lowercase for platform-agnostic case-insensitive matching
        var indexFile = Path.Combine(_tempPath, "Indices", "status_active.json");
        Assert.True(File.Exists(indexFile));
    }

    [Fact]
    public async Task AddProjectionAsync_AddsKeyToExistingIndex()
    {
        // Arrange
        var tag = new Tag { Key = "Status", Value = "Active" };

        // Act
        await _index.AddProjectionAsync(_tempPath, tag, "proj-1");
        await _index.AddProjectionAsync(_tempPath, tag, "proj-2");
        await _index.AddProjectionAsync(_tempPath, tag, "proj-3");

        // Assert
        var keys = await _index.GetProjectionKeysByTagAsync(_tempPath, tag);
        Assert.Equal(3, keys.Length);
        Assert.Contains("proj-1", keys);
        Assert.Contains("proj-2", keys);
        Assert.Contains("proj-3", keys);
    }

    [Fact]
    public async Task AddProjectionAsync_PreventsDuplicates()
    {
        // Arrange
        var tag = new Tag { Key = "Status", Value = "Active" };

        // Act - Add same key twice
        await _index.AddProjectionAsync(_tempPath, tag, "proj-1");
        await _index.AddProjectionAsync(_tempPath, tag, "proj-1");

        // Assert - Should only appear once
        var keys = await _index.GetProjectionKeysByTagAsync(_tempPath, tag);
        Assert.Single(keys);
        Assert.Equal("proj-1", keys[0]);
    }

    [Fact]
    public async Task RemoveProjectionAsync_RemovesKeyFromIndex()
    {
        // Arrange
        var tag = new Tag { Key = "Status", Value = "Active" };
        await _index.AddProjectionAsync(_tempPath, tag, "proj-1");
        await _index.AddProjectionAsync(_tempPath, tag, "proj-2");

        // Act
        await _index.RemoveProjectionAsync(_tempPath, tag, "proj-1");

        // Assert
        var keys = await _index.GetProjectionKeysByTagAsync(_tempPath, tag);
        Assert.Single(keys);
        Assert.Equal("proj-2", keys[0]);
    }

    [Fact]
    public async Task RemoveProjectionAsync_DeletesIndexFileWhenEmpty()
    {
        // Arrange
        var tag = new Tag { Key = "Status", Value = "Active" };
        await _index.AddProjectionAsync(_tempPath, tag, "proj-1");

        // Act
        await _index.RemoveProjectionAsync(_tempPath, tag, "proj-1");

        // Assert
        // Index files are now stored in lowercase for platform-agnostic case-insensitive matching
        var indexFile = Path.Combine(_tempPath, "Indices", "status_active.json");
        Assert.False(File.Exists(indexFile));
    }

    [Fact]
    public async Task GetProjectionKeysByTagAsync_ReturnsEmptyForNonExistentTag()
    {
        // Arrange
        var tag = new Tag { Key = "Status", Value = "Inactive" };

        // Act
        var keys = await _index.GetProjectionKeysByTagAsync(_tempPath, tag);

        // Assert
        Assert.Empty(keys);
    }

    [Fact]
    public async Task GetProjectionKeysByTagsAsync_ReturnsIntersection()
    {
        // Arrange
        var tag1 = new Tag { Key = "Status", Value = "Active" };
        var tag2 = new Tag { Key = "Tier", Value = "Premium" };

        await _index.AddProjectionAsync(_tempPath, tag1, "proj-1");
        await _index.AddProjectionAsync(_tempPath, tag1, "proj-2");
        await _index.AddProjectionAsync(_tempPath, tag1, "proj-3");

        await _index.AddProjectionAsync(_tempPath, tag2, "proj-1");
        await _index.AddProjectionAsync(_tempPath, tag2, "proj-3");

        // Act - Query with both tags (AND logic)
        var keys = await _index.GetProjectionKeysByTagsAsync(_tempPath, [tag1, tag2]);

        // Assert - Only proj-1 and proj-3 have both tags
        Assert.Equal(2, keys.Length);
        Assert.Contains("proj-1", keys);
        Assert.Contains("proj-3", keys);
    }

    [Fact]
    public async Task GetProjectionKeysByTagsAsync_ReturnsEmptyIfAnyTagHasNoMatches()
    {
        // Arrange
        var tag1 = new Tag { Key = "Status", Value = "Active" };
        var tag2 = new Tag { Key = "NonExistent", Value = "Value" };

        await _index.AddProjectionAsync(_tempPath, tag1, "proj-1");

        // Act
        var keys = await _index.GetProjectionKeysByTagsAsync(_tempPath, [tag1, tag2]);

        // Assert
        Assert.Empty(keys);
    }

    [Fact]
    public async Task UpdateProjectionTagsAsync_RemovesOldAndAddsNew()
    {
        // Arrange
        var projKey = "proj-1";
        var oldTags = new[]
        {
            new Tag { Key = "Status", Value = "Pending" },
            new Tag { Key = "Tier", Value = "Basic" }
        };
        var newTags = new[]
        {
            new Tag { Key = "Status", Value = "Active" },
            new Tag { Key = "Tier", Value = "Premium" }
        };

        foreach (var tag in oldTags)
        {
            await _index.AddProjectionAsync(_tempPath, tag, projKey);
        }

        // Act
        await _index.UpdateProjectionTagsAsync(_tempPath, projKey, oldTags, newTags);

        // Assert - Old tags should not contain the projection
        var pendingKeys = await _index.GetProjectionKeysByTagAsync(_tempPath, oldTags[0]);
        Assert.Empty(pendingKeys);

        var basicKeys = await _index.GetProjectionKeysByTagAsync(_tempPath, oldTags[1]);
        Assert.Empty(basicKeys);

        // New tags should contain the projection
        var activeKeys = await _index.GetProjectionKeysByTagAsync(_tempPath, newTags[0]);
        Assert.Contains(projKey, activeKeys);

        var premiumKeys = await _index.GetProjectionKeysByTagAsync(_tempPath, newTags[1]);
        Assert.Contains(projKey, premiumKeys);
    }

    [Fact]
    public async Task UpdateProjectionTagsAsync_OnlyUpdatesChangedTags()
    {
        // Arrange
        var projKey = "proj-1";
        var oldTags = new[]
        {
            new Tag { Key = "Status", Value = "Active" },
            new Tag { Key = "Tier", Value = "Basic" }
        };
        var newTags = new[]
        {
            new Tag { Key = "Status", Value = "Active" }, // Unchanged
            new Tag { Key = "Tier", Value = "Premium" }   // Changed
        };

        foreach (var tag in oldTags)
        {
            await _index.AddProjectionAsync(_tempPath, tag, projKey);
        }

        // Act
        await _index.UpdateProjectionTagsAsync(_tempPath, projKey, oldTags, newTags);

        // Assert - Status tag should still exist (unchanged)
        var activeKeys = await _index.GetProjectionKeysByTagAsync(_tempPath, newTags[0]);
        Assert.Contains(projKey, activeKeys);

        // Basic tier should be removed
        var basicKeys = await _index.GetProjectionKeysByTagAsync(_tempPath, oldTags[1]);
        Assert.Empty(basicKeys);

        // Premium tier should be added
        var premiumKeys = await _index.GetProjectionKeysByTagAsync(_tempPath, newTags[1]);
        Assert.Contains(projKey, premiumKeys);
    }

    [Fact]
    public void DeleteAllIndices_RemovesIndicesFolder()
    {
        // Arrange
        var indicesPath = Path.Combine(_tempPath, "Indices");
        Directory.CreateDirectory(indicesPath);
        File.WriteAllText(Path.Combine(indicesPath, "test.json"), "{}");

        // Act
        _index.DeleteAllIndices(_tempPath);

        // Assert
        Assert.False(Directory.Exists(indicesPath));
    }

    [Fact]
    public async Task ConcurrentAddition_SameTag_NoLostUpdates()
    {
        // Arrange
        var tag = new Tag { Key = "Status", Value = "Active" };
        var tasks = new List<Task>();

        // Act - Add 50 projections concurrently
        for (int i = 0; i < 50; i++)
        {
            var key = $"proj-{i}";
            tasks.Add(Task.Run(async () => await _index.AddProjectionAsync(_tempPath, tag, key)));
        }
        await Task.WhenAll(tasks);

        // Assert - All 50 should be present
        var keys = await _index.GetProjectionKeysByTagAsync(_tempPath, tag);
        Assert.Equal(50, keys.Length);
    }
}
