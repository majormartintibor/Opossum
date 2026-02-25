using Opossum.Configuration;
using Opossum.IntegrationTests.Helpers;
using Opossum.Projections;

namespace Opossum.IntegrationTests.Projections;

public class ProjectionStoreMetadataTests : IDisposable
{
    private readonly string _tempPath;
    private readonly OpossumOptions _options;

    public ProjectionStoreMetadataTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"ProjectionStoreMetadataTests_{Guid.NewGuid():N}");
        _options = new OpossumOptions { RootPath = _tempPath };
        _options.UseStore("TestContext");
    }

    public void Dispose()
    {
        TestDirectoryHelper.ForceDelete(_tempPath);
    }

    [Fact]
    public async Task SaveAsync_CreatesMetadataOnFirstSaveAsync()
    {
        // Arrange
        var store = new FileSystemProjectionStore<TestProjection>(_options, "TestProjection");
        var projection = new TestProjection { Id = "test-1", Value = "Initial" };

        // Act
        await store.SaveAsync("test-1", projection);

        // Assert - Check metadata file was created
        var metadataFile = Path.Combine(
            _tempPath,
            "TestContext",
            "Projections",
            "TestProjection",
            "Metadata",
            "index.json");
        Assert.True(File.Exists(metadataFile));
    }

    [Fact]
    public async Task SaveAsync_WrapsProjectionWithMetadataAsync()
    {
        // Arrange
        var store = new FileSystemProjectionStore<TestProjection>(_options, "TestProjection");
        var projection = new TestProjection { Id = "test-1", Value = "Test" };

        // Act
        await store.SaveAsync("test-1", projection);

        // Assert - Read file directly and verify wrapper structure
        var filePath = Path.Combine(
            _tempPath,
            "TestContext",
            "Projections",
            "TestProjection",
            "test-1.json");
        var json = await File.ReadAllTextAsync(filePath);

        Assert.Contains("\"data\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"metadata\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"createdAt\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"lastUpdatedAt\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"version\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"sizeInBytes\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAsync_UnwrapsMetadataAsync()
    {
        // Arrange
        var store = new FileSystemProjectionStore<TestProjection>(_options, "TestProjection");
        var projection = new TestProjection { Id = "test-1", Value = "Test" };
        await store.SaveAsync("test-1", projection);

        // Act
        var retrieved = await store.GetAsync("test-1");

        // Assert - Should get back unwrapped data
        Assert.NotNull(retrieved);
        Assert.Equal("test-1", retrieved.Id);
        Assert.Equal("Test", retrieved.Value);
    }

    [Fact]
    public async Task SaveAsync_IncrementsVersionOnUpdateAsync()
    {
        // Arrange
        var store = new FileSystemProjectionStore<TestProjection>(_options, "TestProjection");
        var projectionPath = Path.Combine(_tempPath, "TestContext", "Projections", "TestProjection");
        var indexPath = Path.Combine(projectionPath, "Metadata", "index.json");

        // Act - Save multiple times
        await store.SaveAsync("test-1", new TestProjection { Id = "test-1", Value = "V1" });
        var json1 = await File.ReadAllTextAsync(indexPath);
        var index1 = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ProjectionMetadata>>(json1);

        await store.SaveAsync("test-1", new TestProjection { Id = "test-1", Value = "V2" });
        var json2 = await File.ReadAllTextAsync(indexPath);
        var index2 = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ProjectionMetadata>>(json2);

        await store.SaveAsync("test-1", new TestProjection { Id = "test-1", Value = "V3" });
        var json3 = await File.ReadAllTextAsync(indexPath);
        var index3 = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ProjectionMetadata>>(json3);

        // Assert
        Assert.Equal(1, index1!["test-1"].Version);
        Assert.Equal(2, index2!["test-1"].Version);
        Assert.Equal(3, index3!["test-1"].Version);
    }

    [Fact]
    public async Task SaveAsync_UpdatesLastUpdatedAtAsync()
    {
        // Arrange
        var store = new FileSystemProjectionStore<TestProjection>(_options, "TestProjection");
        var projectionPath = Path.Combine(_tempPath, "TestContext", "Projections", "TestProjection");
        var indexPath = Path.Combine(projectionPath, "Metadata", "index.json");

        // Act
        await store.SaveAsync("test-1", new TestProjection { Id = "test-1", Value = "V1" });
        var json1 = await File.ReadAllTextAsync(indexPath);
        var index1 = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ProjectionMetadata>>(json1);

        await Task.Delay(100); // Ensure time difference

        await store.SaveAsync("test-1", new TestProjection { Id = "test-1", Value = "V2" });
        var json2 = await File.ReadAllTextAsync(indexPath);
        var index2 = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ProjectionMetadata>>(json2);

        // Assert
        Assert.True(index2!["test-1"].LastUpdatedAt > index1!["test-1"].LastUpdatedAt);
    }

    [Fact]
    public async Task SaveAsync_MaintainsCreatedAtAsync()
    {
        // Arrange
        var store = new FileSystemProjectionStore<TestProjection>(_options, "TestProjection");
        var projectionPath = Path.Combine(_tempPath, "TestContext", "Projections", "TestProjection");
        var indexPath = Path.Combine(projectionPath, "Metadata", "index.json");

        // Act
        await store.SaveAsync("test-1", new TestProjection { Id = "test-1", Value = "V1" });
        var json1 = await File.ReadAllTextAsync(indexPath);
        var index1 = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ProjectionMetadata>>(json1);

        await Task.Delay(100);

        await store.SaveAsync("test-1", new TestProjection { Id = "test-1", Value = "V2" });
        var json2 = await File.ReadAllTextAsync(indexPath);
        var index2 = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ProjectionMetadata>>(json2);

        // Assert - CreatedAt should not change
        Assert.Equal(index1!["test-1"].CreatedAt, index2!["test-1"].CreatedAt);
    }

    [Fact]
    public async Task SaveAsync_UpdatesSizeInBytesAsync()
    {
        // Arrange
        var store = new FileSystemProjectionStore<TestProjection>(_options, "TestProjection");
        var projectionPath = Path.Combine(_tempPath, "TestContext", "Projections", "TestProjection");
        var indexPath = Path.Combine(projectionPath, "Metadata", "index.json");

        // Act
        await store.SaveAsync("test-1", new TestProjection { Id = "test-1", Value = "Small" });
        var json1 = await File.ReadAllTextAsync(indexPath);
        var index1 = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ProjectionMetadata>>(json1);

        await store.SaveAsync("test-1", new TestProjection
        {
            Id = "test-1",
            Value = "Much larger value that will increase the JSON size significantly"
        });
        var json2 = await File.ReadAllTextAsync(indexPath);
        var index2 = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ProjectionMetadata>>(json2);

        // Assert
        Assert.True(index2!["test-1"].SizeInBytes > index1!["test-1"].SizeInBytes);
    }

    [Fact]
    public async Task DeleteAsync_RemovesMetadataAsync()
    {
        // Arrange
        var store = new FileSystemProjectionStore<TestProjection>(_options, "TestProjection");
        var projectionPath = Path.Combine(_tempPath, "TestContext", "Projections", "TestProjection");
        var indexPath = Path.Combine(projectionPath, "Metadata", "index.json");

        await store.SaveAsync("test-1", new TestProjection { Id = "test-1", Value = "Test" });

        // Act
        await store.DeleteAsync("test-1");

        // Assert
        var json = await File.ReadAllTextAsync(indexPath);
        var index = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ProjectionMetadata>>(json);
        Assert.DoesNotContain("test-1", index!.Keys);
    }

    [Fact]
    public async Task GetAllAsync_UnwrapsAllProjectionsAsync()
    {
        // Arrange
        var store = new FileSystemProjectionStore<TestProjection>(_options, "TestProjection");

        await store.SaveAsync("test-1", new TestProjection { Id = "test-1", Value = "Value1" });
        await store.SaveAsync("test-2", new TestProjection { Id = "test-2", Value = "Value2" });
        await store.SaveAsync("test-3", new TestProjection { Id = "test-3", Value = "Value3" });

        // Act
        var all = await store.GetAllAsync();

        // Assert
        Assert.Equal(3, all.Count);
        Assert.All(all, p => Assert.NotNull(p.Id));
        Assert.All(all, p => Assert.NotNull(p.Value));
    }

    // Test helper class
    private record TestProjection
    {
        public string Id { get; init; } = string.Empty;
        public string Value { get; init; } = string.Empty;
    }
}
