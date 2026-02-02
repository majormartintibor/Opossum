using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Opossum.DependencyInjection;
using Opossum.Mediator;

namespace Opossum.IntegrationTests;

/// <summary>
/// Test fixture that provides configured Opossum services for integration tests.
/// 
/// IMPORTANT: This fixture is shared across all tests in a collection.
/// Use GetIsolatedServiceScope() to get per-test service isolation.
/// </summary>
public class OpossumFixture : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string _baseStoragePath;
    private readonly List<string> _testStoragePaths = new();
    private readonly object _pathLock = new();

    public IMediator Mediator { get; }
    public IEventStore EventStore { get; }

    public OpossumFixture()
    {
        // Create base directory for all tests in this fixture
        _baseStoragePath = Path.Combine(
            Path.GetTempPath(),
            "OpossumIntegrationTests",
            Guid.NewGuid().ToString());

        var services = new ServiceCollection();

        // Configure Opossum with test storage path
        services.AddOpossum(options =>
        {
            options.RootPath = _baseStoragePath;
            options.AddContext("CourseManagement");
            options.AddContext("TestContext");
        });

        // Add mediator
        services.AddMediator();

        // Add logging for tests
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        _serviceProvider = services.BuildServiceProvider();

        Mediator = _serviceProvider.GetRequiredService<IMediator>();
        EventStore = _serviceProvider.GetRequiredService<IEventStore>();
    }

    /// <summary>
    /// Creates an isolated service scope for a test with its own storage directory.
    /// This provides proper test isolation when running concurrent operations within a test.
    /// </summary>
    /// <returns>A service scope with isolated EventStore and Mediator instances</returns>
    public IServiceScope GetIsolatedServiceScope()
    {
        // Create unique storage path for this test
        var testStoragePath = Path.Combine(
            Path.GetTempPath(),
            "OpossumIntegrationTests",
            Guid.NewGuid().ToString());

        lock (_pathLock)
        {
            _testStoragePaths.Add(testStoragePath);
        }

        var services = new ServiceCollection();

        // Configure Opossum with isolated storage path
        services.AddOpossum(options =>
        {
            options.RootPath = testStoragePath;
            options.AddContext("CourseManagement");
            options.AddContext("TestContext");
        });

        services.AddMediator();

        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        var serviceProvider = services.BuildServiceProvider();
        return serviceProvider.CreateScope();
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();

        // Clean up base storage
        if (Directory.Exists(_baseStoragePath))
        {
            try
            {
                Directory.Delete(_baseStoragePath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }

        // Clean up all isolated test storage paths
        lock (_pathLock)
        {
            foreach (var path in _testStoragePaths)
            {
                if (Directory.Exists(path))
                {
                    try
                    {
                        Directory.Delete(path, recursive: true);
                    }
                    catch
                    {
                        // Ignore cleanup errors in tests
                    }
                }
            }
        }
    }
}
