using Opossum.Configuration;

namespace Opossum.UnitTests.Configuration;

public class OpossumOptionsTests
{
    [Fact]
    public void Constructor_SetsDefaultRootPath()
    {
        // Arrange & Act
        var options = new OpossumOptions();

        // Assert
        Assert.Equal("OpossumStore", options.RootPath);
    }

    [Fact]
    public void Constructor_InitializesEmptyContextsList()
    {
        // Arrange & Act
        var options = new OpossumOptions();

        // Assert
        Assert.NotNull(options.Contexts);
        Assert.Empty(options.Contexts);
    }

    [Fact]
    public void AddContext_WithValidName_AddsContext()
    {
        // Arrange
        var options = new OpossumOptions();

        // Act
        var result = options.AddContext("CourseManagement");

        // Assert
        Assert.Single(options.Contexts);
        Assert.Contains("CourseManagement", options.Contexts);
        Assert.Same(options, result); // Fluent API
    }

    [Fact]
    public void AddContext_WithMultipleContexts_AddsAll()
    {
        // Arrange
        var options = new OpossumOptions();

        // Act
        options.AddContext("CourseManagement")
               .AddContext("StudentEnrollment")
               .AddContext("Billing");

        // Assert
        Assert.Equal(3, options.Contexts.Count);
        Assert.Contains("CourseManagement", options.Contexts);
        Assert.Contains("StudentEnrollment", options.Contexts);
        Assert.Contains("Billing", options.Contexts);
    }

    [Fact]
    public void AddContext_WithNullName_ThrowsArgumentException()
    {
        // Arrange
        var options = new OpossumOptions();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => options.AddContext(null!));
        Assert.Equal("contextName", exception.ParamName);
        Assert.Contains("cannot be empty", exception.Message);
    }

    [Fact]
    public void AddContext_WithEmptyName_ThrowsArgumentException()
    {
        // Arrange
        var options = new OpossumOptions();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => options.AddContext(""));
        Assert.Equal("contextName", exception.ParamName);
        Assert.Contains("cannot be empty", exception.Message);
    }

    [Fact]
    public void AddContext_WithWhitespaceName_ThrowsArgumentException()
    {
        // Arrange
        var options = new OpossumOptions();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => options.AddContext("   "));
        Assert.Equal("contextName", exception.ParamName);
        Assert.Contains("cannot be empty", exception.Message);
    }

    [Fact]
    public void AddContext_WithInvalidCharacters_ThrowsArgumentException()
    {
        // Arrange
        var options = new OpossumOptions();

        // Act & Assert - various invalid characters
        var exception1 = Assert.Throws<ArgumentException>(() => options.AddContext("Course/Management"));
        Assert.Contains("Invalid context name", exception1.Message);

        var exception2 = Assert.Throws<ArgumentException>(() => options.AddContext("Course\\Management"));
        Assert.Contains("Invalid context name", exception2.Message);

        var exception3 = Assert.Throws<ArgumentException>(() => options.AddContext("Course:Management"));
        Assert.Contains("Invalid context name", exception3.Message);

        var exception4 = Assert.Throws<ArgumentException>(() => options.AddContext("Course*Management"));
        Assert.Contains("Invalid context name", exception4.Message);

        var exception5 = Assert.Throws<ArgumentException>(() => options.AddContext("Course?Management"));
        Assert.Contains("Invalid context name", exception5.Message);
    }

    [Fact]
    public void AddContext_WithDuplicateName_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new OpossumOptions();
        options.AddContext("CourseManagement");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => 
            options.AddContext("CourseManagement"));
        Assert.Contains("already been added", exception.Message);
    }

    [Fact]
    public void AddContext_WithDuplicateNameDifferentCase_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new OpossumOptions();
        options.AddContext("CourseManagement");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => 
            options.AddContext("coursemanagement"));
        Assert.Contains("already been added", exception.Message);
    }

    [Fact]
    public void RootPath_CanBeSet()
    {
        // Arrange
        var options = new OpossumOptions();

        // Act
        options.RootPath = "/custom/path/to/store";

        // Assert
        Assert.Equal("/custom/path/to/store", options.RootPath);
    }

    [Fact]
    public void RootPath_CanBeSetToRelativePath()
    {
        // Arrange
        var options = new OpossumOptions();

        // Act
        options.RootPath = "./data/events";

        // Assert
        Assert.Equal("./data/events", options.RootPath);
    }

    [Theory]
    [InlineData("ValidContext")]
    [InlineData("Context123")]
    [InlineData("Context_With_Underscores")]
    [InlineData("Context-With-Dashes")]
    [InlineData("Context.With.Dots")]
    [InlineData("CourseManagement")]
    public void AddContext_WithValidNames_Succeeds(string contextName)
    {
        // Arrange
        var options = new OpossumOptions();

        // Act
        options.AddContext(contextName);

        // Assert
        Assert.Contains(contextName, options.Contexts);
    }

    [Fact]
    public void AddContext_PreservesOrder()
    {
        // Arrange
        var options = new OpossumOptions();

        // Act
        options.AddContext("First")
               .AddContext("Second")
               .AddContext("Third");

        // Assert
        Assert.Equal("First", options.Contexts[0]);
        Assert.Equal("Second", options.Contexts[1]);
        Assert.Equal("Third", options.Contexts[2]);
    }
}
