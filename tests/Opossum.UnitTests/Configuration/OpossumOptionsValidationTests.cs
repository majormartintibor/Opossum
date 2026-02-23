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
        options.UseStore("ValidContext");

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
        var options = new OpossumOptions { RootPath = "" };
        options.UseStore("ValidContext");

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
        options.UseStore("ValidContext");

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
        var options = new OpossumOptions { RootPath = "relative/path" };
        options.UseStore("ValidContext");

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
        options.UseStore("ValidContext");

        var validator = new OpossumOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("invalid characters", string.Join(", ", result.Failures));
    }

    [Fact]
    public void Validate_NoStoreName_ReturnsFail()
    {
        // Arrange
        var options = new OpossumOptions
        {
            RootPath = GetValidAbsolutePath()
        };
        // UseStore not called

        var validator = new OpossumOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("StoreName must be configured", string.Join(", ", result.Failures));
    }

    [Fact]
    public void UseStore_WithEmptyName_ThrowsArgumentException()
    {
        // Arrange
        var options = new OpossumOptions { RootPath = GetValidAbsolutePath() };

        // Act & Assert — UseStore validates before setting, so the validator is never reached
        Assert.Throws<ArgumentException>(() => options.UseStore(""));
    }

    [Fact]
    public void UseStore_WithInvalidCharacters_ThrowsArgumentException()
    {
        // Arrange
        var options = new OpossumOptions { RootPath = GetValidAbsolutePath() };

        // Act & Assert — UseStore validates before setting, so the validator is never reached
        Assert.Throws<ArgumentException>(() => options.UseStore("Invalid\0Name"));
    }

    [Fact]
    public void UseStore_WithReservedName_ThrowsArgumentException()
    {
        // Arrange — CON is a Windows-reserved device name, rejected by IsValidDirectoryName
        var options = new OpossumOptions { RootPath = GetValidAbsolutePath() };

        // Act & Assert
        // CON passes Opossum's forbidden-char check (no /, \, :, *, ?, ", <, >, |, \0)
        // but is caught by the validator's reserved-name check
        var validOptions = new OpossumOptions { RootPath = GetValidAbsolutePath() };
        validOptions.UseStore("CON");
        var validator = new OpossumOptionsValidator();
        var result = validator.Validate(null, validOptions);
        Assert.True(result.Failed);
        Assert.Contains("Invalid store name 'CON'", string.Join(", ", result.Failures));
    }

    [Fact]
    public void Validate_ValidStoreName_ReturnsSuccess()
    {
        // Arrange
        var options = new OpossumOptions { RootPath = GetValidAbsolutePath() };
        options.UseStore("CourseManagement");

        var validator = new OpossumOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_MultipleFailures_ReturnsAllErrors()
    {
        // Arrange — invalid RootPath AND no StoreName
        var options = new OpossumOptions { RootPath = "relative/path" };

        var validator = new OpossumOptionsValidator();

        // Act
        var result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.True(result.Failures.Count() >= 2, "Should report both RootPath and StoreName failures");
    }
}
