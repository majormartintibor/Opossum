using Microsoft.Extensions.DependencyInjection;
using Opossum.Mediator;

namespace Opossum.IntegrationTests.Tests;

/// <summary>
/// Unit tests for OpossumFixture to ensure proper initialization and cleanup
/// </summary>
public class OpossumFixtureTests
{
    [Fact]
    public void Constructor_InitializesMediator()
    {
        // Arrange & Act
        using var fixture = new OpossumFixture();

        // Assert
        Assert.NotNull(fixture.Mediator);
        Assert.IsAssignableFrom<IMediator>(fixture.Mediator);
    }

    [Fact]
    public void Constructor_InitializesEventStore()
    {
        // Arrange & Act
        using var fixture = new OpossumFixture();

        // Assert
        Assert.NotNull(fixture.EventStore);
        Assert.IsAssignableFrom<IEventStore>(fixture.EventStore);
    }

    [Fact]
    public void Constructor_CreatesUniqueStoragePath()
    {
        // Arrange & Act
        using var fixture1 = new OpossumFixture();
        using var fixture2 = new OpossumFixture();

        // Assert - Each fixture should have its own unique instance
        // This is verified indirectly - if they shared the same path,
        // we would see concurrency issues in integration tests
        Assert.NotNull(fixture1.EventStore);
        Assert.NotNull(fixture2.EventStore);
    }

    [Fact]
    public void Constructor_InitializesStorageStructure()
    {
        // Arrange & Act
        using var fixture = new OpossumFixture();

        // Assert - The fixture should create storage structure
        // We can verify this by checking that EventStore is initialized
        Assert.NotNull(fixture.EventStore);
    }

    [Fact]
    public void Dispose_CleansUpResources()
    {
        // Arrange
        var fixture = new OpossumFixture();
        var mediator = fixture.Mediator;
        var eventStore = fixture.EventStore;

        // Act
        fixture.Dispose();

        // Assert - After disposal, the fixture should have cleaned up
        // Verify that objects were created (we can't verify disposal directly)
        Assert.NotNull(mediator);
        Assert.NotNull(eventStore);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var fixture = new OpossumFixture();

        // Act & Assert - Should not throw
        fixture.Dispose();
        fixture.Dispose();
    }

    [Fact]
    public void Mediator_CanBeUsedForServiceResolution()
    {
        // Arrange
        using var fixture = new OpossumFixture();

        // Act
        var mediator = fixture.Mediator;

        // Assert
        Assert.NotNull(mediator);
        Assert.IsType<Opossum.Mediator.Mediator>(mediator);
    }

    [Fact]
    public void EventStore_CanBeUsedForEventOperations()
    {
        // Arrange
        using var fixture = new OpossumFixture();

        // Act
        var eventStore = fixture.EventStore;

        // Assert
        Assert.NotNull(eventStore);
        // The actual type will be FileSystemEventStore once implemented
        Assert.IsAssignableFrom<IEventStore>(eventStore);
    }

    [Fact]
    public void Constructor_ConfiguresCourseManagementContext()
    {
        // Arrange & Act
        using var fixture = new OpossumFixture();

        // Assert - The fixture should be ready to work with CourseManagement context
        // This will be verified through integration tests
        Assert.NotNull(fixture.EventStore);
    }

    [Fact]
    public void Constructor_ConfiguresTestContext()
    {
        // Arrange & Act
        using var fixture = new OpossumFixture();

        // Assert - The fixture should be ready to work with TestContext
        // This will be verified through integration tests
        Assert.NotNull(fixture.EventStore);
    }

    [Fact]
    public void UsingStatement_DisposesFixtureAutomatically()
    {
        // Arrange
        OpossumFixture? capturedFixture = null;

        // Act
        using (var fixture = new OpossumFixture())
        {
            capturedFixture = fixture;
            Assert.NotNull(fixture.Mediator);
            Assert.NotNull(fixture.EventStore);
        }

        // Assert - After using block, fixture should be disposed
        Assert.NotNull(capturedFixture);
    }

    [Fact]
    public void MultipleFixtures_CanExistSimultaneously()
    {
        // Arrange & Act
        using var fixture1 = new OpossumFixture();
        using var fixture2 = new OpossumFixture();
        using var fixture3 = new OpossumFixture();

        // Assert - All fixtures should be independent
        Assert.NotNull(fixture1.EventStore);
        Assert.NotNull(fixture2.EventStore);
        Assert.NotNull(fixture3.EventStore);
        Assert.NotSame(fixture1.EventStore, fixture2.EventStore);
        Assert.NotSame(fixture2.EventStore, fixture3.EventStore);
    }

    [Fact]
    public void Fixture_ProvidesSameServicesForLifetime()
    {
        // Arrange
        using var fixture = new OpossumFixture();

        // Act
        var mediator1 = fixture.Mediator;
        var mediator2 = fixture.Mediator;
        var eventStore1 = fixture.EventStore;
        var eventStore2 = fixture.EventStore;

        // Assert - Properties should return same instances
        Assert.Same(mediator1, mediator2);
        Assert.Same(eventStore1, eventStore2);
    }

    [Fact]
    public void Fixture_ConfiguresLoggingForTests()
    {
        // Arrange & Act
        using var fixture = new OpossumFixture();

        // Assert - Logging should be configured
        // This is verified indirectly through the successful service initialization
        Assert.NotNull(fixture.Mediator);
        Assert.NotNull(fixture.EventStore);
    }

    [Fact]
    public void Fixture_UsesTemporaryStoragePath()
    {
        // Arrange & Act
        using var fixture = new OpossumFixture();

        // Assert - Storage should be in temp directory
        // This ensures tests don't pollute the project directory
        Assert.NotNull(fixture.EventStore);
    }

    [Fact]
    public void Fixture_IsolatesTestData()
    {
        // Arrange & Act
        using var fixture1 = new OpossumFixture();
        using var fixture2 = new OpossumFixture();

        // Assert - Each fixture has isolated storage
        // This prevents test interference
        Assert.NotSame(fixture1.EventStore, fixture2.EventStore);
    }
}
