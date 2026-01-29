# Basic Mediator Pattern Implementation Specification

This specification describes a minimal in-process mediator pattern implementation based on the Wolverine framework's design, focusing solely on the `InvokeAsync<T>` method for request/response scenarios.

## Overview

The mediator pattern decouples the sender of a request from its handler by introducing a mediator that routes requests to appropriate handlers. This implementation focuses on:
- In-process, synchronous execution
- Type-safe request/response handling
- Dependency injection support for handlers
- Simple, convention-based handler discovery

## Core Interfaces

### 1. IMediator Interface

```csharp
/// <summary>
/// Entry point for processing messages with the mediator pattern
/// </summary>
public interface IMediator
{
    /// <summary>
    /// Execute the message handling for this message synchronously and wait for the response.
    /// The message is handled locally and delegates immediately to the appropriate handler.
    /// </summary>
    /// <typeparam name="T">The expected response type</typeparam>
    /// <param name="message">The message/request to process</param>
    /// <param name="cancellation">Cancellation token for async operations</param>
    /// <param name="timeout">Optional timeout for the operation</param>
    /// <returns>The response of type T from the handler</returns>
    Task<T> InvokeAsync<T>(object message, CancellationToken cancellation = default, TimeSpan? timeout = default);
}
```

### 2. IMessageHandler Interface

```csharp
/// <summary>
/// Interface for generated/compiled message handlers
/// </summary>
public interface IMessageHandler
{
    /// <summary>
    /// The type of message this handler processes
    /// </summary>
    Type MessageType { get; }
    
    /// <summary>
    /// Execute the handler logic
    /// </summary>
    /// <param name="message">The message to handle</param>
    /// <param name="serviceProvider">Service provider for dependency injection</param>
    /// <param name="cancellation">Cancellation token</param>
    /// <returns>The response object, or null if no response</returns>
    Task<object?> HandleAsync(object message, IServiceProvider serviceProvider, CancellationToken cancellation);
}
```

## Handler Convention

### Discovery Rules

Handlers are discovered using these conventions:

1. **Class naming**: Classes ending with `Handler` or marked with a `[MessageHandler]` attribute
2. **Method naming**: Methods named `Handle`, `HandleAsync`, `Consume`, or `ConsumeAsync`
3. **Method signature**: 
   - First parameter is the message type
   - Additional parameters are injected from DI container
   - Return type is the response type (for request/response scenarios)

### Handler Examples

#### Basic Handler with Response
```csharp
public record GetUserQuery(int UserId);
public record UserResponse(int Id, string Name);

public class GetUserQueryHandler
{
    public UserResponse Handle(GetUserQuery query)
    {
        // Handle the query and return response
        return new UserResponse(query.UserId, "John Doe");
    }
}
```

#### Async Handler with Dependencies
```csharp
public record CreateOrderCommand(string ProductId, int Quantity);
public record OrderCreatedResponse(string OrderId);

public class CreateOrderCommandHandler
{
    public async Task<OrderCreatedResponse> HandleAsync(
        CreateOrderCommand command, 
        IOrderRepository repository,
        ILogger<CreateOrderCommandHandler> logger)
    {
        logger.LogInformation("Creating order for {ProductId}", command.ProductId);
        
        var order = await repository.CreateAsync(command.ProductId, command.Quantity);
        
        return new OrderCreatedResponse(order.Id);
    }
}
```

#### Static Handler Methods
```csharp
public static class ProductQueryHandler
{
    public static async Task<ProductDto> Handle(
        GetProductQuery query,
        IProductRepository repository)
    {
        return await repository.GetByIdAsync(query.ProductId);
    }
}
```

## Implementation Architecture

### 1. Mediator Implementation

