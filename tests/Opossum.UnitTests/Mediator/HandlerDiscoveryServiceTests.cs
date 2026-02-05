using Opossum.Mediator;

namespace Opossum.UnitTests.Mediator;

public class HandlerDiscoveryServiceTests
{
    [Fact]
    public void IncludeAssembly_WithValidAssembly_AddsAssembly()
    {
        // Arrange
        var discovery = new HandlerDiscoveryService();
        var assembly = typeof(HandlerDiscoveryServiceTests).Assembly;
        
        // Act
        discovery.IncludeAssembly(assembly);
        var handlers = discovery.DiscoverHandlers();
        
        // Assert
        Assert.NotEmpty(handlers);
    }
    
    [Fact]
    public void IncludeAssembly_WithNull_ThrowsArgumentNullException()
    {
        // Arrange
        var discovery = new HandlerDiscoveryService();
        
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            discovery.IncludeAssembly(null!));
    }
    
    [Fact]
    public void DiscoverHandlers_FindsHandlerWithHandlerSuffix()
    {
        // Arrange
        var discovery = new HandlerDiscoveryService();
        discovery.IncludeAssembly(typeof(DiscoveryTestHandler).Assembly);
        
        // Act
        var handlers = discovery.DiscoverHandlers();
        
        // Assert
        var handler = handlers.FirstOrDefault(h => 
            h.HandlerType == typeof(DiscoveryTestHandler));
        Assert.NotNull(handler);
    }
    
    [Fact]
    public void DiscoverHandlers_FindsHandlerWithAttribute()
    {
        // Arrange
        var discovery = new HandlerDiscoveryService();
        discovery.IncludeAssembly(typeof(NonConventionalHandlerName).Assembly);
        
        // Act
        var handlers = discovery.DiscoverHandlers();
        
        // Assert
        var handler = handlers.FirstOrDefault(h => 
            h.HandlerType == typeof(NonConventionalHandlerName));
        Assert.NotNull(handler);
    }
    
    [Fact]
    public void DiscoverHandlers_FindsHandleMethod()
    {
        // Arrange
        var discovery = new HandlerDiscoveryService();
        discovery.IncludeAssembly(typeof(HandleMethodHandler).Assembly);
        
        // Act
        var handlers = discovery.DiscoverHandlers();
        
        // Assert
        var handler = handlers.FirstOrDefault(h => 
            h.HandlerType == typeof(HandleMethodHandler) && 
            h.Method.Name == "Handle");
        Assert.NotNull(handler);
    }
    
    [Fact]
    public void DiscoverHandlers_FindsHandleAsyncMethod()
    {
        // Arrange
        var discovery = new HandlerDiscoveryService();
        discovery.IncludeAssembly(typeof(HandleAsyncMethodHandler).Assembly);
        
        // Act
        var handlers = discovery.DiscoverHandlers();
        
        // Assert
        var handler = handlers.FirstOrDefault(h => 
            h.HandlerType == typeof(HandleAsyncMethodHandler) && 
            h.Method.Name == "HandleAsync");
        Assert.NotNull(handler);
    }
    
    [Fact]
    public void DiscoverHandlers_FindsConsumeMethod()
    {
        // Arrange
        var discovery = new HandlerDiscoveryService();
        discovery.IncludeAssembly(typeof(ConsumeMethodHandler).Assembly);
        
        // Act
        var handlers = discovery.DiscoverHandlers();
        
        // Assert
        var handler = handlers.FirstOrDefault(h => 
            h.HandlerType == typeof(ConsumeMethodHandler) && 
            h.Method.Name == "Consume");
        Assert.NotNull(handler);
    }
    
    [Fact]
    public void DiscoverHandlers_FindsConsumeAsyncMethod()
    {
        // Arrange
        var discovery = new HandlerDiscoveryService();
        discovery.IncludeAssembly(typeof(ConsumeAsyncMethodHandler).Assembly);
        
        // Act
        var handlers = discovery.DiscoverHandlers();
        
        // Assert
        var handler = handlers.FirstOrDefault(h => 
            h.HandlerType == typeof(ConsumeAsyncMethodHandler) && 
            h.Method.Name == "ConsumeAsync");
        Assert.NotNull(handler);
    }
    
    [Fact]
    public void DiscoverHandlers_IgnoresInvalidMethodNames()
    {
        // Arrange
        var discovery = new HandlerDiscoveryService();
        discovery.IncludeAssembly(typeof(InvalidMethodProcessor).Assembly);

        // Act
        var handlers = discovery.DiscoverHandlers();

        // Assert
        // Should not find handler because Process() is not a valid method name
        var handler = handlers.FirstOrDefault(h => 
            h.HandlerType == typeof(InvalidMethodProcessor) &&
            h.Method.Name == "Process");
        Assert.True(handler == default, "InvalidMethodProcessor with Process method should not be discovered");
    }
    
    [Fact]
    public void DiscoverHandlers_IgnoresMethodsWithoutParameters()
    {
        // Arrange
        var discovery = new HandlerDiscoveryService();
        discovery.IncludeAssembly(typeof(NoParameterHandler).Assembly);
        
        // Act
        var handlers = discovery.DiscoverHandlers();
        
        // Assert
        var handler = handlers.FirstOrDefault(h => 
            h.HandlerType == typeof(NoParameterHandler));
        Assert.True(handler == default, "NoParameterHandler should not be discovered");
    }
    
    [Fact]
    public void DiscoverHandlers_IgnoresAbstractClasses()
    {
        // Arrange
        var discovery = new HandlerDiscoveryService();
        discovery.IncludeAssembly(typeof(AbstractHandler).Assembly);

        // Act
        var handlers = discovery.DiscoverHandlers();

        // Assert
        // AbstractHandler should not be found because it's abstract (and not static/sealed)
        var handler = handlers.FirstOrDefault(h => 
            h.HandlerType == typeof(AbstractHandler));
        Assert.True(handler == default, "AbstractHandler should not be discovered");

        // But static handlers should still be found (they are abstract AND sealed)
        var staticHandler = handlers.FirstOrDefault(h =>
            h.HandlerType == typeof(StaticMethodHandler));
        Assert.NotNull(staticHandler);
    }
    
    [Fact]
    public void DiscoverHandlers_FindsStaticMethods()
    {
        // Arrange
        var discovery = new HandlerDiscoveryService();
        discovery.IncludeAssembly(typeof(StaticMethodHandler).Assembly);
        
        // Act
        var handlers = discovery.DiscoverHandlers();
        
        // Assert
        var handler = handlers.FirstOrDefault(h => 
            h.HandlerType == typeof(StaticMethodHandler) && 
            h.Method.IsStatic);
        Assert.NotNull(handler);
    }
    
    [Fact]
    public void DiscoverHandlers_WithMultipleAssemblies_FindsHandlersFromAll()
    {
        // Arrange
        var discovery = new HandlerDiscoveryService();
        var assembly1 = typeof(DiscoveryTestHandler).Assembly;
        var assembly2 = typeof(HandlerDiscoveryServiceTests).Assembly;
        
        discovery.IncludeAssembly(assembly1);
        discovery.IncludeAssembly(assembly2);
        
        // Act
        var handlers = discovery.DiscoverHandlers();
        
        // Assert
        Assert.NotEmpty(handlers);
    }
}

