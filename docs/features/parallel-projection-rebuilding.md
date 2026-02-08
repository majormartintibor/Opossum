# Parallel Projection Rebuilding - Feature Specification

**Status:** Planning  
**Target Version:** v1.1.0  
**Created:** 2025-01-XX  
**Priority:** High  

---

## Table of Contents

1. [Overview](#overview)
2. [Problem Statement](#problem-statement)
3. [Use Cases](#use-cases)
4. [Solution Design](#solution-design)
5. [API Design](#api-design)
6. [Implementation Plan](#implementation-plan)
7. [Testing Strategy](#testing-strategy)
8. [Migration Guide](#migration-guide)

---

## Overview

Add support for **parallel projection rebuilding** to significantly improve rebuild performance while maintaining control over resource usage. This feature addresses both development (fast iteration) and production (controlled, monitored rebuilds) scenarios.

### Goals

✅ **Performance:** Reduce rebuild time by up to 4x (for 4 projection types on 4+ cores)  
✅ **Control:** Configurable concurrency limits to prevent resource exhaustion  
✅ **Production-Ready:** Support for manual, controlled rebuilds in production  
✅ **Observability:** Detailed logging and progress tracking  
✅ **Safety:** Prevent disk I/O saturation and memory pressure  

### Non-Goals

❌ Automatic detection of corrupted projection files (future enhancement)  
❌ Incremental rebuilds (only missing/deleted files) - current: all-or-nothing  
❌ Per-projection concurrency tuning (future enhancement)  

---

## Problem Statement

### Current Behavior

**Sequential Rebuilding:**
- 4 projections × 30 seconds each = **120 seconds total**
- Only 1 CPU core utilized
- Wastes available parallelism

**Production Concerns:**
- `EnableAutoRebuild = true` blocks app startup for 2+ minutes
- No control over rebuild timing
- No progress visibility
- No manual rebuild API for bug fixes

### Desired Behavior

**Parallel Rebuilding:**
- 4 projections in parallel = **30 seconds total** (4x speedup)
- Configurable concurrency (2-8 cores)
- Manual rebuild API for production
- Progress logging and observability

---

## Use Cases

### Use Case 1: Development - Fast Iteration

**Actor:** Developer  
**Scenario:** Reset database, reseed data, restart app  
**Need:** Fast automatic rebuilds without manual intervention  

**Solution:**
```csharp
// appsettings.Development.json
builder.Services.AddProjections(options =>
{
    options.EnableAutoRebuild = true;
    options.MaxConcurrentRebuilds = 4; // Use 4 cores
});
```

**Result:** App starts in 30 seconds instead of 120 seconds.

---

### Use Case 2: Production - Add New Projection Type

**Actor:** DevOps Engineer  
**Scenario:** Deploy v2.0 with new `CourseStatistics` projection  
**Need:** Build new projection from historical events without downtime  

**Solution:**
```csharp
// appsettings.Production.json
builder.Services.AddProjections(options =>
{
    options.EnableAutoRebuild = false; // Manual control
    options.MaxConcurrentRebuilds = 2; // Limit resource usage
});
```

**Deployment:**
1. Deploy v2.0 (app starts immediately, `CourseStatistics` empty)
2. Call manual rebuild API: `POST /admin/projections/rebuild`
3. Monitor rebuild progress via logs/health endpoint

**Result:** Zero downtime, controlled resource usage.

---

### Use Case 3: Production - Fix Projection Bug

**Actor:** DevOps Engineer  
**Scenario:** Deployed buggy projection logic, need to rebuild  
**Need:** Rebuild specific projection(s) without rebuilding all  

**Solution:**
```csharp
// Call manual rebuild API for specific projection
POST /admin/projections/CourseDetails/rebuild
```

**Result:** Only `CourseDetails` rebuilds, others remain operational.

---

### Use Case 4: Disaster Recovery

**Actor:** SRE  
**Scenario:** Disk corruption, lost projection files, events intact  
**Need:** Rebuild all projections from event store  

**Solution:**
```csharp
// Call manual rebuild API for all projections
POST /admin/projections/rebuild?forceAll=true
```

**Result:** All projections rebuilt from events.

---

## Solution Design

### Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                     ASP.NET Core App                        │
│                                                             │
│  ┌──────────────────────────────────────────────────┐      │
│  │         ProjectionDaemon (BackgroundService)     │      │
│  │                                                  │      │
│  │  1. Startup: Wait 2 seconds                      │      │
│  │  2. Auto-rebuild (if enabled):                   │      │
│  │     └─ RebuildMissingProjectionsAsync()          │      │
│  │        ├─ Parallel.ForEachAsync()                │      │
│  │        └─ MaxDegreeOfParallelism = config        │      │
│  │  3. Polling: ProcessNewEventsAsync()             │      │
│  │     └─ Every 5 seconds                           │      │
│  └──────────────────────────────────────────────────┘      │
│                                                             │
│  ┌──────────────────────────────────────────────────┐      │
│  │         IProjectionManager (API)                 │      │
│  │                                                  │      │
│  │  + RebuildAsync(name)           ← Existing       │      │
│  │  + RebuildAllAsync()             ← NEW           │      │
│  │  + RebuildAsync(names[])         ← NEW           │      │
│  │  + GetRebuildStatusAsync()       ← NEW           │      │
│  └──────────────────────────────────────────────────┘      │
│                                                             │
│  ┌──────────────────────────────────────────────────┐      │
│  │       Admin Endpoints (Production)               │      │
│  │                                                  │      │
│  │  POST /admin/projections/rebuild                │      │
│  │  POST /admin/projections/{name}/rebuild         │      │
│  │  GET  /admin/projections/status                 │      │
│  └──────────────────────────────────────────────────┘      │
└─────────────────────────────────────────────────────────────┘
```

### Configuration Options

```csharp
public class ProjectionOptions
{
    /// <summary>
    /// Automatically rebuild missing projections on application startup.
    /// 
    /// DEVELOPMENT: Set to true for fast iteration.
    /// PRODUCTION: Set to false and use manual rebuild API.
    /// 
    /// Default: true (optimized for development)
    /// </summary>
    public bool EnableAutoRebuild { get; set; } = true;

    /// <summary>
    /// Maximum number of projections to rebuild concurrently.
    /// 
    /// DISK TYPE RECOMMENDATIONS:
    /// - HDD (single disk): 2-4
    /// - SSD: 4-8
    /// - NVMe SSD: 8-16
    /// - RAID array: 16-32
    /// 
    /// IMPORTANT: Higher values improve rebuild speed but increase:
    /// - CPU usage (may slow HTTP requests)
    /// - Memory usage (all events loaded in parallel)
    /// - Disk I/O contention
    /// 
    /// Default: 4 (balanced for most scenarios)
    /// </summary>
    public int MaxConcurrentRebuilds { get; set; } = 4;

    // ... existing properties ...
}
```

---

## API Design

### 1. IProjectionManager Extensions

```csharp
public interface IProjectionManager
{
    // ========================================================================
    // EXISTING METHODS
    // ========================================================================
    
    /// <summary>
    /// Rebuilds a single projection from scratch by replaying all events.
    /// </summary>
    Task RebuildAsync(string projectionName, CancellationToken cancellationToken = default);
    
    Task<long> GetCheckpointAsync(string projectionName, CancellationToken cancellationToken = default);
    IReadOnlyList<string> GetRegisteredProjections();

    // ========================================================================
    // NEW METHODS - Parallel & Batch Rebuilding
    // ========================================================================

    /// <summary>
    /// Rebuilds all registered projections in parallel.
    /// Respects MaxConcurrentRebuilds configuration.
    /// </summary>
    /// <param name="forceRebuild">
    /// If true, rebuilds even projections with existing checkpoints.
    /// If false, only rebuilds projections with checkpoint = 0.
    /// </param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Rebuild result summary</returns>
    Task<ProjectionRebuildResult> RebuildAllAsync(
        bool forceRebuild = false, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds specific projections in parallel.
    /// Useful for rebuilding only buggy projections after a fix.
    /// </summary>
    /// <param name="projectionNames">Names of projections to rebuild</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Rebuild result summary</returns>
    Task<ProjectionRebuildResult> RebuildAsync(
        string[] projectionNames, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current rebuild status (in-progress or completed).
    /// </summary>
    /// <returns>Rebuild status with progress information</returns>
    Task<ProjectionRebuildStatus> GetRebuildStatusAsync();
}
```

### 2. New Data Transfer Objects

```csharp
/// <summary>
/// Result of a projection rebuild operation.
/// </summary>
public sealed class ProjectionRebuildResult
{
    /// <summary>
    /// Total number of projections that were rebuilt.
    /// </summary>
    public int TotalRebuilt { get; init; }

    /// <summary>
    /// Total duration of the rebuild operation.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Individual projection rebuild details.
    /// </summary>
    public required List<ProjectionRebuildDetail> Details { get; init; }

    /// <summary>
    /// Whether all projections rebuilt successfully.
    /// </summary>
    public bool Success => Details.All(d => d.Success);

    /// <summary>
    /// List of projections that failed to rebuild.
    /// </summary>
    public List<string> FailedProjections => 
        Details.Where(d => !d.Success).Select(d => d.ProjectionName).ToList();
}

/// <summary>
/// Details of a single projection rebuild.
/// </summary>
public sealed class ProjectionRebuildDetail
{
    public required string ProjectionName { get; init; }
    public bool Success { get; init; }
    public TimeSpan Duration { get; init; }
    public int EventsProcessed { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Current status of projection rebuilding.
/// </summary>
public sealed class ProjectionRebuildStatus
{
    /// <summary>
    /// Whether a rebuild is currently in progress.
    /// </summary>
    public bool IsRebuilding { get; init; }

    /// <summary>
    /// Projections currently being rebuilt.
    /// </summary>
    public required List<string> InProgressProjections { get; init; }

    /// <summary>
    /// Projections waiting to be rebuilt.
    /// </summary>
    public required List<string> QueuedProjections { get; init; }

    /// <summary>
    /// When the current rebuild started.
    /// </summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>
    /// Estimated completion time (null if not rebuilding).
    /// </summary>
    public DateTimeOffset? EstimatedCompletionAt { get; init; }
}
```

### 3. Admin API Endpoints (Sample App)

```csharp
// In Samples/Opossum.Samples.CourseManagement/AdminEndpoints.cs (NEW FILE)

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
            .WithTags("Admin - Projections")
            .WithOpenApi();

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
            IProjectionManager projectionManager) =>
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
```

---

## Implementation Plan

### Phase 1: Core Infrastructure (Day 1 - 2 hours)

#### Step 1: Update `ProjectionOptions.cs`

**File:** `src/Opossum/Projections/ProjectionOptions.cs`

**Changes:**
```csharp
// Add new property with XML documentation
public int MaxConcurrentRebuilds { get; set; } = 4;
```

**Details:**
- Add extensive XML documentation (see API Design section)
- Include disk type recommendations in comments
- Mention CPU/memory/I/O trade-offs

---

#### Step 2: Create New DTOs

**File:** `src/Opossum/Projections/ProjectionRebuildResult.cs` (NEW)

**Content:**
- `ProjectionRebuildResult` class
- `ProjectionRebuildDetail` class
- `ProjectionRebuildStatus` class

**Details:**
- Add XML documentation for all properties
- Include usage examples in comments
- Make classes `sealed` and use `required` properties (C# 14 features)

---

#### Step 3: Update `IProjectionManager.cs`

**File:** `src/Opossum/Projections/IProjectionManager.cs`

**Changes:**
- Add `RebuildAllAsync(bool forceRebuild, CancellationToken)`
- Add `RebuildAsync(string[] projectionNames, CancellationToken)`
- Add `GetRebuildStatusAsync()`

**Details:**
- Add extensive XML documentation
- Include usage examples in comments
- Document threading behavior

---

### Phase 2: Parallel Rebuild Implementation (Day 1 - 3 hours)

#### Step 4: Add Rebuild Tracking to `ProjectionManager.cs`

**File:** `src/Opossum/Projections/ProjectionManager.cs`

**Changes:**
1. Add private fields for tracking:
```csharp
private readonly object _rebuildLock = new();
private ProjectionRebuildStatus _currentRebuildStatus = new()
{
    IsRebuilding = false,
    InProgressProjections = [],
    QueuedProjections = []
};
```

2. Implement `GetRebuildStatusAsync()`:
```csharp
public Task<ProjectionRebuildStatus> GetRebuildStatusAsync()
{
    lock (_rebuildLock)
    {
        return Task.FromResult(_currentRebuildStatus);
    }
}
```

---

#### Step 5: Implement `RebuildAllAsync()`

**File:** `src/Opossum/Projections/ProjectionManager.cs`

**Implementation:**
```csharp
public async Task<ProjectionRebuildResult> RebuildAllAsync(
    bool forceRebuild = false, 
    CancellationToken cancellationToken = default)
{
    var projections = GetRegisteredProjections();
    var projectionsToRebuild = new List<string>();

    // Determine which projections need rebuilding
    foreach (var projectionName in projections)
    {
        var checkpoint = await GetCheckpointAsync(projectionName, cancellationToken);
        
        if (forceRebuild || checkpoint == 0)
        {
            projectionsToRebuild.Add(projectionName);
        }
    }

    if (projectionsToRebuild.Count == 0)
    {
        return new ProjectionRebuildResult
        {
            TotalRebuilt = 0,
            Duration = TimeSpan.Zero,
            Details = []
        };
    }

    // Delegate to RebuildAsync(string[])
    return await RebuildAsync(projectionsToRebuild.ToArray(), cancellationToken);
}
```

**Details:**
- Add logging: "Found {count} projections needing rebuild"
- Handle cancellation gracefully
- Return early if no rebuilds needed

---

#### Step 6: Implement `RebuildAsync(string[])`

**File:** `src/Opossum/Projections/ProjectionManager.cs`

**Implementation:**
```csharp
public async Task<ProjectionRebuildResult> RebuildAsync(
    string[] projectionNames, 
    CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(projectionNames);

    if (projectionNames.Length == 0)
    {
        return new ProjectionRebuildResult
        {
            TotalRebuilt = 0,
            Duration = TimeSpan.Zero,
            Details = []
        };
    }

    // Update rebuild status
    UpdateRebuildStatus(isRebuilding: true, 
        inProgress: [], 
        queued: projectionNames.ToList());

    var overallStopwatch = Stopwatch.StartNew();
    var details = new ConcurrentBag<ProjectionRebuildDetail>();

    try
    {
        // Get MaxConcurrentRebuilds from options (injected via constructor)
        var maxConcurrency = _options?.MaxConcurrentRebuilds ?? 4;

        _logger.LogInformation(
            "Starting parallel rebuild of {Count} projections with max {MaxConcurrency} concurrent rebuilds",
            projectionNames.Length,
            maxConcurrency);

        // Rebuild projections in parallel
        await Parallel.ForEachAsync(
            projectionNames,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxConcurrency,
                CancellationToken = cancellationToken
            },
            async (projectionName, ct) =>
            {
                // Update status: move from queued to in-progress
                MoveToInProgress(projectionName);

                var stopwatch = Stopwatch.StartNew();
                
                try
                {
                    _logger.LogInformation("Rebuilding projection '{ProjectionName}'...", projectionName);

                    // Call existing RebuildAsync(string) method
                    await RebuildAsync(projectionName, ct);

                    stopwatch.Stop();

                    var eventsProcessed = await GetCheckpointAsync(projectionName, ct);

                    details.Add(new ProjectionRebuildDetail
                    {
                        ProjectionName = projectionName,
                        Success = true,
                        Duration = stopwatch.Elapsed,
                        EventsProcessed = (int)eventsProcessed,
                        ErrorMessage = null
                    });

                    _logger.LogInformation(
                        "Projection '{ProjectionName}' rebuilt successfully in {Duration}ms ({EventCount} events)",
                        projectionName,
                        stopwatch.ElapsedMilliseconds,
                        eventsProcessed);
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();

                    details.Add(new ProjectionRebuildDetail
                    {
                        ProjectionName = projectionName,
                        Success = false,
                        Duration = stopwatch.Elapsed,
                        EventsProcessed = 0,
                        ErrorMessage = ex.Message
                    });

                    _logger.LogError(ex, 
                        "Failed to rebuild projection '{ProjectionName}'", 
                        projectionName);
                }
                finally
                {
                    // Update status: remove from in-progress
                    RemoveFromInProgress(projectionName);
                }
            });

        overallStopwatch.Stop();

        var result = new ProjectionRebuildResult
        {
            TotalRebuilt = details.Count(d => d.Success),
            Duration = overallStopwatch.Elapsed,
            Details = details.OrderBy(d => d.ProjectionName).ToList()
        };

        if (result.Success)
        {
            _logger.LogInformation(
                "All {Count} projections rebuilt successfully in {Duration}",
                result.TotalRebuilt,
                overallStopwatch.Elapsed);
        }
        else
        {
            _logger.LogWarning(
                "Projection rebuild completed with errors. Success: {Success}/{Total}. Failed: {Failed}",
                result.TotalRebuilt,
                projectionNames.Length,
                string.Join(", ", result.FailedProjections));
        }

        return result;
    }
    finally
    {
        // Clear rebuild status
        UpdateRebuildStatus(isRebuilding: false, inProgress: [], queued: []);
    }
}
```

**Helper Methods:**
```csharp
private void UpdateRebuildStatus(bool isRebuilding, List<string> inProgress, List<string> queued)
{
    lock (_rebuildLock)
    {
        _currentRebuildStatus = new ProjectionRebuildStatus
        {
            IsRebuilding = isRebuilding,
            InProgressProjections = inProgress,
            QueuedProjections = queued,
            StartedAt = isRebuilding ? DateTimeOffset.UtcNow : null,
            EstimatedCompletionAt = null // TODO: Implement estimation
        };
    }
}

private void MoveToInProgress(string projectionName)
{
    lock (_rebuildLock)
    {
        var queued = _currentRebuildStatus.QueuedProjections.ToList();
        queued.Remove(projectionName);

        var inProgress = _currentRebuildStatus.InProgressProjections.ToList();
        inProgress.Add(projectionName);

        _currentRebuildStatus = _currentRebuildStatus with
        {
            QueuedProjections = queued,
            InProgressProjections = inProgress
        };
    }
}

private void RemoveFromInProgress(string projectionName)
{
    lock (_rebuildLock)
    {
        var inProgress = _currentRebuildStatus.InProgressProjections.ToList();
        inProgress.Remove(projectionName);

        _currentRebuildStatus = _currentRebuildStatus with
        {
            InProgressProjections = inProgress
        };
    }
}
```

**Details:**
- Use `ConcurrentBag<T>` for thread-safe result collection
- Use `Stopwatch` for accurate timing
- Log progress for each projection
- Handle exceptions gracefully (don't fail entire rebuild if one projection fails)
- Update rebuild status in real-time

---

#### Step 7: Inject `ProjectionOptions` into `ProjectionManager`

**File:** `src/Opossum/Projections/ProjectionManager.cs`

**Changes:**
```csharp
private readonly ProjectionOptions _options;

public ProjectionManager(
    OpossumOptions opossumOptions,
    IEventStore eventStore,
    IServiceProvider serviceProvider,
    ProjectionOptions projectionOptions) // ← NEW
{
    // ... existing validation ...
    _options = projectionOptions;
}
```

**File:** `src/Opossum/Projections/ProjectionServiceCollectionExtensions.cs`

**Verify registration:**
```csharp
// Already exists, just verify it's a singleton
services.AddSingleton(options);
```

---

### Phase 3: Update ProjectionDaemon (Day 1 - 1 hour)

#### Step 8: Use New Parallel API in `ProjectionDaemon.cs`

**File:** `src/Opossum/Projections/ProjectionDaemon.cs`

**Replace `RebuildMissingProjectionsAsync()` method:**

```csharp
private async Task RebuildMissingProjectionsAsync(CancellationToken cancellationToken)
{
    _logger.LogInformation("Checking for projections that need rebuilding...");

    // Use the new RebuildAllAsync method (only rebuilds missing projections)
    var result = await _projectionManager.RebuildAllAsync(
        forceRebuild: false, 
        cancellationToken);

    if (result.TotalRebuilt == 0)
    {
        _logger.LogInformation("All projections are up to date (no rebuilds needed)");
        return;
    }

    if (result.Success)
    {
        _logger.LogInformation(
            "Successfully rebuilt {Count} projections in {Duration}. Details: {@Details}",
            result.TotalRebuilt,
            result.Duration,
            result.Details);
    }
    else
    {
        _logger.LogWarning(
            "Projection rebuild completed with {SuccessCount} successes and {FailureCount} failures. " +
            "Failed projections: {FailedProjections}. Details: {@Details}",
            result.TotalRebuilt,
            result.Details.Count - result.TotalRebuilt,
            string.Join(", ", result.FailedProjections),
            result.Details);
    }
}
```

**Details:**
- Remove old sequential foreach loop
- Use new `RebuildAllAsync()` method
- Log detailed results
- Use structured logging (`@Details` for JSON serialization)

---

### Phase 4: Sample App Admin API (Day 1 - 1 hour)

#### Step 9: Create Admin Endpoints File

**File:** `Samples/Opossum.Samples.CourseManagement/AdminEndpoints.cs` (NEW)

**Content:**
- Copy the full implementation from "API Design" section above
- Add extensive code comments explaining:
  - When to use each endpoint
  - Production vs. development usage
  - Authentication recommendations
  - Performance implications

---

#### Step 10: Register Admin Endpoints in `Program.cs`

**File:** `Samples/Opossum.Samples.CourseManagement/Program.cs`

**Add after existing endpoint registrations:**

```csharp
// Sample App Endpoints Registration
app.MapRegisterStudentEndpoint();
// ... existing endpoints ...
app.MapEnrollStudentToCourseEndpoint();

// ============================================================================
// ADMIN ENDPOINTS - Projection Management
// ============================================================================
// These endpoints allow administrators to manually rebuild projections.
//
// DEVELOPMENT USAGE:
// - After database resets/reseeds
// - Testing projection changes
// - Debugging projection issues
//
// PRODUCTION USAGE:
// - After deploying new projection types
// - Fixing buggy projection logic (deploy fix, then rebuild)
// - Disaster recovery (lost projection files)
//
// SECURITY WARNING:
// - In production, add proper authentication/authorization
// - These endpoints can trigger expensive operations
// - Consider rate limiting to prevent abuse
//
// Example: Add authorization requirement
// if (app.Environment.IsProduction())
// {
//     app.MapGroup("/admin").RequireAuthorization("AdminOnly");
// }
// ============================================================================
app.MapProjectionAdminEndpoints();
```

---

#### Step 11: Update Sample App Configuration

**File:** `Samples/Opossum.Samples.CourseManagement/Program.cs`

**Update projection configuration with comments:**

```csharp
// ============================================================================
// PROJECTION SYSTEM CONFIGURATION
// ============================================================================
// Projections are read models derived from events in the event store.
// They are automatically updated as new events are appended.
//
// CONFIGURATION OPTIONS:
//
// 1. EnableAutoRebuild (default: true)
//    - Development: Keep true for fast iteration
//    - Production: Set to false, use admin API for controlled rebuilds
//
// 2. MaxConcurrentRebuilds (default: 4)
//    - Controls how many projections rebuild simultaneously
//    - Higher = faster rebuilds but more CPU/memory/disk I/O usage
//    - Recommendations:
//      * HDD: 2-4 (avoid disk thrashing)
//      * SSD: 4-8 (good balance)
//      * NVMe: 8-16 (fast parallel I/O)
//
// 3. PollingInterval (default: 5 seconds)
//    - How often to check for new events
//    - Lower = more real-time updates but higher CPU usage
//
// 4. BatchSize (default: 1000)
//    - Number of events to process in each batch
//    - Higher = better throughput but more memory usage
//
// ============================================================================
builder.Services.AddProjections(options =>
{
    // Scan this assembly for projection definitions
    options.ScanAssembly(typeof(Program).Assembly);

    // Auto-rebuild missing projections on startup
    // Set to false in production and use POST /admin/projections/rebuild
    options.EnableAutoRebuild = true;

    // Rebuild up to 4 projections concurrently
    // With 4 projections in this sample app, all rebuild simultaneously
    // Expected rebuild time: ~30 seconds (vs. 120 seconds sequential)
    options.MaxConcurrentRebuilds = 4;

    // Poll for new events every 5 seconds
    options.PollingInterval = TimeSpan.FromSeconds(5);

    // Process up to 1000 events in each batch
    options.BatchSize = 1000;
});
```

---

### Phase 5: Testing (Day 2 - 3 hours)

#### Step 12: Unit Tests for `ProjectionOptions`

**File:** `tests/Opossum.UnitTests/Projections/ProjectionOptionsTests.cs`

**Add test:**
```csharp
[Fact]
public void MaxConcurrentRebuilds_DefaultValue_IsFour()
{
    // Arrange & Act
    var options = new ProjectionOptions();

    // Assert
    Assert.Equal(4, options.MaxConcurrentRebuilds);
}

[Theory]
[InlineData(1)]
[InlineData(2)]
[InlineData(8)]
[InlineData(16)]
public void MaxConcurrentRebuilds_CanBeConfigured(int value)
{
    // Arrange
    var options = new ProjectionOptions
    {
        MaxConcurrentRebuilds = value
    };

    // Assert
    Assert.Equal(value, options.MaxConcurrentRebuilds);
}
```

---

#### Step 13: Integration Tests for Parallel Rebuilding

**File:** `tests/Opossum.IntegrationTests/Projections/ParallelRebuildTests.cs` (NEW)

**Tests to implement:**

```csharp
[Fact]
public async Task RebuildAllAsync_WithMultipleProjections_RebuildsInParallel()
{
    // Arrange: Add events and register 4 projections
    
    // Act: Rebuild all
    var result = await _projectionManager.RebuildAllAsync(forceRebuild: false);
    
    // Assert:
    // - TotalRebuilt = 4
    // - All Success = true
    // - Duration < (4 × single projection rebuild time)
}

[Fact]
public async Task RebuildAsync_WithSpecificProjections_RebuildsOnlyThose()
{
    // Arrange: 4 projections, delete 2 checkpoints
    
    // Act: Rebuild only 2
    var result = await _projectionManager.RebuildAsync(
        new[] { "Projection1", "Projection2" });
    
    // Assert: Only 2 rebuilt
}

[Fact]
public async Task RebuildAllAsync_WithForceRebuild_RebuildsAll()
{
    // Arrange: All projections have checkpoints
    
    // Act: Force rebuild
    var result = await _projectionManager.RebuildAllAsync(forceRebuild: true);
    
    // Assert: All 4 rebuilt even though checkpoints existed
}

[Fact]
public async Task RebuildAsync_WhenOneProjectionFails_ContinuesWithOthers()
{
    // Arrange: Create projection that throws exception
    
    // Act: Rebuild all
    var result = await _projectionManager.RebuildAllAsync();
    
    // Assert:
    // - Success = false
    // - TotalRebuilt = 3 (1 failed)
    // - FailedProjections contains the failed one
}

[Fact]
public async Task GetRebuildStatusAsync_DuringRebuild_ReturnsProgress()
{
    // Arrange: Start slow rebuild in background
    
    // Act: Get status while rebuilding
    var status = await _projectionManager.GetRebuildStatusAsync();
    
    // Assert:
    // - IsRebuilding = true
    // - InProgressProjections.Count > 0
}

[Fact]
public async Task RebuildAsync_RespectsMaxConcurrentRebuilds()
{
    // Arrange: 8 projections, MaxConcurrentRebuilds = 2
    
    // Act & Assert: Monitor that at most 2 rebuild simultaneously
    // (This is tricky - may need to use mocking or timing analysis)
}
```

---

#### Step 14: Integration Tests for Admin Endpoints

**File:** `tests/Opossum.IntegrationTests/SampleApp/AdminEndpointTests.cs` (NEW)

**Tests to implement:**

```csharp
[Fact]
public async Task POST_RebuildAll_RebuildsAllProjections()
{
    // Arrange: WebApplicationFactory with sample app
    
    // Act
    var response = await _client.PostAsync("/admin/projections/rebuild", null);
    
    // Assert
    response.EnsureSuccessStatusCode();
    var result = await response.Content.ReadFromJsonAsync<ProjectionRebuildResult>();
    Assert.True(result!.Success);
}

[Fact]
public async Task POST_RebuildSpecific_RebuildsOnlyThatProjection()
{
    // Arrange
    
    // Act
    var response = await _client.PostAsync(
        "/admin/projections/CourseDetails/rebuild", null);
    
    // Assert
    response.EnsureSuccessStatusCode();
}

[Fact]
public async Task GET_Status_ReturnsCurrentRebuildStatus()
{
    // Arrange
    
    // Act
    var response = await _client.GetAsync("/admin/projections/status");
    
    // Assert
    response.EnsureSuccessStatusCode();
    var status = await response.Content.ReadFromJsonAsync<ProjectionRebuildStatus>();
    Assert.NotNull(status);
}
```

---

#### Step 15: Manual Testing Checklist

**Development Environment:**

1. ✅ Delete all projection files and checkpoints
2. ✅ Start sample app
3. ✅ Verify logs show parallel rebuilding
4. ✅ Verify all 4 projections rebuild in ~30 seconds (not 120)
5. ✅ Verify HTTP requests work during rebuild (may be slower)
6. ✅ Check Scalar UI shows admin endpoints
7. ✅ Test POST `/admin/projections/rebuild`
8. ✅ Test POST `/admin/projections/CourseDetails/rebuild`
9. ✅ Test GET `/admin/projections/status`

**Performance Testing:**

1. ✅ Seed 10,000 events
2. ✅ Measure sequential rebuild time
3. ✅ Measure parallel rebuild time (MaxConcurrentRebuilds = 4)
4. ✅ Verify 3-4x speedup
5. ✅ Monitor CPU usage during rebuild
6. ✅ Monitor memory usage during rebuild
7. ✅ Test HTTP request latency during rebuild

---

### Phase 6: Documentation (Day 2 - 1 hour)

#### Step 16: Update README

**File:** `README.md`

**Add section:**

```markdown
## Projection Rebuilding

Projections can be rebuilt from the event store when needed.

### Automatic Rebuilding (Development)

By default, projections with missing checkpoints are automatically rebuilt on application startup:

```csharp
builder.Services.AddProjections(options =>
{
    options.EnableAutoRebuild = true; // Default
    options.MaxConcurrentRebuilds = 4; // Rebuild 4 at once
});
```

### Manual Rebuilding (Production)

In production, disable auto-rebuild and use the admin API:

```csharp
builder.Services.AddProjections(options =>
{
    options.EnableAutoRebuild = false;
});
```

**Rebuild all projections:**
```bash
curl -X POST https://api.example.com/admin/projections/rebuild
```

**Rebuild specific projection:**
```bash
curl -X POST https://api.example.com/admin/projections/CourseDetails/rebuild
```

**Check rebuild status:**
```bash
curl https://api.example.com/admin/projections/status
```

### When to Rebuild Projections

✅ **Common Use Cases:**
- Adding new projection types in production
- Fixing bugs in projection logic
- Disaster recovery (lost projection files)
- Development database resets

❌ **Should NOT Happen:**
- Random missing projection files (indicates a bug)
- Routine production operations

### Performance Tuning

Configure `MaxConcurrentRebuilds` based on your disk type:

| Disk Type | Recommended Value |
|-----------|-------------------|
| HDD | 2-4 |
| SSD | 4-8 |
| NVMe | 8-16 |
| RAID | 16-32 |

Higher values = faster rebuilds but more resource usage.
```

---

#### Step 17: Create Feature Documentation

**File:** `docs/features/parallel-projection-rebuilding.md` (THIS FILE)

**Update status:**
```markdown
**Status:** ✅ Implemented
```

---

### Phase 7: Final Verification (Day 2 - 30 minutes)

#### Step 18: Pre-Completion Checklist

```markdown
## Pre-Completion Verification

- [ ] ✅ Build successful: `dotnet build`
- [ ] ✅ Unit tests passing: `dotnet test tests/Opossum.UnitTests/`
- [ ] ✅ Integration tests passing: `dotnet test tests/Opossum.IntegrationTests/`
- [ ] ✅ Sample app runs without errors
- [ ] ✅ Manual testing completed (see Step 15)
- [ ] ✅ Performance improvements verified (3-4x speedup)
- [ ] ✅ Admin endpoints work correctly
- [ ] ✅ Logging is detailed and useful
- [ ] ✅ All copilot-instructions followed
- [ ] ✅ Documentation updated (README, feature doc)
- [ ] ✅ Code comments are comprehensive
```

---

## Testing Strategy

### Unit Tests

**Coverage:**
- `ProjectionOptions` validation
- `ProjectionRebuildResult` serialization
- Helper method logic

**Files:**
- `tests/Opossum.UnitTests/Projections/ProjectionOptionsTests.cs`
- `tests/Opossum.UnitTests/Projections/ProjectionRebuildResultTests.cs` (NEW)

---

### Integration Tests

**Coverage:**
- Parallel rebuilding behavior
- Manual API methods (`RebuildAllAsync`, `RebuildAsync(string[])`)
- Rebuild status tracking
- Error handling (partial failures)
- Admin endpoints

**Files:**
- `tests/Opossum.IntegrationTests/Projections/ParallelRebuildTests.cs` (NEW)
- `tests/Opossum.IntegrationTests/SampleApp/AdminEndpointTests.cs` (NEW)

---

### Performance Tests

**Benchmarks to add:**

```csharp
// tests/Opossum.BenchmarkTests/Projections/ParallelRebuildBenchmarks.cs (NEW)

[Benchmark]
public async Task SequentialRebuild_4Projections_10000Events()
{
    // MaxConcurrentRebuilds = 1
}

[Benchmark]
public async Task ParallelRebuild_4Projections_10000Events_Concurrency2()
{
    // MaxConcurrentRebuilds = 2
}

[Benchmark]
public async Task ParallelRebuild_4Projections_10000Events_Concurrency4()
{
    // MaxConcurrentRebuilds = 4
}

[Benchmark]
public async Task ParallelRebuild_4Projections_10000Events_Concurrency8()
{
    // MaxConcurrentRebuilds = 8
}
```

**Expected results:**
- Sequential: ~120 seconds
- Concurrency 2: ~60 seconds (2x)
- Concurrency 4: ~30 seconds (4x)
- Concurrency 8: ~30 seconds (no improvement - only 4 projections)

---

## Migration Guide

### For Existing Opossum Users

**No breaking changes!** This is a backward-compatible enhancement.

**Migration steps:**

1. **Update Opossum NuGet package to v1.1.0**

2. **Optional: Configure concurrency (recommended)**
```csharp
builder.Services.AddProjections(options =>
{
    options.ScanAssembly(typeof(Program).Assembly);
    options.EnableAutoRebuild = true;
    options.MaxConcurrentRebuilds = 4; // ← NEW (defaults to 4 if not set)
});
```

3. **Optional: Add admin endpoints (recommended for production)**
```csharp
// In Program.cs
app.MapProjectionAdminEndpoints();
```

4. **Optional: Disable auto-rebuild in production**
```csharp
if (builder.Environment.IsProduction())
{
    builder.Services.AddProjections(options =>
    {
        options.EnableAutoRebuild = false; // Use manual API
    });
}
```

---

## Success Criteria

✅ **Performance:**
- 4 projections rebuild in ≤35 seconds (vs. 120 seconds baseline)
- No OOM errors with 8 concurrent rebuilds
- HTTP requests still functional during rebuild (may be slower)

✅ **Functionality:**
- `RebuildAllAsync()` correctly rebuilds all projections
- `RebuildAsync(string[])` correctly rebuilds specific projections
- `GetRebuildStatusAsync()` returns accurate real-time status
- Admin endpoints work correctly

✅ **Quality:**
- All tests pass (unit + integration)
- Code comments are comprehensive
- Logging is detailed and useful
- Documentation is complete

✅ **Production-Ready:**
- Configurable concurrency prevents resource exhaustion
- Manual rebuild API for controlled production rebuilds
- Partial failure handling (1 failed projection doesn't stop others)

---

## Future Enhancements (Not in Scope)

### Phase 8 (Future): Advanced Features

1. **Corruption Detection**
   - Detect missing projection files (checkpoint exists but files missing)
   - Auto-rebuild only corrupted projections

2. **Rebuild Estimation**
   - Estimate completion time based on event count
   - Show progress percentage

3. **Priority-Based Rebuilding**
   - Configure projection rebuild order
   - Rebuild critical projections first

4. **Incremental Rebuilding**
   - Rebuild only missing projection files (not entire projection)
   - Requires tracking which entities are missing

5. **Rebuild Scheduling**
   - Schedule rebuilds during off-peak hours
   - Cron-based rebuild triggers

---

## Implementation Checklist (For Tomorrow)

Use this checklist to track progress:

### Phase 1: Core Infrastructure
- [ ] Step 1: Update `ProjectionOptions.cs`
- [ ] Step 2: Create `ProjectionRebuildResult.cs`
- [ ] Step 3: Update `IProjectionManager.cs`

### Phase 2: Parallel Rebuild
- [ ] Step 4: Add rebuild tracking to `ProjectionManager.cs`
- [ ] Step 5: Implement `RebuildAllAsync()`
- [ ] Step 6: Implement `RebuildAsync(string[])`
- [ ] Step 7: Inject `ProjectionOptions` into `ProjectionManager`

### Phase 3: Update Daemon
- [ ] Step 8: Update `ProjectionDaemon.cs`

### Phase 4: Sample App API
- [ ] Step 9: Create `AdminEndpoints.cs`
- [ ] Step 10: Register endpoints in `Program.cs`
- [ ] Step 11: Update configuration comments

### Phase 5: Testing
- [ ] Step 12: Unit tests for `ProjectionOptions`
- [ ] Step 13: Integration tests for parallel rebuilding
- [ ] Step 14: Integration tests for admin endpoints
- [ ] Step 15: Manual testing

### Phase 6: Documentation
- [ ] Step 16: Update README
- [ ] Step 17: Update feature doc status

### Phase 7: Final Verification
- [ ] Step 18: Complete pre-completion checklist

---

## Notes for Tomorrow

### Key Design Decisions Made

1. **Default `MaxConcurrentRebuilds = 4`**
   - Balanced for most scenarios (HDD/SSD)
   - Not `Environment.ProcessorCount` (I/O-bound, not CPU-bound)

2. **`RebuildAsync(string[])` as core implementation**
   - `RebuildAllAsync()` delegates to it
   - Single source of truth for parallel logic

3. **Non-blocking background rebuilds**
   - App serves requests during rebuild
   - Performance degradation is acceptable (development)
   - Production should use `EnableAutoRebuild = false`

4. **Partial failure handling**
   - 1 failed projection doesn't stop others
   - Return detailed `ProjectionRebuildResult` with individual statuses

5. **Real-time status tracking**
   - `GetRebuildStatusAsync()` returns current progress
   - Enables monitoring long-running rebuilds

### Potential Challenges

1. **Thread-safe status updates**
   - Use `lock` for simplicity (not async-friendly)
   - Alternative: Use `SemaphoreSlim` if needed

2. **Testing parallel behavior**
   - Hard to verify "at most N concurrent"
   - May need timing-based assertions or mocking

3. **Benchmark variance**
   - Disk I/O is non-deterministic
   - Run benchmarks multiple times, take average

---

## Questions to Consider Tomorrow

1. Should `MaxConcurrentRebuilds = 0` mean "unlimited"? Or throw exception?
2. Should we add a "dry run" mode to preview what would rebuild?
3. Should admin endpoints require authentication by default?
4. Should we add metrics (Prometheus/OpenTelemetry) for rebuild operations?

---

**END OF SPECIFICATION**

This document serves as the complete blueprint for implementing parallel projection rebuilding. Follow the step-by-step implementation plan, and verify each phase before moving to the next.

Good luck tomorrow! 🚀
