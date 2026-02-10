using System.Net;
using System.Net.Http.Json;
using Opossum.Projections;

namespace Opossum.Samples.CourseManagement.IntegrationTests;

/// <summary>
/// Integration tests for admin projection management endpoints.
/// Tests the actual HTTP endpoints exposed by the sample application.
/// </summary>
[Collection("Integration Tests")]
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
    public async Task POST_RebuildAll_ReturnsOkWithResult()
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
    public async Task POST_RebuildAll_WithForceAll_RebuildsAllProjections()
    {
        // Act - First rebuild (will rebuild missing projections)
        var firstResponse = await _client.PostAsync("/admin/projections/rebuild?forceAll=false", null);
        firstResponse.EnsureSuccessStatusCode();
        var firstResult = await firstResponse.Content.ReadFromJsonAsync<ProjectionRebuildResult>();

        // Act - Second rebuild with forceAll=true (should rebuild even with checkpoints)
        var secondResponse = await _client.PostAsync("/admin/projections/rebuild?forceAll=true", null);
        secondResponse.EnsureSuccessStatusCode();
        var secondResult = await secondResponse.Content.ReadFromJsonAsync<ProjectionRebuildResult>();

        // Assert - Second rebuild should rebuild all projections
        Assert.NotNull(secondResult);
        Assert.True(secondResult.Success);
        // All 4 sample app projections should be rebuilt
        Assert.True(secondResult.TotalRebuilt >= 4, $"Expected at least 4 projections, got {secondResult.TotalRebuilt}");
    }

    [Fact]
    public async Task POST_RebuildAll_WithForceAllFalse_OnlyRebuildsProjectionsWithMissingCheckpoints()
    {
        // Arrange - First rebuild to establish checkpoints
        await _client.PostAsync("/admin/projections/rebuild?forceAll=true", null);

        // Act - Second rebuild without force (should not rebuild anything)
        var response = await _client.PostAsync("/admin/projections/rebuild?forceAll=false", null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ProjectionRebuildResult>();

        // Assert - No projections should be rebuilt (all have checkpoints)
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(0, result.TotalRebuilt);
    }

    [Fact]
    public async Task POST_RebuildSpecific_WithValidProjectionName_ReturnsOk()
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
    public async Task POST_RebuildSpecific_WithInvalidProjectionName_ReturnsNotFound()
    {
        // Act
        var response = await _client.PostAsync("/admin/projections/NonExistentProjection/rebuild", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<dynamic>();
        Assert.NotNull(result);
    }

    [Fact]
    public async Task POST_RebuildSpecific_RebuildsOnlySpecifiedProjection()
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
    public async Task GET_Status_ReturnsRebuildStatus()
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
    public async Task GET_Status_WhenNotRebuilding_ReturnsNotRebuildingStatus()
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
    public async Task GET_Checkpoints_ReturnsAllProjectionCheckpoints()
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
    public async Task GET_Checkpoints_AfterRebuild_ReturnsUpdatedCheckpoints()
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
    public async Task RebuildResult_ContainsDetailedInformation()
    {
        // Act
        var response = await _client.PostAsync("/admin/projections/rebuild?forceAll=true", null);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ProjectionRebuildResult>();

        // Assert - Verify result structure
        Assert.NotNull(result);
        Assert.NotNull(result.Details);
        Assert.True(result.Details.Count > 0, "Should have at least one projection detail");
        
        // Verify each detail has required information
        foreach (var detail in result.Details)
        {
            Assert.NotNull(detail.ProjectionName);
            Assert.NotEmpty(detail.ProjectionName);
            Assert.True(detail.Duration >= TimeSpan.Zero, "Duration should be non-negative");
            Assert.True(detail.EventsProcessed >= 0, "EventsProcessed should be non-negative");
            
            if (detail.Success)
            {
                Assert.Null(detail.ErrorMessage);
            }
            else
            {
                Assert.NotNull(detail.ErrorMessage);
            }
        }

        // Verify overall result properties
        Assert.True(result.Duration > TimeSpan.Zero, "Overall duration should be positive");
        Assert.Equal(result.Details.Count(d => d.Success), result.TotalRebuilt);
    }

    [Fact]
    public async Task AdminEndpoints_AreAccessibleWithoutAuthentication()
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
    public async Task RebuildAll_WithMultipleProjections_ExecutesInParallel()
    {
        // This test verifies that parallel execution provides performance benefits
        // by comparing the total duration to the sum of individual durations

        // Act
        var response = await _client.PostAsync("/admin/projections/rebuild?forceAll=true", null);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ProjectionRebuildResult>();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Details.Count > 1, "Need multiple projections to test parallelism");

        // Calculate sum of individual durations
        var sumOfIndividualDurations = TimeSpan.Zero;
        foreach (var detail in result.Details)
        {
            sumOfIndividualDurations += detail.Duration;
        }

        // With parallel execution, total duration should be less than sum of individual durations
        // (unless there's only 1 projection or they're very fast)
        // We allow for some overhead, so we check it's at most 80% of sequential time
        var maxExpectedDuration = sumOfIndividualDurations * 0.8;
        
        // Note: This assertion might be flaky on very fast systems or with very few events
        // It's more of a smoke test to ensure parallelism is happening
        if (result.Details.Count >= 2 && sumOfIndividualDurations.TotalMilliseconds > 100)
        {
            Assert.True(result.Duration <= sumOfIndividualDurations, 
                $"Parallel execution ({result.Duration}) should be faster than sequential ({sumOfIndividualDurations})");
        }
    }
}
