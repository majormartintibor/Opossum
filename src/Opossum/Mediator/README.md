# Mediator Pattern Implementation

This folder contains a minimal in-process mediator pattern implementation based on the Wolverine framework's design.

## Overview

The mediator pattern decouples the sender of a request from its handler by introducing a mediator that routes requests to appropriate handlers.

## Components

### Core Interfaces

- **`IMediator`**: Entry point for processing messages with request/response pattern
- **`IMessageHandler`**: Interface for compiled message handlers (internal)

### Implementation Classes

- **`Mediator`**: Main implementation of the mediator pattern
- **`HandlerDiscoveryService`**: Discovers message handlers using reflection
- **`ReflectionMessageHandler`**: Runtime handler wrapper using reflection
- **`MessageHandlerAttribute`**: Marks classes as message handlers for explicit discovery

### Configuration

- **`MediatorOptions`**: Configuration options for handler discovery
- **`MediatorServiceExtensions`**: Extension methods for DI registration

## Handler Convention

Handlers are discovered using these conventions:

1. **Class naming**: Classes ending with `Handler` or marked with `[MessageHandler]` attribute
2. **Method naming**: Methods named `Handle`, `HandleAsync`, `Consume`, or `ConsumeAsync`
3. **Method signature**: 
   - First parameter is the message type
   - Additional parameters are injected from DI container
   - Return type is the response type

## Usage

### 1. Register the Mediator

```csharp
// In Program.cs
builder.Services.AddMediator(options =>
{
    // Optionally include additional assemblies
    options.Assemblies.Add(typeof(SomeHandler).Assembly);
});
```

### 2. Create a Handler

```csharp
// Message
public record GetUserQuery(int UserId);
public record UserResponse(int Id, string Name);

// Handler
public class GetUserQueryHandler
{
    public UserResponse Handle(GetUserQuery query)
    {
        return new UserResponse(query.UserId, "John Doe");
    }
}
```

### 3. Use the Mediator

```csharp
public class UserService
{
    private readonly IMediator _mediator;
    
    public UserService(IMediator mediator)
    {
        _mediator = mediator;
    }
    
    public async Task<UserResponse> GetUserAsync(int userId)
    {
        var query = new GetUserQuery(userId);
        return await _mediator.InvokeAsync<UserResponse>(query);
    }
}
```

## Handler Examples

### Basic Handler
```csharp
public class CreateOrderCommandHandler
{
    public OrderCreatedResponse Handle(CreateOrderCommand command)
    {
        return new OrderCreatedResponse(Guid.NewGuid().ToString());
    }
}
```

### Async Handler with Dependencies
```csharp
public class GetProductQueryHandler
{
    public async Task<ProductDto> HandleAsync(
        GetProductQuery query,
        IProductRepository repository,
        ILogger<GetProductQueryHandler> logger)
    {
        logger.LogInformation("Getting product {ProductId}", query.ProductId);
        return await repository.GetByIdAsync(query.ProductId);
    }
}
```

### Static Handler
```csharp
public static class ProcessOrderHandler
{
    public static async Task<OrderResult> Handle(
        ProcessOrderCommand command,
        IOrderService orderService)
    {
        return await orderService.ProcessAsync(command.OrderId);
    }
}
```

### Handler with CancellationToken
```csharp
public class LongRunningQueryHandler
{
    public async Task<Result> HandleAsync(
        LongRunningQuery query,
        IDataService dataService,
        CancellationToken cancellationToken)
    {
        return await dataService.ProcessAsync(query, cancellationToken);
    }
}
```

## Features

- ✅ Type-safe request/response handling
- ✅ Dependency injection support
- ✅ Timeout support
- ✅ Cancellation token support
- ✅ Convention-based handler discovery
- ✅ Support for sync and async handlers
- ✅ Support for static and instance methods
- ✅ Clear error messages

## Implementation Details

- **Handler Resolution**: Handlers are discovered at startup and cached
- **One Handler Per Message**: Only one handler per message type is supported
- **Reflection-Based**: Uses `MethodInfo.Invoke` for handler execution
- **DI Integration**: Dependencies are resolved from `IServiceProvider`

## Future Enhancements

Potential improvements:
- Source generator for compile-time handler generation
- Middleware pipeline support
- Validation integration
- Structured logging
- Metrics and telemetry
- Publish/subscribe pattern support