```csharp
public class Mediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<Type, IMessageHandler> _handlers;
    
    public Mediator(IServiceProvider serviceProvider, Dictionary<Type, IMessageHandler> handlers)
    {
        _serviceProvider = serviceProvider;
        _handlers = handlers;
    }
    
    public async Task<T> InvokeAsync<T>(
        object message, 
        CancellationToken cancellation = default, 
        TimeSpan? timeout = default)
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }
        
        var messageType = message.GetType();
        
        if (!_handlers.TryGetValue(messageType, out var handler))
        {
            throw new InvalidOperationException(
                $"No handler registered for message type {messageType.FullName}");
        }
        
        // Apply timeout if specified
        using var cts = timeout.HasValue 
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellation) 
            : null;
        
        if (cts != null)
        {
            cts.CancelAfter(timeout.Value);
        }
        
        var effectiveToken = cts?.Token ?? cancellation;
        
        var response = await handler.HandleAsync(message, _serviceProvider, effectiveToken);
        
        if (response == null)
        {
            return default!;
        }
        
        if (response is not T typedResponse)
        {
            throw new InvalidOperationException(
                $"Handler returned type {response.GetType().FullName} but expected {typeof(T).FullName}");
        }
        
        return typedResponse;
    }
}
```

### 2. Handler Discovery Service

```csharp
public class HandlerDiscoveryService
{
    private readonly List<Assembly> _assemblies = new();
    
    public void IncludeAssembly(Assembly assembly)
    {
        _assemblies.Add(assembly);
    }
    
    public List<(Type HandlerType, MethodInfo Method)> DiscoverHandlers()
    {
        var handlers = new List<(Type, MethodInfo)>();
        
        foreach (var assembly in _assemblies)
        {
            var handlerTypes = assembly.GetTypes()
                .Where(t => !t.IsAbstract && 
                           (t.Name.EndsWith("Handler") || 
                            t.GetCustomAttribute<MessageHandlerAttribute>() != null))
                .ToList();
            
            foreach (var handlerType in handlerTypes)
            {
                var methods = handlerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .Where(IsValidHandlerMethod)
                    .ToList();
                
                foreach (var method in methods)
                {
                    handlers.Add((handlerType, method));
                }
            }
        }
        
        return handlers;
    }
    
    private bool IsValidHandlerMethod(MethodInfo method)
    {
        // Valid method names
        var validNames = new[] { "Handle", "HandleAsync", "Consume", "ConsumeAsync" };
        if (!validNames.Contains(method.Name))
        {
            return false;
        }
        
        // Must have at least one parameter (the message)
        var parameters = method.GetParameters();
        if (parameters.Length == 0)
        {
            return false;
        }
        
        // First parameter is the message type
        var messageType = parameters[0].ParameterType;
        if (!IsValidMessageType(messageType))
        {
            return false;
        }
        
        return true;
    }
    
    private bool IsValidMessageType(Type type)
    {
        // Should be a concrete class or record
        return type.IsClass && !type.IsAbstract;
    }
}
```

### 3. Handler Code Generation

For each discovered handler method, generate a class implementing `IMessageHandler`:

```csharp
// Example generated handler for GetUserQueryHandler.Handle(GetUserQuery)
public sealed class GetUserQueryHandler_Generated : IMessageHandler
{
    public Type MessageType => typeof(GetUserQuery);
    
    public async Task<object?> HandleAsync(
        object message, 
        IServiceProvider serviceProvider, 
        CancellationToken cancellation)
    {
        var typedMessage = (GetUserQuery)message;
        
        // Create handler instance (or use static method)
        var handler = ActivatorUtilities.CreateInstance<GetUserQueryHandler>(serviceProvider);
        
        // Call the handler method
        var result = handler.Handle(typedMessage);
        
        return result;
    }
}
```

For handlers with dependencies:

