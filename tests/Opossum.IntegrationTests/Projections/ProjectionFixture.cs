using Opossum.Configuration;
using Opossum.DependencyInjection;
using Opossum.Projections;

namespace Opossum.IntegrationTests.Projections;

/// <summary>
/// Test fixture for projection integration tests with file system
/// </summary>
public class ProjectionFixture : IDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public IEventStore EventStore { get; }
    public IProjectionManager ProjectionManager { get; }
    public OpossumOptions OpossumOptions { get; }
    public ProjectionOptions ProjectionOptions { get; }
    public string TestStoragePath { get; }

    public ProjectionFixture()
    {
        // Create unique temp directory for this test run
        TestStoragePath = Path.Combine(
            Path.GetTempPath(),
            "OpossumProjectionTests",
            Guid.NewGuid().ToString());

        var services = new ServiceCollection();

        // Configure Opossum with test storage path
        services.AddOpossum(options =>
        {
            options.RootPath = TestStoragePath;
            options.AddContext("TestContext");
        });

        // Configure projections (without auto-discovery for manual testing)
        services.AddProjections(options =>
        {
            options.PollingInterval = TimeSpan.FromSeconds(1); // Fast polling for tests
            options.BatchSize = 100;
            options.EnableAutoRebuild = false; // Manual control in tests
        });

        // Add logging for tests
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        _serviceProvider = services.BuildServiceProvider();

        EventStore = _serviceProvider.GetRequiredService<IEventStore>();
        ProjectionManager = _serviceProvider.GetRequiredService<IProjectionManager>();
        OpossumOptions = _serviceProvider.GetRequiredService<OpossumOptions>();
        ProjectionOptions = _serviceProvider.GetRequiredService<ProjectionOptions>();
    }

    public T GetService<T>() where T : notnull
    {
        return _serviceProvider.GetRequiredService<T>();
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();

        // Clean up test storage
        if (Directory.Exists(TestStoragePath))
        {
            try
            {
                Directory.Delete(TestStoragePath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }
}
