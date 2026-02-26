using Opossum.Mediator;

namespace Opossum.UnitTests.Mediator;

/// <summary>
/// Integration tests that verify the entire mediator pipeline
/// </summary>
public class MediatorIntegrationTests
{
    [Fact]
    public async Task EndToEnd_SimpleQuery_ReturnsResultAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator();
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.InvokeAsync<IntegrationUserResponse>(
            new GetIntegrationUserQuery(1));

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.UserId);
        Assert.Equal("Test User", result.Name);
    }

    [Fact]
    public async Task EndToEnd_CommandWithDependencies_ProcessesSuccessfullyAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationRepository, IntegrationRepository>();
        services.AddMediator();
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.InvokeAsync<IntegrationOrderResult>(
            new CreateIntegrationOrderCommand("PROD-123", 5));

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.OrderId);
        Assert.Equal(5, result.Quantity);
    }

    [Fact]
    public async Task EndToEnd_AsyncHandlerWithLogging_ExecutesSuccessfullyAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediator();
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.InvokeAsync<IntegrationProcessResult>(
            new ProcessIntegrationDataCommand("test-data"));

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("test-data processed", result.Message);
    }

    [Fact]
    public async Task EndToEnd_StaticHandler_ExecutesCorrectlyAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator();
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.InvokeAsync<IntegrationCalculationResult>(
            new CalculateIntegrationQuery(10, 20));

        // Assert
        Assert.NotNull(result);
        Assert.Equal(30, result.Sum);
        Assert.Equal(200, result.Product);
    }

    [Fact]
    public async Task EndToEnd_WithCancellation_CancelsOperationAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator();
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            mediator.InvokeAsync<IntegrationLongResult>(
                new LongRunningIntegrationCommand(),
                cts.Token));
    }

    [Fact]
    public async Task EndToEnd_MultipleHandlers_AllWorkIndependentlyAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator();
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result1 = await mediator.InvokeAsync<IntegrationUserResponse>(
            new GetIntegrationUserQuery(1));
        var result2 = await mediator.InvokeAsync<IntegrationCalculationResult>(
            new CalculateIntegrationQuery(5, 10));

        // Assert
        Assert.NotNull(result1);
        Assert.Equal(1, result1.UserId);

        Assert.NotNull(result2);
        Assert.Equal(15, result2.Sum);
    }

    [Fact]
    public async Task EndToEnd_HandlerThrowsException_PropagatesExceptionAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator();
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.InvokeAsync<IntegrationErrorResult>(
                new ThrowingIntegrationCommand()));

        Assert.Equal("Integration test error", exception.Message);
    }

    [Fact]
    public async Task EndToEnd_ComplexScenario_WithMultipleDependenciesAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationRepository, IntegrationRepository>();
        services.AddSingleton<IIntegrationValidator, IntegrationValidator>();
        services.AddLogging();
        services.AddMediator();
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.InvokeAsync<IntegrationComplexResult>(
            new ComplexIntegrationCommand("test", 100));

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.NotNull(result.ProcessedData);
        Assert.Contains("test", result.ProcessedData);
    }
}

// Integration test messages
public record GetIntegrationUserQuery(int UserId);
public record IntegrationUserResponse(int UserId, string Name);

public record CreateIntegrationOrderCommand(string ProductId, int Quantity);
public record IntegrationOrderResult(string OrderId, int Quantity);

public record ProcessIntegrationDataCommand(string Data);
public record IntegrationProcessResult(bool Success, string Message);

public record CalculateIntegrationQuery(int A, int B);
public record IntegrationCalculationResult(int Sum, int Product);

public record LongRunningIntegrationCommand();
public record IntegrationLongResult();

public record ThrowingIntegrationCommand();
public record IntegrationErrorResult();

public record ComplexIntegrationCommand(string Data, int Value);
public record IntegrationComplexResult(bool IsValid, string ProcessedData);

// Integration test handlers
public class GetIntegrationUserQueryHandler
{
    public IntegrationUserResponse Handle(GetIntegrationUserQuery query)
    {
        return new IntegrationUserResponse(query.UserId, "Test User");
    }
}

public class CreateIntegrationOrderCommandHandler
{
    public async Task<IntegrationOrderResult> HandleAsync(
        CreateIntegrationOrderCommand command,
        IIntegrationRepository repository)
    {
        var orderId = await repository.CreateOrderAsync(command.ProductId, command.Quantity);
        return new IntegrationOrderResult(orderId, command.Quantity);
    }
}

public class ProcessIntegrationDataCommandHandler
{
    public async Task<IntegrationProcessResult> HandleAsync(
        ProcessIntegrationDataCommand command,
        ILogger<ProcessIntegrationDataCommandHandler> logger)
    {
        logger.LogInformation("Processing {Data}", command.Data);
        await Task.Delay(10);
        return new IntegrationProcessResult(true, $"{command.Data} processed");
    }
}

public static class CalculateIntegrationQueryHandler
{
    public static IntegrationCalculationResult Handle(CalculateIntegrationQuery query)
    {
        return new IntegrationCalculationResult(query.A + query.B, query.A * query.B);
    }
}

public class LongRunningIntegrationCommandHandler
{
    public async Task<IntegrationLongResult> HandleAsync(
        LongRunningIntegrationCommand command,
        CancellationToken cancellationToken)
    {
        await Task.Delay(10000, cancellationToken);
        return new IntegrationLongResult();
    }
}

public class ThrowingIntegrationCommandHandler
{
    public IntegrationErrorResult Handle(ThrowingIntegrationCommand command)
    {
        throw new InvalidOperationException("Integration test error");
    }
}

public class ComplexIntegrationCommandHandler
{
    public async Task<IntegrationComplexResult> HandleAsync(
        ComplexIntegrationCommand command,
        IIntegrationRepository repository,
        IIntegrationValidator validator,
        ILogger<ComplexIntegrationCommandHandler> logger)
    {
        logger.LogInformation("Handling complex command");

        var isValid = validator.Validate(command.Data);
        var processed = await repository.ProcessAsync(command.Data, command.Value);

        return new IntegrationComplexResult(isValid, processed);
    }
}

// Test services
public interface IIntegrationRepository
{
    Task<string> CreateOrderAsync(string productId, int quantity);
    Task<string> ProcessAsync(string data, int value);
}

public class IntegrationRepository : IIntegrationRepository
{
    public Task<string> CreateOrderAsync(string productId, int quantity)
    {
        return Task.FromResult(Guid.NewGuid().ToString());
    }

    public Task<string> ProcessAsync(string data, int value)
    {
        return Task.FromResult($"{data}-{value}");
    }
}

public interface IIntegrationValidator
{
    bool Validate(string data);
}

public class IntegrationValidator : IIntegrationValidator
{
    public bool Validate(string data)
    {
        return !string.IsNullOrEmpty(data);
    }
}
