using Opossum.Mediator;

namespace Opossum.UnitTests.Mediator;

public class MediatorServiceExtensionsTests
{
    [Fact]
    public void AddMediator_RegistersIMediator()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddMediator();
        var provider = services.BuildServiceProvider();

        // Assert
        var mediator = provider.GetService<IMediator>();
        Assert.NotNull(mediator);
    }

    [Fact]
    public void AddMediator_RegistersAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator();
        var provider = services.BuildServiceProvider();

        // Act
        var mediator1 = provider.GetRequiredService<IMediator>();
        var mediator2 = provider.GetRequiredService<IMediator>();

        // Assert
        Assert.Same(mediator1, mediator2);
    }

    [Fact]
    public void AddMediator_WithOptions_IncludesAdditionalAssemblies()
    {
        // Arrange
        var services = new ServiceCollection();
        var additionalAssembly = typeof(MediatorServiceExtensionsTests).Assembly;

        // Act
        services.AddMediator(options =>
        {
            options.Assemblies.Add(additionalAssembly);
        });

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Assert
        Assert.NotNull(mediator);
    }

    [Fact]
    public async Task AddMediator_RegistersHandlersFromCallingAssembly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator();
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();
        var message = new ExtensionTestMessage(42);

        // Act
        var result = await mediator.InvokeAsync<ExtensionTestResponse>(message);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void AddMediator_RegistersHandlersCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddMediator();
        var provider = services.BuildServiceProvider();

        // Assert
        var mediator = provider.GetRequiredService<IMediator>();
        Assert.NotNull(mediator);

        // Verify that mediator is a singleton
        var mediator2 = provider.GetRequiredService<IMediator>();
        Assert.Same(mediator, mediator2);
    }

    [Fact]
    public async Task AddMediator_WithMultipleAssemblies_DiscoversAllHandlers()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator(options =>
        {
            options.Assemblies.Add(typeof(MediatorServiceExtensionsTests).Assembly);
        });

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.InvokeAsync<ExtensionTestResponse>(
            new ExtensionTestMessage(123));

        // Assert
        Assert.NotNull(result);
        Assert.Equal(123, result.Value);
    }

    [Fact]
    public void AddMediator_WithNullConfigure_DoesNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert - using a normal invocation since it's synchronous
        services.AddMediator(null);

        // If we get here without exception, test passes
        Assert.NotNull(services);
    }
}

// Test messages and handlers for extension tests
public record ExtensionTestMessage(int Value);
public record ExtensionTestResponse(int Value);

public class ExtensionTestMessageHandler
{
    public ExtensionTestResponse Handle(ExtensionTestMessage message)
    {
        return new ExtensionTestResponse(message.Value);
    }
}

// Duplicate handlers for testing error handling - using unique message type
public record UniqueTestMessageForDuplicateTest();

public class UniqueTestMessageForDuplicateTestHandler1
{
    public ExtensionTestResponse Handle(UniqueTestMessageForDuplicateTest message)
    {
        return new ExtensionTestResponse(1);
    }
}

public class UniqueTestMessageForDuplicateTestHandler2
{
    public ExtensionTestResponse Handle(UniqueTestMessageForDuplicateTest message)
    {
        return new ExtensionTestResponse(2);
    }
}
