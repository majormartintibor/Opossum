using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Opossum.Samples.CourseManagement.IntegrationTests;

/// <summary>
/// Integration tests for the store admin endpoint (DELETE /admin/store).
/// Uses a dedicated isolated collection so deleting the store does not affect
/// other test collections that share a fixture.
/// </summary>
[Collection("Store Admin Tests")]
public class StoreAdminEndpointTests
{
    private readonly HttpClient _client;
    private readonly IntegrationTestFixture _fixture;

    public StoreAdminEndpointTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Client;
    }

    [Fact]
    public async Task DELETE_Store_WithoutConfirm_ReturnsBadRequestAsync()
    {
        // Act
        var response = await _client.DeleteAsync("/admin/store");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DELETE_Store_WithConfirmFalse_ReturnsBadRequestAsync()
    {
        // Act
        var response = await _client.DeleteAsync("/admin/store?confirm=false");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DELETE_Store_WithConfirmTrue_ReturnsNoContentAsync()
    {
        // Act
        var response = await _client.DeleteAsync("/admin/store?confirm=true");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DELETE_Store_WithConfirmTrue_DeletesStoreDirectoryAsync()
    {
        // Arrange — create a course to ensure the store directory exists, regardless
        // of which other tests in this collection ran first and may have deleted the store.
        await _client.PostAsJsonAsync("/courses", new
        {
            CourseId = Guid.NewGuid(),
            Name = "Delete Test Course",
            Description = "Ensures store directory exists before testing deletion",
            StudentLimit = 5
        });

        var storeName = GetStoreName();
        var storePath = Path.Combine(_fixture.TestDatabasePath, storeName);
        Assert.True(Directory.Exists(storePath), "Store directory should exist after creating a course");

        // Act
        var response = await _client.DeleteAsync("/admin/store?confirm=true");
        response.EnsureSuccessStatusCode();

        // Assert — the store subdirectory is gone
        Assert.False(Directory.Exists(storePath), "Store directory should be deleted");
    }

    [Fact]
    public async Task DELETE_Store_ThenAppendEvent_StoreIsRecreatedAsync()
    {
        // Arrange — delete the store
        var deleteResponse = await _client.DeleteAsync("/admin/store?confirm=true");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Act — trigger a new event by enrolling (the POST endpoint creates events)
        var courseId = Guid.NewGuid();
        await _client.PostAsJsonAsync("/courses", new
        {
            CourseId = courseId,
            Name = "Post-Delete Course",
            Description = "Created after store deletion",
            StudentLimit = 5
        });

        // Assert — store directory is recreated and events exist
        var storeName = GetStoreName();
        var storePath = Path.Combine(_fixture.TestDatabasePath, storeName);
        Assert.True(Directory.Exists(storePath), "Store directory should be recreated after first append");
    }

    [Fact]
    public async Task DELETE_Store_CalledTwice_SecondCallAlsoSucceedsAsync()
    {
        // Arrange — first deletion
        var first = await _client.DeleteAsync("/admin/store?confirm=true");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        // Act — second deletion on an already-absent directory
        var second = await _client.DeleteAsync("/admin/store?confirm=true");

        // Assert — should succeed (idempotent)
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the store name from the test fixture's factory configuration, matching
    /// the same logic used by <see cref="IntegrationTestFixture"/>.
    /// </summary>
    private string GetStoreName()
    {
        var config = _fixture.Factory.Services
            .GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();

        return config["Opossum:StoreName"] ?? "TestContext";
    }
}
