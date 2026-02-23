using Opossum.Projections;

namespace Opossum.IntegrationTests.Projections;

public class FileSystemProjectionStoreTests : IClassFixture<ProjectionFixture>
{
    private readonly ProjectionFixture _fixture;
    private readonly FileSystemProjectionStore<TestOrderState> _store;
    private readonly string _uniqueStoreName;

    public FileSystemProjectionStoreTests(ProjectionFixture fixture)
    {
        _fixture = fixture;
        // Use unique store name per test class instance to avoid cross-test pollution
        _uniqueStoreName = $"TestOrders_{Guid.NewGuid()}";
        _store = new FileSystemProjectionStore<TestOrderState>(
            _fixture.OpossumOptions,
            _uniqueStoreName);
    }

    [Fact]
    public async Task SaveAsync_WithValidState_SavesFile()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var state = new TestOrderState(orderId, "Customer A", 100m);

        // Act
        await _store.SaveAsync(orderId.ToString(), state);

        // Assert
        var retrieved = await _store.GetAsync(orderId.ToString());
        Assert.NotNull(retrieved);
        Assert.Equal(state.OrderId, retrieved.OrderId);
        Assert.Equal(state.CustomerName, retrieved.CustomerName);
        Assert.Equal(state.TotalAmount, retrieved.TotalAmount);
    }

    [Fact]
    public async Task GetAsync_WithNonExistentKey_ReturnsNull()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid().ToString();

        // Act
        var result = await _store.GetAsync(nonExistentId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAsync_WithExistingKey_UpdatesFile()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var initialState = new TestOrderState(orderId, "Customer A", 100m);
        var updatedState = new TestOrderState(orderId, "Customer A", 200m);

        // Act
        await _store.SaveAsync(orderId.ToString(), initialState);
        await _store.SaveAsync(orderId.ToString(), updatedState);

        // Assert
        var retrieved = await _store.GetAsync(orderId.ToString());
        Assert.NotNull(retrieved);
        Assert.Equal(200m, retrieved.TotalAmount);
    }

    [Fact]
    public async Task DeleteAsync_WithExistingKey_RemovesFile()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var state = new TestOrderState(orderId, "Customer A", 100m);
        await _store.SaveAsync(orderId.ToString(), state);

        // Act
        await _store.DeleteAsync(orderId.ToString());

        // Assert
        var retrieved = await _store.GetAsync(orderId.ToString());
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistentKey_DoesNotThrow()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid().ToString();

        // Act & Assert
        await _store.DeleteAsync(nonExistentId); // Should not throw
    }

    [Fact]
    public async Task GetAllAsync_WithMultipleStates_ReturnsAll()
    {
        // Arrange
        var order1 = new TestOrderState(Guid.NewGuid(), "Customer A", 100m);
        var order2 = new TestOrderState(Guid.NewGuid(), "Customer B", 200m);
        var order3 = new TestOrderState(Guid.NewGuid(), "Customer C", 300m);

        await _store.SaveAsync(order1.OrderId.ToString(), order1);
        await _store.SaveAsync(order2.OrderId.ToString(), order2);
        await _store.SaveAsync(order3.OrderId.ToString(), order3);

        // Act
        var allOrders = await _store.GetAllAsync();

        // Assert
        Assert.Equal(3, allOrders.Count);
        Assert.Contains(allOrders, o => o.OrderId == order1.OrderId);
        Assert.Contains(allOrders, o => o.OrderId == order2.OrderId);
        Assert.Contains(allOrders, o => o.OrderId == order3.OrderId);
    }

    [Fact]
    public async Task GetAllAsync_WithNoStates_ReturnsEmptyList()
    {
        // Arrange
        var emptyStore = new FileSystemProjectionStore<TestOrderState>(
            _fixture.OpossumOptions,
            $"EmptyStore_{Guid.NewGuid()}");

        // Act
        var result = await emptyStore.GetAllAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task QueryAsync_WithPredicate_ReturnsFilteredResults()
    {
        // Arrange
        var order1 = new TestOrderState(Guid.NewGuid(), "Customer A", 100m);
        var order2 = new TestOrderState(Guid.NewGuid(), "Customer B", 200m);
        var order3 = new TestOrderState(Guid.NewGuid(), "Customer C", 300m);

        await _store.SaveAsync(order1.OrderId.ToString(), order1);
        await _store.SaveAsync(order2.OrderId.ToString(), order2);
        await _store.SaveAsync(order3.OrderId.ToString(), order3);

        // Act
        var expensiveOrders = await _store.QueryAsync(o => o.TotalAmount >= 200m);

        // Assert
        Assert.Equal(2, expensiveOrders.Count);
        Assert.Contains(expensiveOrders, o => o.OrderId == order2.OrderId);
        Assert.Contains(expensiveOrders, o => o.OrderId == order3.OrderId);
    }

    [Fact]
    public async Task SaveAsync_WithSpecialCharactersInKey_HandlesSafely()
    {
        // Arrange
        var key = "order/with:special*chars?";
        var state = new TestOrderState(Guid.NewGuid(), "Customer", 100m);

        // Act
        await _store.SaveAsync(key, state);

        // Assert
        var retrieved = await _store.GetAsync(key);
        Assert.NotNull(retrieved);
    }

    [Fact]
    public async Task SaveAsync_WithNullState_ThrowsArgumentNullException()
    {
        // Arrange
        var key = Guid.NewGuid().ToString();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _store.SaveAsync(key, null!));
    }

    [Fact]
    public async Task GetAsync_WithNullKey_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _store.GetAsync(null!));
    }

    [Fact]
    public async Task GetAsync_WithEmptyKey_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.GetAsync(""));
    }

    // ========================================================================
    // Missing Directory Tests (for new projection types / deleted folders)
    // ========================================================================

    [Fact]
    public async Task GetAsync_WhenDirectoryMissing_ReturnsNull()
    {
        // Arrange - Create store with unique name, then delete its directory
        var storeName = $"MissingDirStore_{Guid.NewGuid()}";
        var store = new FileSystemProjectionStore<TestOrderState>(
            _fixture.OpossumOptions,
            storeName);

        var projectionPath = Path.Combine(
            _fixture.OpossumOptions.RootPath,
            _fixture.OpossumOptions.StoreName!,
            "Projections",
            storeName);

        // Delete the directory to simulate missing projection folder
        if (Directory.Exists(projectionPath))
        {
            Directory.Delete(projectionPath, recursive: true);
        }

        // Act
        var result = await store.GetAsync("any-key");

        // Assert
        Assert.Null(result); // Should return null, not throw
    }

    [Fact]
    public async Task GetAllAsync_WhenDirectoryMissing_ReturnsEmptyList()
    {
        // Arrange - Create store with unique name, then delete its directory
        var storeName = $"MissingDirStore_{Guid.NewGuid()}";
        var store = new FileSystemProjectionStore<TestOrderState>(
            _fixture.OpossumOptions,
            storeName);

        var projectionPath = Path.Combine(
            _fixture.OpossumOptions.RootPath,
            _fixture.OpossumOptions.StoreName!,
            "Projections",
            storeName);

        // Delete the directory to simulate missing projection folder
        if (Directory.Exists(projectionPath))
        {
            Directory.Delete(projectionPath, recursive: true);
        }

        // Act
        var result = await store.GetAllAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result); // Should return empty list, not throw
    }

    [Fact]
    public async Task SaveAsync_WhenDirectoryMissing_CreatesDirectoryAndSaves()
    {
        // Arrange - Create store with unique name, then delete its directory
        var storeName = $"MissingDirStore_{Guid.NewGuid()}";
        var store = new FileSystemProjectionStore<TestOrderState>(
            _fixture.OpossumOptions,
            storeName);

        var projectionPath = Path.Combine(
            _fixture.OpossumOptions.RootPath,
            _fixture.OpossumOptions.StoreName!,
            "Projections",
            storeName);

        // Delete the directory to simulate missing projection folder
        if (Directory.Exists(projectionPath))
        {
            Directory.Delete(projectionPath, recursive: true);
        }

        var orderId = Guid.NewGuid();
        var state = new TestOrderState(orderId, "Customer A", 100m);

        // Act
        await store.SaveAsync(orderId.ToString(), state);

        // Assert
        Assert.True(Directory.Exists(projectionPath), "Directory should be created");
        var retrieved = await store.GetAsync(orderId.ToString());
        Assert.NotNull(retrieved);
        Assert.Equal(state.OrderId, retrieved.OrderId);
    }

    [Fact]
    public async Task DeleteAsync_WhenDirectoryMissing_DoesNotThrow()
    {
        // Arrange - Create store with unique name, then delete its directory
        var storeName = $"MissingDirStore_{Guid.NewGuid()}";
        var store = new FileSystemProjectionStore<TestOrderState>(
            _fixture.OpossumOptions,
            storeName);

        var projectionPath = Path.Combine(
            _fixture.OpossumOptions.RootPath,
            _fixture.OpossumOptions.StoreName!,
            "Projections",
            storeName);

        // Delete the directory to simulate missing projection folder
        if (Directory.Exists(projectionPath))
        {
            Directory.Delete(projectionPath, recursive: true);
        }

        // Act & Assert - Should not throw
        await store.DeleteAsync("any-key");
    }

    [Fact]
    public async Task QueryAsync_WhenDirectoryMissing_ReturnsEmptyList()
    {
        // Arrange - Create store with unique name, then delete its directory
        var storeName = $"MissingDirStore_{Guid.NewGuid()}";
        var store = new FileSystemProjectionStore<TestOrderState>(
            _fixture.OpossumOptions,
            storeName);

        var projectionPath = Path.Combine(
            _fixture.OpossumOptions.RootPath,
            _fixture.OpossumOptions.StoreName!,
            "Projections",
            storeName);

        // Delete the directory to simulate missing projection folder
        if (Directory.Exists(projectionPath))
        {
            Directory.Delete(projectionPath, recursive: true);
        }

        // Act
        var result = await store.QueryAsync(o => o.TotalAmount > 100m);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result); // Should return empty list, not throw
    }

    // Test state model
    private record TestOrderState(Guid OrderId, string CustomerName, decimal TotalAmount);
}
