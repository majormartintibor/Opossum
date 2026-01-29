# Mediator Pattern Usage Rules

## Overview

The Opossum mediator pattern provides in-process request/response messaging with convention-based handler discovery.

## Handler Conventions

### 1. Handler Class Naming

Handlers MUST follow one of these naming conventions:

- ✅ End with `Handler` suffix: `GetUserQueryHandler`, `CreateOrderCommandHandler`
- ✅ Use `[MessageHandler]` attribute for non-conventional names

```csharp
// Conventional naming
public class GetUserQueryHandler { }

// Explicit attribute
[MessageHandler]
public class UserQueries { }
```

### 2. Handler Method Naming

Handler methods MUST use one of these names:

- `Handle` (sync)
- `HandleAsync` (async)
- `Consume` (sync)
- `ConsumeAsync` (async)

```csharp
// Valid method names
public UserResponse Handle(GetUserQuery query) { }
public Task<UserResponse> HandleAsync(GetUserQuery query) { }
```

### 3. Handler Method Signature

**Required:**
- First parameter is the message/request object
- Message type must be a concrete class or record

**Optional:**
- Additional parameters are resolved from DI
- `CancellationToken` parameter for cancellable operations
- Return type is the response (for request/response pattern)

```csharp
// Simple handler
public UserResponse Handle(GetUserQuery query)

// With dependencies
public async Task<OrderResult> HandleAsync(
    CreateOrderCommand command,
    IOrderRepository repository,
    ILogger<CreateOrderCommandHandler> logger)

// With cancellation
public async Task<Result> HandleAsync(
    ProcessDataCommand command,
    IDataService service,
    CancellationToken cancellationToken)
```

### 4. Handler Scope

Handlers can be:

- **Instance methods**: Handler class is instantiated per invocation
- **Static methods**: No handler instance created

```csharp
// Instance method
public class OrderHandler
{
    public OrderResponse Handle(CreateOrderCommand command) { }
}

// Static method
public static class OrderHandler
{
    public static OrderResponse Handle(CreateOrderCommand command) { }
}
```

## Message Conventions

### 1. Message Naming

Follow CQRS naming patterns:

- **Queries**: End with `Query` (e.g., `GetUserQuery`, `SearchProductsQuery`)
- **Commands**: End with `Command` (e.g., `CreateOrderCommand`, `UpdateUserCommand`)
- **Responses**: End with `Response`, `Result`, or use DTO suffix

```csharp
// Query
public record GetUserQuery(int UserId);
public record UserResponse(int Id, string Name, string Email);

// Command
public record CreateOrderCommand(string ProductId, int Quantity);
public record OrderCreatedResult(string OrderId, decimal Total);
```

### 2. Message Type

Messages MUST be:
- Concrete classes or records
- Not abstract or interfaces

```csharp
// ✅ Correct - record
public record GetUserQuery(int UserId);

// ✅ Correct - class
public class CreateOrderCommand
{
    public string ProductId { get; init; }
    public int Quantity { get; init; }
}

// ❌ Wrong - interface
public interface IGetUserQuery { }

// ❌ Wrong - abstract
public abstract class BaseQuery { }
```

## Registration

### Basic Registration

```csharp
// In Program.cs or DI setup
builder.Services.AddMediator();
```

### Include Additional Assemblies

```csharp
builder.Services.AddMediator(options =>
{
    options.Assemblies.Add(typeof(SomeHandler).Assembly);
});
```

## Usage Patterns

### 1. Inject IMediator

```csharp
public class UserService
{
    private readonly IMediator _mediator;
    
    public UserService(IMediator mediator)
    {
        _mediator = mediator;
    }
}
```

### 2. Invoke Handlers

```csharp
// Simple invocation
var response = await _mediator.InvokeAsync<UserResponse>(new GetUserQuery(userId));

// With cancellation
var response = await _mediator.InvokeAsync<UserResponse>(
    new GetUserQuery(userId), 
    cancellationToken);

// With timeout
var response = await _mediator.InvokeAsync<UserResponse>(
    new GetUserQuery(userId), 
    timeout: TimeSpan.FromSeconds(30));

// With both
var response = await _mediator.InvokeAsync<UserResponse>(
    new GetUserQuery(userId), 
    cancellationToken, 
    TimeSpan.FromSeconds(30));
```

## Best Practices

### 1. One Handler Per Message Type

Each message type MUST have exactly one handler:

```csharp
// ✅ Correct - one handler
public class GetUserQueryHandler
{
    public UserResponse Handle(GetUserQuery query) { }
}

// ❌ Wrong - multiple handlers for same message
public class GetUserQueryHandler1
{
    public UserResponse Handle(GetUserQuery query) { }
}
public class GetUserQueryHandler2
{
    public UserResponse Handle(GetUserQuery query) { }
}
```

