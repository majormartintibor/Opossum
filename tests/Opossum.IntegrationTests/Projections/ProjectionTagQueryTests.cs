using Opossum.Configuration;
using Opossum.Core;
using Opossum.Projections;

namespace Opossum.IntegrationTests.Projections;

public class ProjectionTagQueryTests : IDisposable
{
    private readonly string _tempPath;
    private readonly OpossumOptions _options;

    public ProjectionTagQueryTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"ProjectionTagQueryTests_{Guid.NewGuid():N}");
        _options = new OpossumOptions
        {
            RootPath = _tempPath
        };
        _options.AddContext("TestContext");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
        {
            Directory.Delete(_tempPath, recursive: true);
        }
    }

    [Fact]
    public async Task QueryByTagAsync_ReturnsProjectionsMatchingTag()
    {
        // Arrange
        var tagProvider = new TestProjectionTagProvider();
        var store = new FileSystemProjectionStore<TestProjection>(_options, "TestProjection", tagProvider);

        var proj1 = new TestProjection { Id = "1", Status = "Active", Tier = "Premium" };
        var proj2 = new TestProjection { Id = "2", Status = "Active", Tier = "Basic" };
        var proj3 = new TestProjection { Id = "3", Status = "Inactive", Tier = "Premium" };

        await store.SaveAsync("1", proj1);
        await store.SaveAsync("2", proj2);
        await store.SaveAsync("3", proj3);

        // Act - Query for Active status
        var results = await store.QueryByTagAsync(new Tag { Key = "Status", Value = "Active" });

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Contains(results, p => p.Id == "1");
        Assert.Contains(results, p => p.Id == "2");
    }

    [Fact]
    public async Task QueryByTagsAsync_ReturnsProjectionsMatchingAllTags()
    {
        // Arrange
        var tagProvider = new TestProjectionTagProvider();
        var store = new FileSystemProjectionStore<TestProjection>(_options, "TestProjection", tagProvider);

        var proj1 = new TestProjection { Id = "1", Status = "Active", Tier = "Premium" };
        var proj2 = new TestProjection { Id = "2", Status = "Active", Tier = "Basic" };
        var proj3 = new TestProjection { Id = "3", Status = "Inactive", Tier = "Premium" };

        await store.SaveAsync("1", proj1);
        await store.SaveAsync("2", proj2);
        await store.SaveAsync("3", proj3);

        // Act - Query for Active AND Premium
        var tags = new[]
        {
            new Tag { Key = "Status", Value = "Active" },
            new Tag { Key = "Tier", Value = "Premium" }
        };
        var results = await store.QueryByTagsAsync(tags);

        // Assert - Only proj1 matches both
        Assert.Single(results);
        Assert.Equal("1", results[0].Id);
    }

    [Fact]
    public async Task QueryByTagAsync_WithCaseInsensitiveComparison_FindsMatches()
    {
        // Arrange
        var tagProvider = new TestProjectionTagProvider();
        var store = new FileSystemProjectionStore<TestProjection>(_options, "TestProjection", tagProvider);

        var proj1 = new TestProjection { Id = "1", Status = "Active", Tier = "Premium" };
        await store.SaveAsync("1", proj1);

        // Act - Query with different case
        var results = await store.QueryByTagAsync(new Tag { Key = "status", Value = "active" });

        // Assert - Should find it (case-insensitive)
        Assert.Single(results);
        Assert.Equal("1", results[0].Id);
    }

    [Fact]
    public async Task SaveAsync_UpdatesIndicesWhenTagsChange()
    {
        // Arrange
        var tagProvider = new TestProjectionTagProvider();
        var store = new FileSystemProjectionStore<TestProjection>(_options, "TestProjection", tagProvider);

        var proj1 = new TestProjection { Id = "1", Status = "Pending", Tier = "Basic" };
        await store.SaveAsync("1", proj1);

        // Act - Update projection with different tags
        proj1 = proj1 with { Status = "Active", Tier = "Premium" };
        await store.SaveAsync("1", proj1);

        // Assert - Should be in new indices
        var activeResults = await store.QueryByTagAsync(new Tag { Key = "Status", Value = "Active" });
        Assert.Single(activeResults);

        var premiumResults = await store.QueryByTagAsync(new Tag { Key = "Tier", Value = "Premium" });
        Assert.Single(premiumResults);

        // Should NOT be in old indices
        var pendingResults = await store.QueryByTagAsync(new Tag { Key = "Status", Value = "Pending" });
        Assert.Empty(pendingResults);

        var basicResults = await store.QueryByTagAsync(new Tag { Key = "Tier", Value = "Basic" });
        Assert.Empty(basicResults);
    }

    [Fact]
    public async Task DeleteAsync_RemovesFromIndices()
    {
        // Arrange
        var tagProvider = new TestProjectionTagProvider();
        var store = new FileSystemProjectionStore<TestProjection>(_options, "TestProjection", tagProvider);

        var proj1 = new TestProjection { Id = "1", Status = "Active", Tier = "Premium" };
        await store.SaveAsync("1", proj1);

        // Act
        await store.DeleteAsync("1");

        // Assert - Should no longer be in indices
        var results = await store.QueryByTagAsync(new Tag { Key = "Status", Value = "Active" });
        Assert.Empty(results);
    }

    [Fact]
    public async Task QueryByTagAsync_WithoutTagProvider_ReturnsEmpty()
    {
        // Arrange - No tag provider
        var store = new FileSystemProjectionStore<TestProjection>(_options, "TestProjection", null);

        var proj1 = new TestProjection { Id = "1", Status = "Active", Tier = "Premium" };
        await store.SaveAsync("1", proj1);

        // Act
        var results = await store.QueryByTagAsync(new Tag { Key = "Status", Value = "Active" });

        // Assert - No tag provider = no indices = empty result
        Assert.Empty(results);
    }

    [Fact]
    public async Task DeleteAllIndices_ClearsAllTagIndices()
    {
        // Arrange
        var tagProvider = new TestProjectionTagProvider();
        var store = new FileSystemProjectionStore<TestProjection>(_options, "TestProjection", tagProvider);

        var proj1 = new TestProjection { Id = "1", Status = "Active", Tier = "Premium" };
        var proj2 = new TestProjection { Id = "2", Status = "Active", Tier = "Basic" };
        await store.SaveAsync("1", proj1);
        await store.SaveAsync("2", proj2);

        // Act
        store.DeleteAllIndices();

        // Assert - Indices should be gone
        var results = await store.QueryByTagAsync(new Tag { Key = "Status", Value = "Active" });
        Assert.Empty(results);

        // But projections should still exist
        var allProjections = await store.GetAllAsync();
        Assert.Equal(2, allProjections.Count);
    }

    // Test helper classes
    private record TestProjection
    {
        public string Id { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string Tier { get; init; } = string.Empty;
    }

    private class TestProjectionTagProvider : IProjectionTagProvider<TestProjection>
    {
        public IEnumerable<Tag> GetTags(TestProjection state)
        {
            yield return new Tag { Key = "Status", Value = state.Status };
            yield return new Tag { Key = "Tier", Value = state.Tier };
        }
    }
}
