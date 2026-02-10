using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Opossum.Configuration;

namespace Opossum.Samples.CourseManagement.IntegrationTests;

/// <summary>
/// Base fixture for integration tests with isolated database per test class.
/// Each test class that uses this fixture gets its own unique temporary database.
/// </summary>
public class IntegrationTestFixture : IDisposable
{
    private readonly string _testDatabasePath;

    public HttpClient Client { get; }
    public WebApplicationFactory<Program> Factory { get; }

    public IntegrationTestFixture()
    {
        // Create unique temporary folder for THIS FIXTURE INSTANCE
        // Each test class gets its own fixture instance with its own database
        _testDatabasePath = Path.Combine(Path.GetTempPath(), $"OpossumTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDatabasePath);

        // Set environment variable BEFORE creating the factory
        // This is the ONLY way to override configuration before Program.cs runs
        Environment.SetEnvironmentVariable("Opossum__RootPath", _testDatabasePath);

        // Create factory with Testing environment
        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
            });

        Client = Factory.CreateClient();
    }

    public void Dispose()
    {
        Client?.Dispose();
        Factory?.Dispose();

        // Clear the environment variable
        Environment.SetEnvironmentVariable("Opossum__RootPath", null);

        // Cleanup test database
        if (Directory.Exists(_testDatabasePath))
        {
            try
            {
                Directory.Delete(_testDatabasePath, recursive: true);
            }
            catch
            {
                // Best effort cleanup - ignore if files are locked
            }
        }

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Collection definition for tests that can share a database.
/// Tests in this collection run sequentially and share the same fixture instance.
/// Use this for tests that don't modify state significantly.
/// </summary>
[CollectionDefinition("Integration Tests")]
public class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>
{
}

/// <summary>
/// Collection definition for admin tests that need isolated state.
/// These tests get their own fixture instance (different from "Integration Tests").
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
