using Opossum.Configuration;

namespace Opossum.UnitTests.Configuration;

/// <summary>
/// Unit tests for OpossumOptions validation.
/// Tests the validation logic to ensure invalid configurations are rejected.
/// </summary>
public sealed class OpossumOptionsValidationTests
{
    /// <summary>
    /// Returns a platform-appropriate absolute path for testing.
    /// Windows: C:\TestPath
    /// Linux: /tmp/TestPath
    /// </summary>
    private static string GetValidAbsolutePath() =>
        OperatingSystem.IsWindows() ? "C:\\TestPath" : "/tmp/TestPath";

    /// <summary>
    /// Returns a platform-appropriate absolute path with invalid characters.
    /// Windows: C:\Invalid|Path (| is invalid)
    /// Linux: /tmp/Invalid\0Path (\0 null character is invalid)
    /// </summary>
    private static string GetPathWithInvalidCharacters() =>
        OperatingSystem.IsWindows()
            ? "C:\\Invalid|Path"  // | is invalid on Windows
            : "/tmp/Invalid\0Path";  // \0 is invalid on Linux

    [Fact]
    public void Validate_ValidOptions_ReturnsSuccess()
    {
        // Arrange
        var options = new OpossumOptions
        {
            RootPath = GetValidAbsolutePath()
        };
        options.AddContext("ValidContext");

        var validator = new OpossumOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_EmptyRootPath_ReturnsFail()
    {
        // Arrange
        var options = new OpossumOptions
        {
            RootPath = ""
        };
        options.AddContext("ValidContext");

        var validator = new OpossumOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("cannot be null or empty", string.Join(", ", result.Failures));
    }

    [Fact]
    public void Validate_NullRootPath_ReturnsFail()
    {
        // Arrange
        var options = new OpossumOptions
        {
            RootPath = null!
        };
        options.AddContext("ValidContext");

        var validator = new OpossumOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("cannot be null or empty", string.Join(", ", result.Failures));
    }

    [Fact]
    public void Validate_RelativePath_ReturnsFail()
    {
        // Arrange
        var options = new OpossumOptions
        {
            RootPath = "relative/path"
        };
        options.AddContext("ValidContext");

        var validator = new OpossumOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("must be an absolute path", string.Join(", ", result.Failures));
    }

    [Fact]
    public void Validate_InvalidPathCharacters_ReturnsFail()
    {
        // Arrange
        var options = new OpossumOptions
        {
            RootPath = GetPathWithInvalidCharacters()
        };
        options.AddContext("ValidContext");

        var validator = new OpossumOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("invalid characters", string.Join(", ", result.Failures));
    }

    [Fact]
    public void Validate_NoContexts_ReturnsFail()
    {
        // Arrange
        var options = new OpossumOptions
        {
            RootPath = GetValidAbsolutePath()
        };
        // Don't add any contexts

        var validator = new OpossumOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("At least one context must be configured", string.Join(", ", result.Failures));
    }

    [Fact]
    public void Validate_EmptyContextName_ReturnsFail()
    {
        // Arrange
        var options = new OpossumOptions
        {
            RootPath = GetValidAbsolutePath()
        };

        // Manually add invalid context (bypassing AddContext validation)
        options.Contexts.Add("");

        var validator = new OpossumOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("cannot be null or whitespace", string.Join(", ", result.Failures));
    }

    [Fact]
    public void Validate_InvalidContextName_ReturnsFail()
    {
        // Arrange
        var options = new OpossumOptions
        {
            RootPath = GetValidAbsolutePath()
        };

        // Manually add invalid context with null character (invalid on ALL platforms)
        options.Contexts.Add("Invalid\0Context");  // \0 (null) is invalid on Windows AND Linux

        var validator = new OpossumOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("Invalid context name", string.Join(", ", result.Failures));
    }

    [Fact]
    public void Validate_ReservedContextName_ReturnsFail()
    {
        // Arrange
        var options = new OpossumOptions
        {
            RootPath = GetValidAbsolutePath()
        };

        // Try to use Windows reserved name
        options.Contexts.Add("CON");  // Reserved Windows name

        var validator = new OpossumOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("Invalid context name 'CON'", string.Join(", ", result.Failures));
    }

    [Fact]
    public void Validate_MultipleValidContexts_ReturnsSuccess()
    {
        // Arrange
        var options = new OpossumOptions
        {
            RootPath = GetValidAbsolutePath()
        };
        options.AddContext("Context1");
        options.AddContext("Context2");
        options.AddContext("Context3");

        var validator = new OpossumOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_MultipleFailures_ReturnsAllErrors()
    {
        // Arrange
        var options = new OpossumOptions
        {
            RootPath = "relative/path"  // Invalid: relative
        };
        // No contexts added - also invalid

        var validator = new OpossumOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.True(result.Failures.Count() >= 2, "Should have multiple validation failures");
    }
}
