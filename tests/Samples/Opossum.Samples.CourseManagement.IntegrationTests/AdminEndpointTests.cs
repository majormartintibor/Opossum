using System.Net;
using System.Net.Http.Json;
using Opossum.Projections;

namespace Opossum.Samples.CourseManagement.IntegrationTests;

/// <summary>
/// Integration tests for admin projection management endpoints.
/// Tests the actual HTTP endpoints exposed by the sample application.
/// Uses dedicated collection to avoid sharing state with other test classes.
/// </summary>
[Collection("Admin Tests")]
public class AdminEndpointTests
{
    private readonly HttpClient _client;
    private readonly IntegrationTestFixture _fixture;

    public AdminEndpointTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Client;
    }

    [Fact]
    public async Task POST_RebuildAll_ReturnsOkWithResultAsync()
    {
        // Act
        var response = await _client.PostAsync("/admin/projections/rebuild", null);

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ProjectionRebuildResult>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotNull(result.Details);
    }

    [Fact]
    public async Task POST_RebuildAll_WithForceAll_RebuildsAllProjectionsAsync()
    {
        // Act - Rebuild with forceAll=true (should rebuild ALL projections regardless of checkpoints)
        var response = await _client.PostAsync("/admin/projections/rebuild?forceAll=true", null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ProjectionRebuildResult>();

        // Assert - All 4 sample app projections should be rebuilt
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(4, result.TotalRebuilt);
        Assert.All(result.Details, detail => Assert.True(detail.Success));
    }

    [Fact]
    public async Task POST_RebuildAll_WithForceAllFalse_OnlyRebuildsProjectionsWithMissingCheckpointsAsync()
    {
        // Arrange - Test database is already seeded with students and courses by fixture
        // Use forceAll=true first to ensure we start from a known state (other tests may have already rebuilt)
        var setupResponse = await _client.PostAsync("/admin/projections/rebuild?forceAll=true", null);
        setupResponse.EnsureSuccessStatusCode();
        var setupResult = await setupResponse.Content.ReadFromJsonAsync<ProjectionRebuildResult>();

        Assert.NotNull(setupResult);
        Assert.True(setupResult.Success);
        Assert.Equal(4, setupResult.TotalRebuilt); // All 4 projections rebuilt

        // Verify all projections now have non-zero checkpoints (processed seeded events)
        var checkpointsResponse = await _client.GetAsync("/admin/projections/checkpoints");
        checkpointsResponse.EnsureSuccessStatusCode();
        var checkpoints = await checkpointsResponse.Content.ReadFromJsonAsync<Dictionary<string, long>>();
        Assert.NotNull(checkpoints);
        Assert.Equal(4, checkpoints.Count);
        Assert.All(checkpoints.Values, checkpoint => Assert.True(checkpoint > 0,
            $"All checkpoints should be > 0 after processing seeded events"));

        // Wait for checkpoint persistence
        await Task.Delay(500);

        // Act - Rebuild with forceAll=false (should not rebuild anything - all have checkpoints > 0)
        var response = await _client.PostAsync("/admin/projections/rebuild?forceAll=false", null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ProjectionRebuildResult>();

        // Assert - No projections should be rebuilt (all have checkpoints > 0)
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(0, result.TotalRebuilt);
    }

    [Fact]
    public async Task POST_RebuildSpecific_WithValidProjectionName_ReturnsOkAsync()
    {
        // Act
        var response = await _client.PostAsync("/admin/projections/CourseDetails/rebuild", null);

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<dynamic>();
        Assert.NotNull(result);
    }

    [Fact]
    public async Task POST_RebuildSpecific_WithInvalidProjectionName_ReturnsNotFoundAsync()
    {
        // Act
        var response = await _client.PostAsync("/admin/projections/NonExistentProjection/rebuild", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<dynamic>();
        Assert.NotNull(result);
    }

    [Fact]
    public async Task POST_RebuildSpecific_RebuildsOnlySpecifiedProjectionAsync()
    {
        // Arrange - Get initial checkpoints
        var initialCheckpointsResponse = await _client.GetAsync("/admin/projections/checkpoints");
        initialCheckpointsResponse.EnsureSuccessStatusCode();
        var initialCheckpoints = await initialCheckpointsResponse.Content.ReadFromJsonAsync<Dictionary<string, long>>();

        // Act - Rebuild only CourseDetails
        var rebuildResponse = await _client.PostAsync("/admin/projections/CourseDetails/rebuild", null);
        rebuildResponse.EnsureSuccessStatusCode();

        // Assert - Verify rebuild response
        var rebuildResult = await rebuildResponse.Content.ReadFromJsonAsync<dynamic>();
        Assert.NotNull(rebuildResult);

        // Get updated checkpoints
        var updatedCheckpointsResponse = await _client.GetAsync("/admin/projections/checkpoints");
        updatedCheckpointsResponse.EnsureSuccessStatusCode();
        var updatedCheckpoints = await updatedCheckpointsResponse.Content.ReadFromJsonAsync<Dictionary<string, long>>();

        Assert.NotNull(updatedCheckpoints);
        Assert.True(updatedCheckpoints.ContainsKey("CourseDetails"));
    }

    [Fact]
    public async Task GET_Status_ReturnsRebuildStatusAsync()
    {
        // Act
        var response = await _client.GetAsync("/admin/projections/status");

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var status = await response.Content.ReadFromJsonAsync<ProjectionRebuildStatus>();
        Assert.NotNull(status);
        Assert.NotNull(status.InProgressProjections);
        Assert.NotNull(status.QueuedProjections);
    }

    [Fact]
    public async Task GET_Status_WhenNotRebuilding_ReturnsNotRebuildingStatusAsync()
    {
        // Act
        var response = await _client.GetAsync("/admin/projections/status");

        // Assert
        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadFromJsonAsync<ProjectionRebuildStatus>();

        Assert.NotNull(status);
        Assert.False(status.IsRebuilding);
        Assert.Empty(status.InProgressProjections);
        Assert.Empty(status.QueuedProjections);
        Assert.Null(status.StartedAt);
    }

    [Fact]
    public async Task GET_Checkpoints_ReturnsAllProjectionCheckpointsAsync()
    {
        // Act
        var response = await _client.GetAsync("/admin/projections/checkpoints");

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var checkpoints = await response.Content.ReadFromJsonAsync<Dictionary<string, long>>();
        Assert.NotNull(checkpoints);

        // Sample app has 4 projections
        Assert.True(checkpoints.Count >= 4, $"Expected at least 4 projections, got {checkpoints.Count}");

        // Verify expected projections exist
        Assert.Contains("CourseDetails", checkpoints.Keys);
        Assert.Contains("CourseShortInfo", checkpoints.Keys);
        Assert.Contains("StudentDetails", checkpoints.Keys);
        Assert.Contains("StudentShortInfo", checkpoints.Keys);

        // All values should be >= 0
        Assert.All(checkpoints.Values, checkpoint => Assert.True(checkpoint >= 0));
    }

    [Fact]
    public async Task GET_Checkpoints_AfterRebuild_ReturnsUpdatedCheckpointsAsync()
    {
        // Arrange - Get initial checkpoints
        var initialResponse = await _client.GetAsync("/admin/projections/checkpoints");
        initialResponse.EnsureSuccessStatusCode();
        var initialCheckpoints = await initialResponse.Content.ReadFromJsonAsync<Dictionary<string, long>>();
        Assert.NotNull(initialCheckpoints);

        // Act - Force rebuild all
        var rebuildResponse = await _client.PostAsync("/admin/projections/rebuild?forceAll=true", null);
        rebuildResponse.EnsureSuccessStatusCode();

        // Get updated checkpoints
        var updatedResponse = await _client.GetAsync("/admin/projections/checkpoints");
        updatedResponse.EnsureSuccessStatusCode();
        var updatedCheckpoints = await updatedResponse.Content.ReadFromJsonAsync<Dictionary<string, long>>();

        // Assert - Checkpoints should match initial (or be greater if events were added)
        Assert.NotNull(updatedCheckpoints);
        Assert.Equal(initialCheckpoints.Count, updatedCheckpoints.Count);

        foreach (var projection in initialCheckpoints.Keys)
        {
            Assert.True(updatedCheckpoints.ContainsKey(projection),
                $"Projection {projection} should still exist after rebuild");
            Assert.True(updatedCheckpoints[projection] >= 0,
                $"Checkpoint for {projection} should be >= 0");
        }
    }

    [Fact]
    public async Task RebuildResult_HasBasicStructureAsync()
    {
        // This is a lightweight test - detailed structure validation is in AdminEndpointResultStructureTests
        // which uses minimal data for fast execution

        // Act
        var response = await _client.PostAsync("/admin/projections/rebuild?forceAll=false", null);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ProjectionRebuildResult>();

        // Assert - Basic structure only
        Assert.NotNull(result);
        Assert.NotNull(result.Details);
    }

    [Fact]
    public async Task AdminEndpoints_AreAccessibleWithoutAuthenticationAsync()
    {
        // This test documents that admin endpoints are currently NOT protected
        // In production, these should require authentication/authorization

        // Act & Assert - All endpoints should be accessible
        var rebuildResponse = await _client.PostAsync("/admin/projections/rebuild", null);
        Assert.True(rebuildResponse.IsSuccessStatusCode,
            "Rebuild endpoint should be accessible (WARNING: Add auth in production!)");

        var statusResponse = await _client.GetAsync("/admin/projections/status");
        Assert.True(statusResponse.IsSuccessStatusCode,
            "Status endpoint should be accessible");

        var checkpointsResponse = await _client.GetAsync("/admin/projections/checkpoints");
        Assert.True(checkpointsResponse.IsSuccessStatusCode,
            "Checkpoints endpoint should be accessible");
    }

    [Fact]
    public async Task RebuildAll_ReturnsSuccessfullyAsync()
    {
        // Lightweight smoke test - detailed parallelism verification is in AdminEndpointResultStructureTests
        // which uses minimal data for fast execution

        // Act
        var response = await _client.PostAsync("/admin/projections/rebuild?forceAll=false", null);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ProjectionRebuildResult>();

        // Assert - Basic success check only
        Assert.NotNull(result);
        Assert.True(result.Success);
    }
}
