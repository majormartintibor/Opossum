using Opossum.DependencyInjection;
using Opossum.Mediator;
using Opossum.Samples.CourseManagement.CourseCreation;
using Opossum.Samples.CourseManagement.CourseEnrollment;
using Opossum.Samples.CourseManagement.CourseShortInfo;
using Opossum.Samples.CourseManagement.CourseStudentLimitModification;
using Opossum.Samples.CourseManagement.StudentRegistration;
using Opossum.Samples.CourseManagement.StudentShortInfo;
using Opossum.Samples.CourseManagement.StudentSubscription;
using Scalar.AspNetCore;
using System.Text.Json.Serialization;

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

// Add projection system
builder.Services.AddProjections(options =>
{
    options.ScanAssembly(typeof(Program).Assembly);
    options.PollingInterval = TimeSpan.FromSeconds(5);
    options.BatchSize = 1000;
    options.EnableAutoRebuild = true;
});

// Add mediator for command/query handling
builder.Services.AddMediator();

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

app.UseHttpsRedirection();

// Sample App Endpoints Registration
app.MapRegisterStudentEndpoint();
app.MapGetStudentsShortInfoEndpoint();
app.MapUpdateStudentSubscriptionEndpoint();
app.MapCreateCourseEndpoint();
app.MapGetCoursesShortInfoEndpoint();
app.MapModifyCourseStudentLimitEndpoint();
app.MapEnrollStudentToCourseEndpoint();

app.Run();

// ============================================================================
// MAKE PROGRAM CLASS ACCESSIBLE FOR INTEGRATION TESTS
// ============================================================================

public partial class Program { }