```csharp
// Example for CreateOrderCommandHandler with dependencies
public sealed class CreateOrderCommandHandler_Generated : IMessageHandler
{
    public Type MessageType => typeof(CreateOrderCommand);
    
    public async Task<object?> HandleAsync(
        object message, 
        IServiceProvider serviceProvider, 
        CancellationToken cancellation)
    {
        var typedMessage = (CreateOrderCommand)message;
        
        // Resolve dependencies from DI
        var repository = serviceProvider.GetRequiredService<IOrderRepository>();
        var logger = serviceProvider.GetRequiredService<ILogger<CreateOrderCommandHandler>>();
        
        // Create handler instance
        var handler = new CreateOrderCommandHandler();
        
        // Call the handler method with dependencies
        var result = await handler.HandleAsync(typedMessage, repository, logger);
        
        return result;
    }
}
```

### 4. Service Registration

```csharp
public static class MediatorServiceExtensions
{
    public static IServiceCollection AddMediator(
        this IServiceCollection services, 
        Action<MediatorOptions>? configure = null)
    {
        var options = new MediatorOptions();
        configure?.Invoke(options);
        
        // Discover handlers
        var discovery = new HandlerDiscoveryService();
        
        // Add calling assembly by default
        var callingAssembly = Assembly.GetCallingAssembly();
        discovery.IncludeAssembly(callingAssembly);
        
        // Add any additional assemblies
        foreach (var assembly in options.Assemblies)
        {
            discovery.IncludeAssembly(assembly);
        }
        
        var discoveredHandlers = discovery.DiscoverHandlers();
        
        // Generate handler implementations
        var generatedHandlers = GenerateHandlers(discoveredHandlers);
        
        // Register mediator
        services.AddSingleton<IMediator>(sp => 
            new Mediator(sp, generatedHandlers));
        
        return services;
    }
    
    private static Dictionary<Type, IMessageHandler> GenerateHandlers(
        List<(Type HandlerType, MethodInfo Method)> discoveredHandlers)
    {
        var handlers = new Dictionary<Type, IMessageHandler>();
        
        foreach (var (handlerType, method) in discoveredHandlers)
        {
            var messageType = method.GetParameters()[0].ParameterType;
            
            // Generate and compile the handler
            var generatedHandler = HandlerGenerator.Generate(handlerType, method);
            
            handlers[messageType] = generatedHandler;
        }
        
        return handlers;
    }
}

public class MediatorOptions
{
    public List<Assembly> Assemblies { get; } = new();
}
```

## Usage Examples

### 1. Service Registration

```csharp
// In Program.cs or Startup.cs
builder.Services.AddMediator(options =>
{
    // Optionally include additional assemblies
    options.Assemblies.Add(typeof(SomeHandler).Assembly);
});
```

### 2. Using the Mediator

```csharp
public class UserController : ControllerBase
{
    private readonly IMediator _mediator;
    
    public UserController(IMediator mediator)
    {
        _mediator = mediator;
    }
    
    [HttpGet("{id}")]
    public async Task<IActionResult> GetUser(int id)
    {
        var query = new GetUserQuery(id);
        var response = await _mediator.InvokeAsync<UserResponse>(query);
        
        return Ok(response);
    }
    
    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderCommand command)
    {
        var response = await _mediator.InvokeAsync<OrderCreatedResponse>(
            command, 
            timeout: TimeSpan.FromSeconds(30));
        
        return CreatedAtAction(nameof(GetOrder), new { id = response.OrderId }, response);
    }
}
```

### 3. Using with Cancellation

```csharp
public class BackgroundService : Microsoft.Extensions.Hosting.BackgroundService
{
    private readonly IMediator _mediator;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var query = new ProcessNextBatchQuery();
            
            var result = await _mediator.InvokeAsync<BatchResult>(
                query, 
                stoppingToken);
            
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
```

## Key Implementation Details

### 1. Handler Resolution
- Handlers are discovered at startup via reflection
- One handler per message type (no support for multiple handlers per message in this basic implementation)
- Handlers are cached in a dictionary by message type for fast lookup

