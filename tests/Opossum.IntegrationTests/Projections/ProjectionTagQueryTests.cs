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
        var results = await store.QueryByTagAsync(new Tag("Status", "Active"));

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
            new Tag("Status", "Active"),
            new Tag("Tier", "Premium")
        };
        var results = await store.QueryByTagsAsync(tags);

        // Assert - Only proj1 matches both
        Assert.Single(results);
        Assert.Equal("1", results[0].Id);
    }

    [Fact]
    public async Task QueryByTagsAsync_AndLogicIsCorrect_WhenFirstTagIndexIsLargerThanSecond()
    {
        // Arrange - regression test: when the first tag's index set is LARGER than the second,
        // the old code sorted to find the smallest, but then called keySets.Skip(1) on the
        // *original* unsorted list — intersecting the smallest set with itself and ignoring
        // the larger set entirely, causing the first filter to have no effect.
        var tagProvider = new TestProjectionTagProvider();
        var store = new FileSystemProjectionStore<TestProjection>(_options, "TestProjection", tagProvider);

        // Tier=Professional → 4 matches (large index, comes first in query)
        // Status=Active     → 2 matches (small index, comes second in query)
        var proj1 = new TestProjection { Id = "1", Status = "Active", Tier = "Professional" }; // matches both
        var proj2 = new TestProjection { Id = "2", Status = "Active", Tier = "Basic" };         // Active only
        var proj3 = new TestProjection { Id = "3", Status = "Inactive", Tier = "Professional" };  // Professional only
        var proj4 = new TestProjection { Id = "4", Status = "Inactive", Tier = "Professional" };  // Professional only
        var proj5 = new TestProjection { Id = "5", Status = "Inactive", Tier = "Professional" };  // Professional only

        await store.SaveAsync("1", proj1);
        await store.SaveAsync("2", proj2);
        await store.SaveAsync("3", proj3);
        await store.SaveAsync("4", proj4);
        await store.SaveAsync("5", proj5);

        // Act - Tier filter produces 4 results (large), Status filter produces 2 results (small)
        var tags = new[]
        {
            new Tag("Tier",   "Professional"),
            new Tag("Status", "Active"),
        };
        var results = await store.QueryByTagsAsync(tags);

        // Assert - only proj1 satisfies BOTH Tier=Professional AND Status=Active
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
        var results = await store.QueryByTagAsync(new Tag("status", "active"));

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
        var activeResults = await store.QueryByTagAsync(new Tag("Status", "Active"));
        Assert.Single(activeResults);

        var premiumResults = await store.QueryByTagAsync(new Tag("Tier", "Premium"));
        Assert.Single(premiumResults);

        // Should NOT be in old indices
        var pendingResults = await store.QueryByTagAsync(new Tag("Status", "Pending"));
        Assert.Empty(pendingResults);

        var basicResults = await store.QueryByTagAsync(new Tag("Tier", "Basic"));
        Assert.Empty(basicResults);
    }

    [Fact]
    public async Task SaveAsync_UpdatesIndicesWhenTagsChange_AfterApplicationRestart()
    {
        // Regression test: _projectionTags is an in-memory cache that is empty after a restart.
        // Without the fix, the first SaveAsync on a new store instance treats every existing
        // projection as "new" (no old tags in cache) and only adds new tag entries without
        // removing stale ones — leaving the key in multiple index files simultaneously.

        // Arrange – first "process": register student as Basic, then upgrade to Standard
        var tagProvider = new TestProjectionTagProvider();
        var store1 = new FileSystemProjectionStore<TestProjection>(_options, "Enrollment", tagProvider);

        await store1.SaveAsync("student-1", new TestProjection { Id = "student-1", Status = "Active", Tier = "Basic" });
        await store1.SaveAsync("student-1", new TestProjection { Id = "student-1", Status = "Active", Tier = "Standard" });

        // Simulate application restart: create a brand-new store instance over the same path.
        // The new instance has an empty _projectionTags dictionary.
        var store2 = new FileSystemProjectionStore<TestProjection>(_options, "Enrollment", tagProvider);

        // Act – upgrade to Professional via the restarted store
        await store2.SaveAsync("student-1", new TestProjection { Id = "student-1", Status = "Active", Tier = "Professional" });

        // Assert – query via a third store instance (independent read) so we test pure index state
        var store3 = new FileSystemProjectionStore<TestProjection>(_options, "Enrollment", tagProvider);

        var professional = await store3.QueryByTagAsync(new Tag("Tier", "Professional"));
        Assert.Single(professional);
        Assert.Equal("student-1", professional[0].Id);

        var standard = await store3.QueryByTagAsync(new Tag("Tier", "Standard"));
        Assert.Empty(standard); // must be removed from Standard index

        var basic = await store3.QueryByTagAsync(new Tag("Tier", "Basic"));
        Assert.Empty(basic); // must be removed from Basic index
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
        var results = await store.QueryByTagAsync(new Tag("Status", "Active"));
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
        var results = await store.QueryByTagAsync(new Tag("Status", "Active"));

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
        await store.DeleteAllIndicesAsync();

        // Assert - Indices should be gone
        var results = await store.QueryByTagAsync(new Tag("Status", "Active"));
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
            yield return new Tag("Status", state.Status);
            yield return new Tag("Tier", state.Tier);
        }
    }
}
