using Opossum.Core;

namespace Opossum.UnitTests.Core;

/// <summary>
/// Unit tests for CommandResult and CommandResult&lt;T&gt;
/// </summary>
public class CommandResultTests
{
    #region CommandResult (non-generic) Tests

    [Fact]
    public void CommandResult_Constructor_SetsPropertiesCorrectly()
    {
        // Arrange & Act
        var result = new CommandResult(Success: true, ErrorMessage: "Test error");

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Test error", result.ErrorMessage);
    }

    [Fact]
    public void CommandResult_Ok_CreatesSuccessfulResult()
    {
        // Act
        var result = CommandResult.Ok();

        // Assert
        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void CommandResult_Fail_CreatesFailedResultWithMessage()
    {
        // Arrange
        var errorMessage = "Operation failed";

        // Act
        var result = CommandResult.Fail(errorMessage);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(errorMessage, result.ErrorMessage);
    }

    [Fact]
    public void CommandResult_WithOptionalErrorMessage_DefaultsToNull()
    {
        // Act
        var result = new CommandResult(Success: true);

        // Assert
        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void CommandResult_IsRecord_SupportsValueEquality()
    {
        // Arrange
        var result1 = new CommandResult(Success: true, ErrorMessage: "Error");
        var result2 = new CommandResult(Success: true, ErrorMessage: "Error");

        // Assert
        Assert.Equal(result1, result2);
        Assert.True(result1 == result2);
    }

    [Fact]
    public void CommandResult_DifferentInstances_AreNotEqual()
    {
        // Arrange
        var result1 = CommandResult.Ok();
        var result2 = CommandResult.Fail("Error");

        // Assert
        Assert.NotEqual(result1, result2);
        Assert.False(result1 == result2);
    }

    #endregion

    #region CommandResult<T> (generic) Tests

    [Fact]
    public void CommandResultGeneric_Constructor_SetsPropertiesCorrectly()
    {
        // Arrange & Act
        var result = new CommandResult<string>(Success: true, Value: "test", ErrorMessage: "Error");

        // Assert
        Assert.True(result.Success);
        Assert.Equal("test", result.Value);
        Assert.Equal("Error", result.ErrorMessage);
    }

    [Fact]
    public void CommandResultGeneric_Ok_CreatesSuccessfulResultWithValue()
    {
        // Arrange
        var testValue = "Test Value";

        // Act
        var result = CommandResult<string>.Ok(testValue);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(testValue, result.Value);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void CommandResultGeneric_Fail_CreatesFailedResultWithoutValue()
    {
        // Arrange
        var errorMessage = "Operation failed";

        // Act
        var result = CommandResult<string>.Fail(errorMessage);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.Value);
        Assert.Equal(errorMessage, result.ErrorMessage);
    }

    [Fact]
    public void CommandResultGeneric_WithComplexType_StoresValueCorrectly()
    {
        // Arrange
        var testObject = new TestData { Id = 42, Name = "Test" };

        // Act
        var result = CommandResult<TestData>.Ok(testObject);

        // Assert
        Assert.True(result.Success);
        Assert.Same(testObject, result.Value);
        Assert.Equal(42, result.Value!.Id);
        Assert.Equal("Test", result.Value.Name);
    }

    [Fact]
    public void CommandResultGeneric_WithValueType_WorksCorrectly()
    {
        // Act
        var result = CommandResult<int>.Ok(123);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(123, result.Value);
    }

    [Fact]
    public void CommandResultGeneric_IsRecord_SupportsValueEquality()
    {
        // Arrange
        var result1 = new CommandResult<int>(Success: true, Value: 42);
        var result2 = new CommandResult<int>(Success: true, Value: 42);

        // Assert
        Assert.Equal(result1, result2);
        Assert.True(result1 == result2);
    }

    [Fact]
    public void CommandResultGeneric_DifferentValues_AreNotEqual()
    {
        // Arrange
        var result1 = CommandResult<int>.Ok(42);
        var result2 = CommandResult<int>.Ok(99);

        // Assert
        Assert.NotEqual(result1, result2);
        Assert.False(result1 == result2);
    }

    [Fact]
    public void CommandResultGeneric_WithList_WorksCorrectly()
    {
        // Arrange
        var list = new List<string> { "Item1", "Item2", "Item3" };

        // Act
        var result = CommandResult<List<string>>.Ok(list);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.Value!.Count);
        Assert.Contains("Item2", result.Value);
    }

    [Fact]
    public void CommandResultGeneric_FailureWithDefaultValue_HasNullValue()
    {
        // Act
        var result = CommandResult<string>.Fail("Error occurred");

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.Value);
        Assert.Equal("Error occurred", result.ErrorMessage);
    }

    [Fact]
    public void CommandResultGeneric_WithNullValue_IsAllowed()
    {
        // Act
        var result = new CommandResult<string?>(Success: true, Value: null);

        // Assert
        Assert.True(result.Success);
        Assert.Null(result.Value);
    }

    [Fact]
    public void CommandResultGeneric_WithOptionalParameters_UsesDefaults()
    {
        // Act
        var result = new CommandResult<int>(Success: true);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(default, result.Value);
        Assert.Null(result.ErrorMessage);
    }

    #endregion

    #region Integration Scenarios

    [Fact]
    public void CommandResult_TypicalUsagePattern_WorksAsExpected()
    {
        // Simulate a command handler
        CommandResult ExecuteCommand(bool shouldSucceed)
        {
            if (shouldSucceed)
                return CommandResult.Ok();
            else
                return CommandResult.Fail("Command execution failed");
        }

        // Act & Assert - Success case
        var successResult = ExecuteCommand(true);
        Assert.True(successResult.Success);

        // Act & Assert - Failure case
        var failResult = ExecuteCommand(false);
        Assert.False(failResult.Success);
        Assert.Equal("Command execution failed", failResult.ErrorMessage);
    }

    [Fact]
    public void CommandResultGeneric_TypicalUsagePattern_WorksAsExpected()
    {
        // Simulate a query handler
        CommandResult<List<string>> ExecuteQuery(bool shouldSucceed)
        {
            if (shouldSucceed)
                return CommandResult<List<string>>.Ok(["Result1", "Result2"]);
            else
                return CommandResult<List<string>>.Fail("Query execution failed");
        }

        // Act & Assert - Success case
        var successResult = ExecuteQuery(true);
        Assert.True(successResult.Success);
        Assert.NotNull(successResult.Value);
        Assert.Equal(2, successResult.Value.Count);

        // Act & Assert - Failure case
        var failResult = ExecuteQuery(false);
        Assert.False(failResult.Success);
        Assert.Null(failResult.Value);
        Assert.Equal("Query execution failed", failResult.ErrorMessage);
    }

    #endregion

    #region Helper Classes

    private class TestData
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    #endregion
}
