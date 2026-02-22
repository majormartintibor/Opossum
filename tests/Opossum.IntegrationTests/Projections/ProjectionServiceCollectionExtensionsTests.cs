using Opossum.DependencyInjection;
using Opossum.Projections;

namespace Opossum.IntegrationTests.Projections;

public class ProjectionServiceCollectionExtensionsTests : IDisposable
{
    private readonly string _testStoragePath;

    public ProjectionServiceCollectionExtensionsTests()
    {
        _testStoragePath = Path.Combine(
            Path.GetTempPath(),
            "OpossumDITests",
            Guid.NewGuid().ToString());
    }

    [Fact]
    public void AddProjections_RegistersRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();

        services.AddOpossum(options =>
        {
            options.RootPath = _testStoragePath;
            options.AddContext("TestContext");
        });

        // Act
        services.AddProjections();

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(serviceProvider.GetService<ProjectionOptions>());
        Assert.NotNull(serviceProvider.GetService<IProjectionManager>());
    }

    [Fact]
    public void AddProjections_WithCustomOptions_AppliesConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();

        services.AddOpossum(options =>
        {
            options.RootPath = _testStoragePath;
            options.AddContext("TestContext");
        });

        // Act
        services.AddProjections(options =>
        {
            options.PollingInterval = TimeSpan.FromSeconds(10);
            options.BatchSize = 500;
            options.EnableAutoRebuild = false;
        });

        var serviceProvider = services.BuildServiceProvider();
        var projectionOptions = serviceProvider.GetRequiredService<ProjectionOptions>();

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(10), projectionOptions.PollingInterval);
        Assert.Equal(500, projectionOptions.BatchSize);
        Assert.False(projectionOptions.EnableAutoRebuild);
    }

    [Fact]
    public void AddProjections_WithAssemblyScanning_RegistersProjectionDefinitions()
    {
        // Arrange
        var services = new ServiceCollection();

        services.AddOpossum(options =>
        {
            options.RootPath = _testStoragePath;
            options.AddContext("TestContext");
        });

        // Act
        services.AddProjections(options =>
        {
            options.ScanAssembly(typeof(ProjectionServiceCollectionExtensionsTests).Assembly);
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert - Should be able to resolve projection stores
        var e2eStore = serviceProvider.GetService<IProjectionStore<E2ETestState>>();
        Assert.NotNull(e2eStore);
    }

    [Fact]
    public void AddProjections_RegistersProjectionDaemon()
    {
        // Arrange
        var services = new ServiceCollection();

        services.AddOpossum(options =>
        {
            options.RootPath = _testStoragePath;
            options.AddContext("TestContext");
        });

        // Act
        services.AddProjections();

        // Assert - ProjectionDaemon should be registered as IHostedService
        var hostedServices = services.Where(s => s.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService));
        Assert.Contains(hostedServices, s => s.ImplementationType?.Name == "ProjectionDaemon");
    }

    [Fact]
    public void AddProjections_RegistersInitializationService()
    {
        // Arrange
        var services = new ServiceCollection();

        services.AddOpossum(options =>
        {
            options.RootPath = _testStoragePath;
            options.AddContext("TestContext");
        });

        // Act
        services.AddProjections(options =>
        {
            options.ScanAssembly(typeof(ProjectionServiceCollectionExtensionsTests).Assembly);
        });

        // Assert
        var hostedServices = services.Where(s => s.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService));
        Assert.Contains(hostedServices, s => s.ImplementationType?.Name == "ProjectionInitializationService");
    }

    [Fact]
    public void AddProjections_WithoutOpossum_ThrowsOnBuild()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddProjections();

        // Assert - Should throw when trying to build service provider because OpossumOptions is missing
        Assert.Throws<InvalidOperationException>(() => services.BuildServiceProvider().GetRequiredService<IProjectionManager>());
    }

    [Fact]
    public void AddProjections_CanResolveProjectionStoresAfterRegistration()
    {
        // Arrange
        var services = new ServiceCollection();

        services.AddOpossum(options =>
        {
            options.RootPath = _testStoragePath;
            options.AddContext("TestContext");
        });

        services.AddProjections(options =>
        {
            options.ScanAssembly(typeof(ProjectionServiceCollectionExtensionsTests).Assembly);
        });

        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert
        var store = serviceProvider.GetService<IProjectionStore<E2ETestState>>();
        Assert.NotNull(store);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testStoragePath))
        {
            try
            {
                Directory.Delete(_testStoragePath, recursive: true);
            }
            catch { }
        }
    }

    // Test projection for discovery
    [ProjectionDefinition("E2ETest")]
    private class E2ETestProjection : IProjectionDefinition<E2ETestState>
    {
        public string ProjectionName => "E2ETest";
        public string[] EventTypes => ["TestEvent"];
        public string KeySelector(Opossum.Core.SequencedEvent evt) => "key";
        public E2ETestState? Apply(E2ETestState? current, IEvent evt) => current;
    }

    private record E2ETestState(string Id);
}