### 2. Keep Messages Immutable

Use records or init-only properties:

```csharp
// ✅ Correct - immutable record
public record CreateOrderCommand(string ProductId, int Quantity);

// ✅ Correct - init-only properties
public class CreateOrderCommand
{
    public string ProductId { get; init; }
    public int Quantity { get; init; }
}

// ❌ Avoid - mutable
public class CreateOrderCommand
{
    public string ProductId { get; set; }
    public int Quantity { get; set; }
}
```

### 3. Handler Responsibilities

Handlers should:
- ✅ Contain business logic for the specific message
- ✅ Coordinate between multiple services
- ✅ Handle errors appropriately
- ❌ Not call other handlers directly (use mediator)
- ❌ Not contain infrastructure concerns

### 4. Testing Handlers

Test handlers directly without the mediator:

```csharp
[Fact]
public async Task Handle_ValidQuery_ReturnsUser()
{
    // Arrange
    var repository = new Mock<IUserRepository>();
    repository.Setup(r => r.GetByIdAsync(1))
        .ReturnsAsync(new User(1, "John"));
    
    var handler = new GetUserQueryHandler();
    var query = new GetUserQuery(1);
    
    // Act
    var result = await handler.HandleAsync(query, repository.Object);
    
    // Assert
    Assert.NotNull(result);
    Assert.Equal("John", result.Name);
}
```

## Error Handling

### Common Exceptions

- **`ArgumentNullException`**: Null message passed
- **`InvalidOperationException`**: No handler registered for message type
- **`InvalidOperationException`**: Handler returned wrong type
- **`OperationCanceledException`**: Timeout or cancellation occurred

### Handler Exceptions

Exceptions from handlers bubble up to the caller:

```csharp
public class OrderHandler
{
    public OrderResponse Handle(CreateOrderCommand command)
    {
        if (command.Quantity <= 0)
        {
            throw new ArgumentException("Quantity must be positive");
        }
        
        // Handle the command
    }
}

// Usage
try
{
    var result = await _mediator.InvokeAsync<OrderResponse>(command);
}
catch (ArgumentException ex)
{
    // Handle validation error
}
catch (InvalidOperationException ex)
{
    // Handle missing handler
}
```

## Integration with Opossum

### Using Statements

Follow Opossum conventions:

```csharp
// In handler files - keep Opossum.* usings
using Opossum.Configuration;
using Opossum.Storage;

namespace Opossum.MyFeature;

// External usings like Microsoft.* are in GlobalUsings.cs
public class MyHandler
{
    public async Task<Result> HandleAsync(
        MyCommand command,
        ILogger<MyHandler> logger) // Microsoft.Extensions.Logging from GlobalUsings
    {
        // Implementation
    }
}
```

### Service Registration

Add mediator with other Opossum services:

```csharp
builder.Services.AddOpossum();
builder.Services.AddMediator(options =>
{
    options.Assemblies.Add(typeof(OpossumOptions).Assembly);
});
```

## Common Patterns

### Query Handler Pattern

```csharp
public record GetUserQuery(int UserId);
public record UserDto(int Id, string Name, string Email);

public class GetUserQueryHandler
{
    public async Task<UserDto> HandleAsync(
        GetUserQuery query,
        IUserRepository repository)
    {
        var user = await repository.GetByIdAsync(query.UserId);
        return new UserDto(user.Id, user.Name, user.Email);
    }
}
```

### Command Handler Pattern

```csharp
public record CreateUserCommand(string Name, string Email);
public record UserCreatedResult(int UserId);

public class CreateUserCommandHandler
{
    public async Task<UserCreatedResult> HandleAsync(
        CreateUserCommand command,
        IUserRepository repository,
        ILogger<CreateUserCommandHandler> logger)
    {
        logger.LogInformation("Creating user {Name}", command.Name);
        
        var user = new User { Name = command.Name, Email = command.Email };
        await repository.AddAsync(user);
        
        return new UserCreatedResult(user.Id);
    }
}
```

### Handler with Validation

```csharp
public class CreateOrderCommandHandler
{
    public async Task<OrderResult> HandleAsync(
        CreateOrderCommand command,
        IOrderService orderService,
        ILogger<CreateOrderCommandHandler> logger)
    {
        // Validate
        if (command.Quantity <= 0)
        {
            throw new ArgumentException("Quantity must be positive", nameof(command.Quantity));
        }
        
        // Process
        logger.LogInformation("Creating order for {ProductId}", command.ProductId);
        var order = await orderService.CreateAsync(command);
        
        return new OrderResult(order.Id, order.Total);
    }
}
```
