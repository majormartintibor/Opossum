using System.Diagnostics;
using Opossum.Projections;

namespace Opossum.Samples.CourseManagement;

/// <summary>
/// Admin endpoints for projection management.
/// These endpoints are protected and should only be accessible to administrators.
/// In production, add proper authentication/authorization.
/// </summary>
public static class AdminEndpoints
{
    public static void MapProjectionAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var adminGroup = app.MapGroup("/admin/projections")
            .WithTags("Admin - Projections");

        // Rebuild all projections
        adminGroup.MapPost("/rebuild", async (
            IProjectionManager projectionManager,
            [FromQuery] bool forceAll = false) =>
        {
            var result = await projectionManager.RebuildAllAsync(forceAll);

            return result.Success
                ? Results.Ok(result)
                : Results.Problem(
                    title: "Rebuild Failed",
                    detail: $"Failed to rebuild: {string.Join(", ", result.FailedProjections)}",
                    statusCode: 500);
        })
        .WithSummary("Rebuild all projections")
        .WithDescription("""
            Rebuilds all registered projections from the event store.
            
            WARNING: This operation can take several minutes depending on the number of events.
            
            Use Cases:
            - Disaster recovery (lost projection files)
            - Adding new projection types in production
            - Testing/development environment resets
            
            Query Parameters:
            - forceAll: If true, rebuilds ALL projections (even with valid checkpoints).
                       If false, only rebuilds projections with missing checkpoints.
            
            Production Usage:
            1. Monitor application logs during rebuild
            2. Check /admin/projections/status for progress
            3. Verify rebuild completed successfully before promoting deployment
            """);

        // Rebuild specific projection
        adminGroup.MapPost("/{projectionName}/rebuild", async (
            string projectionName,
            IProjectionManager projectionManager,
            ILogger<Program> logger) =>
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();
                await projectionManager.RebuildAsync(projectionName);
                stopwatch.Stop();

                return Results.Ok(new
                {
                    ProjectionName = projectionName,
                    Status = "Rebuilt",
                    Duration = stopwatch.Elapsed
                });
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning(ex, "Failed to rebuild projection '{ProjectionName}': {Error}", projectionName, ex.Message);
                return Results.NotFound(new { Error = ex.Message });
            }
        })
        .WithSummary("Rebuild a specific projection")
        .WithDescription("""
            Rebuilds a single projection from the event store.
            
            Use Cases:
            - Fix buggy projection logic (deploy fix, then rebuild)
            - Recover corrupted projection data
            - Test projection changes in development
            
            Example:
            POST /admin/projections/CourseDetails/rebuild
            
            Response:
            {
                "projectionName": "CourseDetails",
                "status": "Rebuilt",
                "duration": "00:00:15.1234567"
            }
            """);

        // Get rebuild status
        adminGroup.MapGet("/status", async (IProjectionManager projectionManager) =>
        {
            var status = await projectionManager.GetRebuildStatusAsync();
            return Results.Ok(status);
        })
        .WithSummary("Get projection rebuild status")
        .WithDescription("""
            Returns the current status of projection rebuilding.
            
            Use this endpoint to monitor long-running rebuild operations.
            
            Response:
            {
                "isRebuilding": true,
                "inProgressProjections": ["CourseDetails", "StudentDetails"],
                "queuedProjections": ["CourseShortInfo", "StudentShortInfo"],
                "startedAt": "2025-01-15T10:30:00Z",
                "estimatedCompletionAt": "2025-01-15T10:35:00Z"
            }
            """);

        // Get all projection checkpoints
        adminGroup.MapGet("/checkpoints", async (IProjectionManager projectionManager) =>
        {
            var projections = projectionManager.GetRegisteredProjections();
            var checkpoints = new Dictionary<string, long>();

            foreach (var projection in projections)
            {
                checkpoints[projection] = await projectionManager.GetCheckpointAsync(projection);
            }

            return Results.Ok(checkpoints);
        })
        .WithSummary("Get all projection checkpoints")
        .WithDescription("""
            Returns the last processed event position for each projection.
            
            A checkpoint of 0 indicates the projection has never been built.
            
            Response:
            {
                "CourseDetails": 1523,
                "CourseShortInfo": 1523,
                "StudentDetails": 1523,
                "StudentShortInfo": 0  ← Not yet built
            }
            """);
    }
}
