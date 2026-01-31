using Opossum.DependencyInjection;
using Opossum.Mediator;
using Opossum.Samples.CourseManagement.CourseCreation;
using Opossum.Samples.CourseManagement.CourseShortInfo;
using Opossum.Samples.CourseManagement.CourseStudentLimitModification;
using Opossum.Samples.CourseManagement.StudentRegistration;
using Opossum.Samples.CourseManagement.StudentShortInfo;
using Opossum.Samples.CourseManagement.StudentSubscription;

var builder = WebApplication.CreateBuilder(args);

// Add event sourcing framework
builder.Services.AddOpossum(options =>
{
    options.RootPath = "D:\\Database";
    options.AddContext("OpossumSampleApp");
    //TODO: multiple context support
    //options.AddContext("ExampleAdditionalContext");
});

// Add mediator for command/query handling
builder.Services.AddMediator();

// Add Swagger/OpenAPI support
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Sample API - Opossum Event Sourcing Sample",
        Version = "v1",
        Description = "A sample application demonstrating the Opossum event sourcing framework with CQRS"
    });
});

var app = builder.Build();

// Configure Swagger UI
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Sample API v1");
        options.RoutePrefix = string.Empty; // Serve Swagger UI at root
    });
}

app.UseHttpsRedirection();

// Sample App Endpoints Registration
app.MapRegisterStudentEndpoint();
app.MapGetStudentsShortInfoEndpoint();
app.MapUpdateStudentSubscriptionEndpoint();
app.MapCreateCourseEndpoint();
app.MapGetCoursesShortInfoEndpoint();
app.MapModifyCourseStudentLimitEndpoint();

app.Run();

// ============================================================================
// MAKE PROGRAM CLASS ACCESSIBLE FOR INTEGRATION TESTS
// ============================================================================

public partial class Program { }
