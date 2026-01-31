using Opossum.Core;
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

    #region CommandResult Tests

    [Fact]
    public async Task InvokeAsync_WithCommandResult_ReturnsSuccessfulResult()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator();
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();
        var command = new SuccessfulCommand("test");

        // Act
        var result = await mediator.InvokeAsync<CommandResult>(command);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task InvokeAsync_WithCommandResult_ReturnsFailedResult()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator();
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();
        var command = new FailingCommand("test");

        // Act
        var result = await mediator.InvokeAsync<CommandResult>(command);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("Command failed: test", result.ErrorMessage);
    }

    [Fact]
    public async Task InvokeAsync_WithCommandResultGeneric_ReturnsSuccessfulResultWithValue()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator();
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();
        var query = new GetStudentQuery(Guid.NewGuid());

        // Act
        var result = await mediator.InvokeAsync<CommandResult<StudentDto>>(query);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.Value);
        Assert.Equal(query.StudentId, result.Value.Id);
        Assert.Equal("John Doe", result.Value.Name);
    }

    [Fact]
    public async Task InvokeAsync_WithCommandResultGeneric_ReturnsFailedResultWithoutValue()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator();
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();
        var query = new GetStudentQuery(Guid.Empty); // Empty Guid triggers failure

        // Act
        var result = await mediator.InvokeAsync<CommandResult<StudentDto>>(query);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("Student not found", result.ErrorMessage);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task InvokeAsync_WithCommandResultList_ReturnsSuccessfulResultWithList()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator();
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();
        var query = new GetAllStudentsQuery();

        // Act
        var result = await mediator.InvokeAsync<CommandResult<List<StudentDto>>>(query);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.Value);
        Assert.Equal(3, result.Value.Count);
        Assert.Contains(result.Value, s => s.Name == "Alice");
        Assert.Contains(result.Value, s => s.Name == "Bob");
        Assert.Contains(result.Value, s => s.Name == "Charlie");
    }

    [Fact]
    public async Task InvokeAsync_WithCommandResultComplex_HandlesBusinessLogicValidation()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator();
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();
        var command = new CreateStudentCommand("", "test@example.com"); // Empty name triggers validation

        // Act
        var result = await mediator.InvokeAsync<CommandResult<Guid>>(command);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Contains("Name cannot be empty", result.ErrorMessage);
        Assert.Equal(Guid.Empty, result.Value);
    }

    [Fact]
    public async Task InvokeAsync_WithCommandResultComplex_ReturnsCreatedId()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator();
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();
        var command = new CreateStudentCommand("John Smith", "john@example.com");

        // Act
        var result = await mediator.InvokeAsync<CommandResult<Guid>>(command);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.NotEqual(Guid.Empty, result.Value);
    }

    #endregion
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

// CommandResult test messages
public record SuccessfulCommand(string Data);
public record FailingCommand(string Data);
public record GetStudentQuery(Guid StudentId);
public record GetAllStudentsQuery();
public record CreateStudentCommand(string Name, string Email);

// CommandResult test DTOs
public record StudentDto(Guid Id, string Name, string Email);

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

// CommandResult test handlers
public class SuccessfulCommandHandler
{
    public CommandResult Handle(SuccessfulCommand command)
    {
        return CommandResult.Ok();
    }
}

public class FailingCommandHandler
{
    public CommandResult Handle(FailingCommand command)
    {
        return CommandResult.Fail($"Command failed: {command.Data}");
    }
}

public class GetStudentQueryHandler
{
    public CommandResult<StudentDto> Handle(GetStudentQuery query)
    {
        if (query.StudentId == Guid.Empty)
        {
            return CommandResult<StudentDto>.Fail("Student not found");
        }

        var student = new StudentDto(query.StudentId, "John Doe", "john@example.com");
        return CommandResult<StudentDto>.Ok(student);
    }
}

public class GetAllStudentsQueryHandler
{
    public CommandResult<List<StudentDto>> Handle(GetAllStudentsQuery query)
    {
        var students = new List<StudentDto>
        {
            new(Guid.NewGuid(), "Alice", "alice@example.com"),
            new(Guid.NewGuid(), "Bob", "bob@example.com"),
            new(Guid.NewGuid(), "Charlie", "charlie@example.com")
        };

        return CommandResult<List<StudentDto>>.Ok(students);
    }
}

public class CreateStudentCommandHandler
{
    public CommandResult<Guid> Handle(CreateStudentCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return CommandResult<Guid>.Fail("Name cannot be empty");
        }

        var studentId = Guid.NewGuid();
        return CommandResult<Guid>.Ok(studentId);
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
