using Microsoft.AspNetCore.Mvc.Testing;

namespace Opossum.Samples.CourseManagement.IntegrationTests;

public class IntegrationTestFixture : IDisposable
{
    private readonly string _testDatabasePath;

    public HttpClient Client { get; }
    public WebApplicationFactory<Program> Factory { get; }

    public IntegrationTestFixture()
    {
        // Create unique temporary folder for each test run
        _testDatabasePath = Path.Combine(Path.GetTempPath(), $"OpossumTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDatabasePath);

        // Create factory - Note: We cannot easily override Opossum configuration
        // because it's configured via AddOpossum in Program.cs
        // For proper test isolation, each test should use unique IDs
        Factory = new WebApplicationFactory<Program>();

        Client = Factory.CreateClient();
    }

    public void Dispose()
    {
        Client?.Dispose();
        Factory?.Dispose();

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

[CollectionDefinition("Integration Tests")]
public class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