// Test handlers for discovery
public record HandlerDiscoveryTestMessage();
public record HandlerDiscoveryTestResponse();
public record NonConventionalMessage();

public class DiscoveryTestHandler
{
    public HandlerDiscoveryTestResponse Handle(HandlerDiscoveryTestMessage message)
    {
        return new HandlerDiscoveryTestResponse();
    }
}

[MessageHandler]
public class NonConventionalHandlerName
{
    public HandlerDiscoveryTestResponse Handle(NonConventionalMessage message)
    {
        return new HandlerDiscoveryTestResponse();
    }
}

public record HandleMethodMessage();
public class HandleMethodHandler
{
    public HandlerDiscoveryTestResponse Handle(HandleMethodMessage message)
    {
        return new HandlerDiscoveryTestResponse();
    }
}

public record HandleAsyncMessage();
public class HandleAsyncMethodHandler
{
    public Task<HandlerDiscoveryTestResponse> HandleAsync(HandleAsyncMessage message)
    {
        return Task.FromResult(new HandlerDiscoveryTestResponse());
    }
}

public record ConsumeMessage();
public class ConsumeMethodHandler
{
    public HandlerDiscoveryTestResponse Consume(ConsumeMessage message)
    {
        return new HandlerDiscoveryTestResponse();
    }
}

public record ConsumeAsyncMessage();
public class ConsumeAsyncMethodHandler
{
    public Task<HandlerDiscoveryTestResponse> ConsumeAsync(ConsumeAsyncMessage message)
    {
        return Task.FromResult(new HandlerDiscoveryTestResponse());
    }
}

public record InvalidMessage();

[MessageHandler] // Mark with attribute so it's considered despite not ending with "Handler"
public class InvalidMethodProcessor
{
    public HandlerDiscoveryTestResponse Process(InvalidMessage message)
    {
        return new HandlerDiscoveryTestResponse();
    }
}

public class NoParameterHandler
{
    public HandlerDiscoveryTestResponse Handle()
    {
        return new HandlerDiscoveryTestResponse();
    }
}

public abstract class AbstractHandler
{
    public abstract HandlerDiscoveryTestResponse Handle(HandlerDiscoveryTestMessage message);
}

public record StaticMessage();
public static class StaticMethodHandler
{
    public static HandlerDiscoveryTestResponse Handle(StaticMessage message)
    {
        return new HandlerDiscoveryTestResponse();
    }
}
