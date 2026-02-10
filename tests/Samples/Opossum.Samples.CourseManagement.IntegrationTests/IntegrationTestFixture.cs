using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Opossum.Configuration;
using Opossum.Projections;

namespace Opossum.Samples.CourseManagement.IntegrationTests;

/// <summary>
/// Base fixture for integration tests with isolated database per test collection.
/// Each collection gets its own temporary database that's cleaned up after tests complete.
/// </summary>
public class IntegrationTestFixture : IDisposable
{
    private readonly string _testDatabasePath;
    private bool _disposed;

    public HttpClient Client { get; }
    public WebApplicationFactory<Program> Factory { get; }
    public string TestDatabasePath => _testDatabasePath;

    public IntegrationTestFixture()
    {
        // Create unique temporary folder for this test collection
        _testDatabasePath = Path.Combine(Path.GetTempPath(), $"OpossumTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDatabasePath);

        // Create factory with configuration override
        // Uses ConfigureServices to ensure overrides happen AFTER configuration is built
        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                // Configure services to override options AFTER configuration binding
                // This is the ONLY reliable way to override configuration-bound options
                builder.ConfigureServices((context, services) =>
                {
                    // Remove existing OpossumOptions registration
                    var opossumDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(OpossumOptions));
                    if (opossumDescriptor != null)
                    {
                        services.Remove(opossumDescriptor);
                    }

                    // Create new options with test database path
                    var testOptions = new OpossumOptions
                    {
                        RootPath = _testDatabasePath
                    };

                    // Add context from configuration
                    var contexts = context.Configuration.GetSection("Opossum:Contexts").Get<string[]>();
                    if (contexts != null)
                    {
                        foreach (var ctx in contexts)
                        {
                            testOptions.AddContext(ctx);
                        }
                    }

                    // Register the test-specific options
                    services.AddSingleton(testOptions);

                    // Override ProjectionOptions using PostConfigure (runs after all other configuration)
                    services.PostConfigure<ProjectionOptions>(options =>
                    {
                        options.EnableAutoRebuild = false;
                    });
                });
            });

        Client = Factory.CreateClient();

        // Seed test database with sample domain data
        SeedTestDataAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Seeds the test database with sample domain events for all projection types.
    /// Creates students, courses, and enrollments so projections have real data to process.
    /// </summary>
    private async Task SeedTestDataAsync()
    {
        try
        {
            // Create 2 students
            for (int i = 1; i <= 2; i++)
            {
                var studentId = Guid.NewGuid();
                await Client.PostAsJsonAsync("/students", new
                {
                    FirstName = $"Test{i}",
                    LastName = $"Student{i}",
                    Email = $"test.student{i}@example.com"
                });
            }

            // Create 2 courses
            for (int i = 1; i <= 2; i++)
            {
                var courseId = Guid.NewGuid();
                await Client.PostAsJsonAsync("/courses", new
                {
                    CourseId = courseId,
                    Name = $"Test Course {i}",
                    Description = $"Description for test course {i}",
                    StudentLimit = 10
                });
            }

            // Give the system a moment to process events
            await Task.Delay(200);
        }
        catch
        {
            // Seeding is best-effort - if it fails, tests will still run
            // but might have different behavior (empty database)
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            // Dispose client and factory FIRST to release file locks
            Client?.Dispose();
            Factory?.Dispose();

            // Give the OS a moment to release file locks
            Thread.Sleep(100);

            // Now try to cleanup the database
            CleanupDatabase();
        }
        catch (Exception ex)
        {
            // Log cleanup failure but don't throw - tests already ran
            Console.WriteLine($"Warning: Failed to cleanup test database at {_testDatabasePath}: {ex.Message}");
        }
        finally
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    private void CleanupDatabase()
    {
        if (!Directory.Exists(_testDatabasePath))
            return;

        try
        {
            // Try simple delete first
            Directory.Delete(_testDatabasePath, recursive: true);
        }
        catch (IOException)
        {
            // If simple delete fails, try aggressive cleanup
            TryAggressiveCleanup(_testDatabasePath);
        }
        catch (UnauthorizedAccessException)
        {
            // If access denied, try aggressive cleanup
            TryAggressiveCleanup(_testDatabasePath);
        }
    }

    private static void TryAggressiveCleanup(string path)
    {
        try
        {
            // First, remove read-only attributes
            var directory = new DirectoryInfo(path);
            if (directory.Exists)
            {
                SetAttributesNormal(directory);

                // Wait a bit for file handles to be released
                Thread.Sleep(200);

                // Try delete again
                directory.Delete(true);
            }
        }
        catch
        {
            // If aggressive cleanup also fails, just leave it
            // The OS will eventually clean up temp files
        }
    }

    private static void SetAttributesNormal(DirectoryInfo directory)
    {
        try
        {
            foreach (var subDir in directory.GetDirectories())
            {
                SetAttributesNormal(subDir);
            }

            foreach (var file in directory.GetFiles())
            {
                file.Attributes = FileAttributes.Normal;
            }

            directory.Attributes = FileAttributes.Normal;
        }
        catch
        {
            // Best effort - ignore failures
        }
    }
}

/// <summary>
/// Collection definition for general integration tests that can share a database.
/// Tests in this collection run sequentially and share the same fixture instance.
/// </summary>
[CollectionDefinition("Integration Tests")]
public class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>
{
}

/// <summary>
/// Collection definition for admin tests that need isolated state.
/// These tests get their own fixture instance with a separate database.
/// </summary>
[CollectionDefinition("Admin Tests")]
public class AdminTestCollection : ICollectionFixture<IntegrationTestFixture>
{
}

/// <summary>
/// Collection definition for diagnostic tests.
/// These tests get their own fixture instance to ensure clean state.
/// </summary>
[CollectionDefinition("Diagnostic Tests")]
public class DiagnosticTestCollection : ICollectionFixture<IntegrationTestFixture>
{
}
