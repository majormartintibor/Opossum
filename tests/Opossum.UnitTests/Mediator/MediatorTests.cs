using Opossum.Mediator;

namespace Opossum.UnitTests.Mediator;

public class MediatorTests
{
    [Fact]
    public async Task InvokeAsync_WithValidHandler_ReturnsResponse()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator();
        var provider = services.BuildServiceProvider();
        
        var mediator = provider.GetRequiredService<IMediator>();
        var query = new MediatorTestQuery(42);

        // Act
        var result = await mediator.InvokeAsync<MediatorTestResponse>(query);
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(42, result.Value);
    }
    
    [Fact]
    public async Task InvokeAsync_WithNullMessage_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator();
        var provider = services.BuildServiceProvider();
        
        var mediator = provider.GetRequiredService<IMediator>();
        
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            mediator.InvokeAsync<MediatorTestResponse>(null!));
    }
    
    [Fact]
    public async Task InvokeAsync_WithNoHandler_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator();
        var provider = services.BuildServiceProvider();
        
        var mediator = provider.GetRequiredService<IMediator>();
        var query = new UnhandledQuery();
        
        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
            mediator.InvokeAsync<MediatorTestResponse>(query));
        
        Assert.Contains("No handler registered", exception.Message);
        Assert.Contains(nameof(UnhandledQuery), exception.Message);
    }
    
    [Fact]
    public async Task InvokeAsync_WithWrongResponseType_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator();
        var provider = services.BuildServiceProvider();
        
        var mediator = provider.GetRequiredService<IMediator>();
        var query = new MediatorTestQuery(42);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
            mediator.InvokeAsync<WrongResponse>(query));
        
        Assert.Contains("Handler returned type", exception.Message);
        Assert.Contains(nameof(MediatorTestResponse), exception.Message);
        Assert.Contains(nameof(WrongResponse), exception.Message);
    }
    
    [Fact]
    public async Task InvokeAsync_WithCancellationToken_PassesCancellationToHandler()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator();
        var provider = services.BuildServiceProvider();
        
        var mediator = provider.GetRequiredService<IMediator>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        
        var command = new CancellableCommand();
        
        // Act & Assert
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => 
            mediator.InvokeAsync<CancellableResponse>(command, cts.Token));
    }
    
    [Fact]
    public async Task InvokeAsync_WithTimeout_CancelsAfterTimeout()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator();
        var provider = services.BuildServiceProvider();
        
        var mediator = provider.GetRequiredService<IMediator>();
        var command = new SlowCommand();
        
        // Act & Assert
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => 
            mediator.InvokeAsync<SlowResponse>(
                command, 
                timeout: TimeSpan.FromMilliseconds(10)));
    }
    
    [Fact]
    public async Task InvokeAsync_WithAsyncHandler_ReturnsResponse()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator();
        var provider = services.BuildServiceProvider();
        
        var mediator = provider.GetRequiredService<IMediator>();
        var command = new AsyncCommand(100);
        
        // Act
        var result = await mediator.InvokeAsync<AsyncResponse>(command);
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(100, result.ProcessedValue);
    }
    
    [Fact]
    public async Task InvokeAsync_WithDependencyInjection_ResolvesDependencies()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ITestService, TestService>();
        services.AddMediator();
        var provider = services.BuildServiceProvider();
        
        var mediator = provider.GetRequiredService<IMediator>();
        var command = new CommandWithDependencies("test");
        
        // Act
        var result = await mediator.InvokeAsync<DependencyResponse>(command);
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal("test-processed", result.Result);
    }
    
    [Fact]
    public async Task InvokeAsync_WithStaticHandler_ExecutesSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator();
        var provider = services.BuildServiceProvider();
        
        var mediator = provider.GetRequiredService<IMediator>();
        var query = new MediatorStaticQuery(999);

        // Act
        var result = await mediator.InvokeAsync<MediatorStaticResponse>(query);
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(999, result.StaticValue);
    }
}

// Test messages and responses for MediatorTests
public record MediatorTestQuery(int Value);
public record MediatorTestResponse(int Value);

public record UnhandledQuery();

public record WrongResponse(string Data);

public record CancellableCommand();
public record CancellableResponse();

public record SlowCommand();
public record SlowResponse();

public record AsyncCommand(int Value);
public record AsyncResponse(int ProcessedValue);

public record CommandWithDependencies(string Input);
public record DependencyResponse(string Result);

public record MediatorStaticQuery(int Value);
public record MediatorStaticResponse(int StaticValue);

// Test handlers
public class MediatorTestQueryHandler
{
    public MediatorTestResponse Handle(MediatorTestQuery query)
    {
        return new MediatorTestResponse(query.Value);
    }
}

public class CancellableCommandHandler
{
    public async Task<CancellableResponse> HandleAsync(
        CancellableCommand command,
        CancellationToken cancellationToken)
    {
        await Task.Delay(1000, cancellationToken);
        return new CancellableResponse();
    }
}

public class SlowCommandHandler
{
    public async Task<SlowResponse> HandleAsync(SlowCommand command, CancellationToken cancellationToken)
    {
        await Task.Delay(5000, cancellationToken);
        return new SlowResponse();
    }
}

public class AsyncCommandHandler
{
    public async Task<AsyncResponse> HandleAsync(AsyncCommand command)
    {
        await Task.Delay(10);
        return new AsyncResponse(command.Value);
    }
}

public class CommandWithDependenciesHandler
{
    public DependencyResponse Handle(
        CommandWithDependencies command,
        ITestService testService)
    {
        var result = testService.Process(command.Input);
        return new DependencyResponse(result);
    }
}

public static class MediatorStaticQueryHandler
{
    public static MediatorStaticResponse Handle(MediatorStaticQuery query)
    {
        return new MediatorStaticResponse(query.Value);
    }
}

// Test service
public interface ITestService
{
    string Process(string input);
}

public class TestService : ITestService
{
    public string Process(string input)
    {
        return $"{input}-processed";
    }
}