### 2. Dependency Injection
- Handler methods can request dependencies as parameters
- Dependencies are resolved from the `IServiceProvider` when the handler is invoked
- Handler instances themselves can be created via `ActivatorUtilities.CreateInstance` if they have constructor dependencies

### 3. Return Value Handling
- Handlers must return a value that matches the expected response type `T`
- `void` handlers are not supported in this implementation (only request/response)
- The framework validates that the returned type matches the expected type

### 4. Error Handling
- Null messages throw `ArgumentNullException`
- Missing handlers throw `InvalidOperationException`
- Type mismatches between returned value and expected type throw `InvalidOperationException`
- Handler exceptions bubble up to the caller

### 5. Code Generation Approach

Two options for implementation:

**Option A: Runtime Code Generation (Reflection.Emit or Expression Trees)**
- Generate IL or expression trees at startup
- Faster execution after compilation
- More complex implementation

**Option B: Source Generators (Recommended for .NET 6+)**
- Use Roslyn source generators
- Code is generated at compile time
- Better debugging experience
- Type-safe and AOT-friendly

**Option C: Simple Reflection (Easiest)**
- Use `MethodInfo.Invoke` with dependency resolution
- Slower but simpler to implement
- Good for proof-of-concept

## Testing Considerations

### Unit Testing Handlers

```csharp
public class GetUserQueryHandlerTests
{
    [Fact]
    public void Handle_ReturnsUserResponse()
    {
        // Arrange
        var handler = new GetUserQueryHandler();
        var query = new GetUserQuery(1);
        
        // Act
        var result = handler.Handle(query);
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
    }
}
```

### Integration Testing with Mediator

```csharp
public class MediatorIntegrationTests
{
    [Fact]
    public async Task InvokeAsync_WithValidQuery_ReturnsResponse()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator();
        var provider = services.BuildServiceProvider();
        
        var mediator = provider.GetRequiredService<IMediator>();
        var query = new GetUserQuery(1);
        
        // Act
        var result = await mediator.InvokeAsync<UserResponse>(query);
        
        // Assert
        Assert.NotNull(result);
    }
}
```

## Performance Considerations

1. **Handler caching**: All handler instances are resolved once at startup
2. **No reflection in hot path**: After initial discovery, use compiled delegates or generated code
3. **Minimal allocations**: Reuse service scopes where possible
4. **Timeout implementation**: Use `CancellationTokenSource` with linked tokens for efficient timeout handling

## Extensibility Points

For future enhancements, consider:

1. **Middleware pipeline**: Add support for pre/post processing
2. **Validation**: Integrate FluentValidation or similar
3. **Logging**: Add structured logging for all invocations
4. **Metrics**: Track handler execution times and success/failure rates
5. **Multiple handlers**: Support for notification pattern (publish/subscribe)
6. **Async streaming**: Support `IAsyncEnumerable<T>` responses

## References

This specification is based on the Wolverine framework's implementation:
- Interface design: `ICommandBus` and `IMessageBus` interfaces
- Handler execution: `Executor` class pattern
- Handler discovery: `HandlerDiscovery` conventions
- Code generation: Generated `MessageHandler` classes
- Dependency injection: Service resolution via `IServiceProvider`

## Minimal Implementation Checklist

To implement this specification:

- [ ] Create `IMediator` interface
- [ ] Create `IMessageHandler` interface  
- [ ] Implement `Mediator` class
- [ ] Create `HandlerDiscoveryService` for finding handlers
- [ ] Implement handler code generation (choose approach A, B, or C)
- [ ] Create `MediatorServiceExtensions` for DI registration
- [ ] Add `[MessageHandler]` attribute for explicit marking
- [ ] Implement timeout support with `CancellationTokenSource`
- [ ] Add proper exception handling and validation
- [ ] Create unit tests for core functionality
- [ ] Create integration tests with DI container
- [ ] Document usage patterns and examples
