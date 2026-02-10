using Opossum.DependencyInjection;
using Opossum.Exceptions;
using Opossum.Mediator;
using Opossum.Projections;
using Opossum.Samples.CourseManagement.CourseCreation;
using Opossum.Samples.CourseManagement.CourseDetails;
using Opossum.Samples.CourseManagement.CourseEnrollment;
using Opossum.Samples.CourseManagement.CourseShortInfo;
using Opossum.Samples.CourseManagement.CourseStudentLimitModification;
using Opossum.Samples.CourseManagement.StudentDetails;
using Opossum.Samples.CourseManagement.StudentRegistration;
using Opossum.Samples.CourseManagement.StudentShortInfo;
using Opossum.Samples.CourseManagement.StudentSubscription;


var builder = WebApplication.CreateBuilder(args);

// Configure JSON serialization to use string enums (for API responses)
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Configure OpenAPI to use string enums (for Swagger schema)
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Add event sourcing framework
builder.Services.AddOpossum(options =>
{
    options.RootPath = "D:\\Database";
    options.AddContext("OpossumSampleApp");
    //TODO: multiple context support
    //options.AddContext("ExampleAdditionalContext");
});

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

// Add mediator for command/query handling
builder.Services.AddMediator();

// Add Problem Details support (RFC 7807)
builder.Services.AddProblemDetails();

// Add OpenAPI with native .NET support (respects JsonStringEnumConverter)
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure OpenAPI and Scalar UI
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // Native OpenAPI endpoint at /openapi/v1.json
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("Opossum Event Sourcing API");
        options.WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    }).RequireHost("*:*"); // Accessible at /scalar/v1

    // Redirect root to Scalar UI
    app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();
}

// ============================================================================
// GLOBAL EXCEPTION HANDLER - Maps Opossum exceptions to HTTP responses
// ============================================================================
app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var exceptionHandlerFeature = context.Features.Get<IExceptionHandlerFeature>();
        var exception = exceptionHandlerFeature?.Error;

        // Map ConcurrencyException to HTTP 409 Conflict
        // This occurs when DCB detects a stale decision model (optimistic concurrency control)
        if (exception is ConcurrencyException concurrencyEx)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Concurrency Conflict",
                Detail = "The operation failed because the resource was modified by another request. " +
                         "This typically means an email address was already taken or a course reached capacity. " +
                         "Please refresh and try again.",
                Instance = context.Request.Path,
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.8"
            });
            return;
        }

        // Map AppendConditionFailedException to HTTP 409 Conflict
        // Similar to ConcurrencyException - DCB append condition failed
        if (exception is AppendConditionFailedException appendEx)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Append Condition Failed",
                Detail = "The operation failed because an append condition was not met. " +
                         "This typically indicates a constraint violation (e.g., duplicate email, course full). " +
                         "Please refresh and try again.",
                Instance = context.Request.Path,
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.8"
            });
            return;
        }

        // Default handler for other exceptions
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Internal Server Error",
            Detail = app.Environment.IsDevelopment() ? exception?.Message : "An unexpected error occurred.",
            Instance = context.Request.Path,
            Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1"
        });
    });
});

app.UseHttpsRedirection();

// Sample App Endpoints Registration
app.MapRegisterStudentEndpoint();
app.MapGetStudentsShortInfoEndpoint();
app.MapGetStudentDetailsEndpoint();
app.MapUpdateStudentSubscriptionEndpoint();
app.MapCreateCourseEndpoint();
app.MapGetCoursesShortInfoEndpoint();
app.MapGetCourseDetailsEndpoint();
app.MapModifyCourseStudentLimitEndpoint();
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
Opossum.Samples.CourseManagement.AdminEndpoints.MapProjectionAdminEndpoints(app);

app.Run();

// ============================================================================
// MAKE PROGRAM CLASS ACCESSIBLE FOR INTEGRATION TESTS
// ============================================================================

public partial class Program { }
