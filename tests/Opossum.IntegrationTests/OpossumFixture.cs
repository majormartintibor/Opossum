using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Opossum.DependencyInjection;
using Opossum.Mediator;

namespace Opossum.IntegrationTests;

/// <summary>
/// Test fixture that provides configured Opossum services for integration tests
/// </summary>
public class OpossumFixture : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string _testStoragePath;

    public IMediator Mediator { get; }
    public IEventStore EventStore { get; }

    public OpossumFixture()
    {
        // Create unique temp directory for this test run
        _testStoragePath = Path.Combine(
            Path.GetTempPath(),
            "OpossumIntegrationTests",
            Guid.NewGuid().ToString());

        var services = new ServiceCollection();

        // Configure Opossum with test storage path
        services.AddOpossum(options =>
        {
            options.RootPath = _testStoragePath;
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

    public void Dispose()
    {
        _serviceProvider?.Dispose();

        // Clean up test storage
        if (Directory.Exists(_testStoragePath))
        {
            try
            {
                Directory.Delete(_testStoragePath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }
}
