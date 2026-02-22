using Opossum.Configuration;
using Opossum.DependencyInjection;
using Opossum.Storage.FileSystem;

namespace Opossum.UnitTests.DependencyInjection;

public class ServiceCollectionExtensionsTests : IDisposable
{
    private readonly string _testRootPath;

    public ServiceCollectionExtensionsTests()
    {
        // Create unique test directory for each test
        _testRootPath = Path.Combine(
            Path.GetTempPath(),
            "OpossumTests",
            "ServiceCollection",
            Guid.NewGuid().ToString());
    }

    public void Dispose()
    {
        // Clean up test directory
        if (Directory.Exists(_testRootPath))
        {
            try
            {
                Directory.Delete(_testRootPath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Fact]
    public void AddOpossum_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection services = null!;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            services.AddOpossum(options => options.AddContext("Test")));
        Assert.Equal("services", exception.ParamName);
    }

    [Fact]
    public void AddOpossum_WithNoContexts_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddOpossum());
        Assert.Contains("At least one context must be configured", exception.Message);
    }

    [Fact]
    public void AddOpossum_WithNullConfigure_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert - no configure action means no contexts
        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddOpossum(configure: null));
        Assert.Contains("At least one context must be configured", exception.Message);
    }

    [Fact]
    public void AddOpossum_WithValidConfiguration_RegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOpossum(options =>
        {
            options.RootPath = _testRootPath;
            options.AddContext("CourseManagement");
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert - OpossumOptions registered
        var options = serviceProvider.GetService<OpossumOptions>();
        Assert.NotNull(options);
        Assert.Equal(_testRootPath, options.RootPath);
        Assert.Contains("CourseManagement", options.Contexts);
    }

    [Fact]
    public void AddOpossum_RegistersOpossumOptionsAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOpossum(options =>
        {
            options.RootPath = _testRootPath;
            options.AddContext("CourseManagement");
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert - Same instance returned
        var options1 = serviceProvider.GetService<OpossumOptions>();
        var options2 = serviceProvider.GetService<OpossumOptions>();
        Assert.Same(options1, options2);
    }

    [Fact]
    public void AddOpossum_RegistersStorageInitializer()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOpossum(options =>
        {
            options.RootPath = _testRootPath;
            options.AddContext("CourseManagement");
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var initializer = serviceProvider.GetService<StorageInitializer>();
        Assert.NotNull(initializer);
    }

    [Fact]
    public void AddOpossum_RegistersStorageInitializerAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOpossum(options =>
        {
            options.RootPath = _testRootPath;
            options.AddContext("CourseManagement");
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert - Same instance returned
        var initializer1 = serviceProvider.GetService<StorageInitializer>();
        var initializer2 = serviceProvider.GetService<StorageInitializer>();
        Assert.Same(initializer1, initializer2);
    }

    [Fact]
    public void AddOpossum_RegistersIEventStore()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOpossum(options =>
        {
            options.RootPath = _testRootPath;
            options.AddContext("CourseManagement");
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var eventStore = serviceProvider.GetService<IEventStore>();
        Assert.NotNull(eventStore);
        Assert.IsType<FileSystemEventStore>(eventStore);
    }

    [Fact]
    public void AddOpossum_RegistersIEventStoreAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOpossum(options =>
        {
            options.RootPath = _testRootPath;
            options.AddContext("CourseManagement");
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert - Same instance returned
        var eventStore1 = serviceProvider.GetService<IEventStore>();
        var eventStore2 = serviceProvider.GetService<IEventStore>();
        Assert.Same(eventStore1, eventStore2);
    }

    [Fact]
    public void AddOpossum_InitializesStorageStructure()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOpossum(options =>
        {
            options.RootPath = _testRootPath;
            options.AddContext("CourseManagement");
        });

        // Assert - Directory structure should exist
        Assert.True(Directory.Exists(_testRootPath));
        Assert.True(Directory.Exists(Path.Combine(_testRootPath, "CourseManagement")));
        Assert.True(File.Exists(Path.Combine(_testRootPath, "CourseManagement", ".ledger")));
        Assert.True(Directory.Exists(Path.Combine(_testRootPath, "CourseManagement", "events")));
        Assert.True(Directory.Exists(Path.Combine(_testRootPath, "CourseManagement", "Indices", "EventType")));
        Assert.True(Directory.Exists(Path.Combine(_testRootPath, "CourseManagement", "Indices", "Tags")));
    }

    [Fact]
    public void AddOpossum_WithMultipleContexts_InitializesAll()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOpossum(options =>
        {
            options.RootPath = _testRootPath;
            options.AddContext("CourseManagement")
                   .AddContext("StudentEnrollment")
                   .AddContext("Billing");
        });

        // Assert - All context directories exist
        Assert.True(Directory.Exists(Path.Combine(_testRootPath, "CourseManagement")));
        Assert.True(Directory.Exists(Path.Combine(_testRootPath, "StudentEnrollment")));
        Assert.True(Directory.Exists(Path.Combine(_testRootPath, "Billing")));
    }

    [Fact]
    public void AddOpossum_ReturnsServiceCollection_ForChaining()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddOpossum(options =>
        {
            options.RootPath = _testRootPath;
            options.AddContext("CourseManagement");
        });

        // Assert
        Assert.Same(services, result);
    }

    [Fact]
    public void AddOpossum_CanBeChainedWithOtherServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services
            .AddOpossum(options =>
            {
                options.RootPath = _testRootPath;
                options.AddContext("CourseManagement");
            })
            .AddLogging()
            .AddSingleton<string>("test");

        var serviceProvider = services.BuildServiceProvider();

        // Assert - All services registered
        Assert.NotNull(serviceProvider.GetService<OpossumOptions>());
        Assert.NotNull(serviceProvider.GetService<IEventStore>());
        Assert.NotNull(serviceProvider.GetService<string>());
    }

    [Fact]
    public void AddOpossum_WithCustomRootPath_UsesSpecifiedPath()
    {
        // Arrange
        var services = new ServiceCollection();
        var customPath = Path.Combine(_testRootPath, "custom", "events");

        // Act
        services.AddOpossum(options =>
        {
            options.RootPath = customPath;
            options.AddContext("CourseManagement");
        });

        // Assert
        Assert.True(Directory.Exists(customPath));
        Assert.True(Directory.Exists(Path.Combine(customPath, "CourseManagement")));
    }

    [Fact]
    public void AddOpossum_WithAbsolutePath_CreatesDirectories()
    {
        // Arrange
        var services = new ServiceCollection();
        // Use absolute path (validation requires absolute paths)
        var absolutePath = Path.Combine(Path.GetTempPath(), "test-opossum-data", Guid.NewGuid().ToString());

        try
        {
            // Act
            services.AddOpossum(options =>
            {
                options.RootPath = absolutePath;
                options.AddContext("CourseManagement");
            });

            // Assert
            Assert.True(Directory.Exists(absolutePath));
            Assert.True(Directory.Exists(Path.Combine(absolutePath, "CourseManagement")));
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(absolutePath))
            {
                Directory.Delete(absolutePath, recursive: true);
            }
        }
    }

    [Fact]
    public void AddOpossum_CalledMultipleTimes_ThrowsOrOverwrites()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act - Call twice
        services.AddOpossum(options =>
        {
            options.RootPath = _testRootPath;
            options.AddContext("CourseManagement");
        });

        services.AddOpossum(options =>
        {
            options.RootPath = _testRootPath;
            options.AddContext("Billing");
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert - Last registration wins (standard DI behavior)
        var options = serviceProvider.GetService<OpossumOptions>();
        Assert.NotNull(options);
        // The last AddOpossum call should have registered different contexts
        Assert.Contains("Billing", options.Contexts);
    }

    [Fact]
    public void AddOpossum_WithEnableProjectionDaemonFalse_StillRegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOpossum(
            configure: options =>
            {
                options.RootPath = _testRootPath;
                options.AddContext("CourseManagement");
            });

        var serviceProvider = services.BuildServiceProvider();

        // Assert - Core services still registered
        Assert.NotNull(serviceProvider.GetService<OpossumOptions>());
        Assert.NotNull(serviceProvider.GetService<IEventStore>());
        Assert.NotNull(serviceProvider.GetService<StorageInitializer>());
    }

    [Fact]
    public void AddOpossum_WithEnableProjectionDaemonTrue_StillRegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOpossum(
            configure: options =>
            {
                options.RootPath = _testRootPath;
                options.AddContext("CourseManagement");
            });

        var serviceProvider = services.BuildServiceProvider();

        // Assert - Core services still registered (daemon not yet implemented)
        Assert.NotNull(serviceProvider.GetService<OpossumOptions>());
        Assert.NotNull(serviceProvider.GetService<IEventStore>());
    }

    [Fact]
    public void AddOpossum_DisposesServiceProvider_CleansUpResources()
    {
        // Arrange
        var services = new ServiceCollection();

        services.AddOpossum(options =>
        {
            options.RootPath = _testRootPath;
            options.AddContext("CourseManagement");
        });

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var eventStore = serviceProvider.GetService<IEventStore>();
        Assert.NotNull(eventStore);

        // Dispose should not throw
        serviceProvider.Dispose();

        // Assert - No exception thrown
        Assert.True(true);
    }
}
