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

// ============================================================================
// EVENT SOURCING FRAMEWORK CONFIGURATION
// ============================================================================
builder.Services.AddOpossum(options =>
{
    // Add contexts from configuration
    var contexts = builder.Configuration.GetSection("Opossum:Contexts").Get<string[]>();
    if (contexts != null)
    {
        foreach (var context in contexts)
        {
            options.AddContext(context);
        }
    }

    // Bind all properties from configuration (RootPath, FlushEventsImmediately, etc.)
    builder.Configuration.GetSection("Opossum").Bind(options);

    // AFTER binding, ensure RootPath is valid for the current platform
    // This handles cases where config has Windows path (D:\Database) on Linux
    if (string.IsNullOrWhiteSpace(options.RootPath))
    {
        // No path configured - use platform default
        options.RootPath = OperatingSystem.IsWindows() 
            ? Path.Combine("D:", "Database")  // Windows: D:\Database
            : "/var/opossum/data";            // Linux: /var/opossum/data
    }
    else if (!Path.IsPathRooted(options.RootPath))
    {
        // Path is not rooted (e.g., Windows drive letter on Linux)
        // Replace with platform default
        options.RootPath = OperatingSystem.IsWindows() 
            ? Path.Combine("D:", "Database")  // Windows: D:\Database
            : "/var/opossum/data";            // Linux: /var/opossum/data
    }
});

// ============================================================================
// PROJECTION SYSTEM CONFIGURATION
// ============================================================================
builder.Services.AddProjections(options =>
{
    // Bind from appsettings.json "Projections" section
    builder.Configuration.GetSection("Projections").Bind(options);

    // Assembly scanning must be done in code (not possible via JSON)
    options.ScanAssembly(typeof(Program).Assembly);
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
