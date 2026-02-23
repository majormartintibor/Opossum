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
    public void Constructor_StoreNameIsNullByDefault()
    {
        // Arrange & Act
        var options = new OpossumOptions();

        // Assert
        Assert.Null(options.StoreName);
    }

    [Fact]
    public void UseStore_WithValidName_SetsStoreName()
    {
        // Arrange
        var options = new OpossumOptions();

        // Act
        var result = options.UseStore("CourseManagement");

        // Assert
        Assert.Equal("CourseManagement", options.StoreName);
        Assert.Same(options, result); // Fluent API
    }

    [Fact]
    public void UseStore_WhenCalledTwice_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new OpossumOptions();
        options.UseStore("CourseManagement");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            options.UseStore("Billing"));
        Assert.Contains("UseStore has already been called", exception.Message);
    }

    [Fact]
    public void UseStore_WithNullName_ThrowsArgumentException()
    {
        // Arrange
        var options = new OpossumOptions();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => options.UseStore(null!));
        Assert.Equal("name", exception.ParamName);
        Assert.Contains("cannot be empty", exception.Message);
    }

    [Fact]
    public void UseStore_WithEmptyName_ThrowsArgumentException()
    {
        // Arrange
        var options = new OpossumOptions();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => options.UseStore(""));
        Assert.Equal("name", exception.ParamName);
        Assert.Contains("cannot be empty", exception.Message);
    }

    [Fact]
    public void UseStore_WithWhitespaceName_ThrowsArgumentException()
    {
        // Arrange
        var options = new OpossumOptions();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => options.UseStore("   "));
        Assert.Equal("name", exception.ParamName);
        Assert.Contains("cannot be empty", exception.Message);
    }

    [Fact]
    public void UseStore_WithInvalidCharacters_ThrowsArgumentException()
    {
        // Arrange
        var options = new OpossumOptions();

        // Act & Assert - various invalid characters
        var exception1 = Assert.Throws<ArgumentException>(() => options.UseStore("Course/Management"));
        Assert.Contains("Invalid store name", exception1.Message);

        var options2 = new OpossumOptions();
        var exception2 = Assert.Throws<ArgumentException>(() => options2.UseStore("Course\\Management"));
        Assert.Contains("Invalid store name", exception2.Message);

        var options3 = new OpossumOptions();
        var exception3 = Assert.Throws<ArgumentException>(() => options3.UseStore("Course:Management"));
        Assert.Contains("Invalid store name", exception3.Message);

        var options4 = new OpossumOptions();
        var exception4 = Assert.Throws<ArgumentException>(() => options4.UseStore("Course*Management"));
        Assert.Contains("Invalid store name", exception4.Message);

        var options5 = new OpossumOptions();
        var exception5 = Assert.Throws<ArgumentException>(() => options5.UseStore("Course?Management"));
        Assert.Contains("Invalid store name", exception5.Message);
    }

    [Fact]
    public void UseStore_WhenCalledTwiceWithSameName_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new OpossumOptions();
        options.UseStore("CourseManagement");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            options.UseStore("CourseManagement"));
        Assert.Contains("UseStore has already been called", exception.Message);
    }

    [Fact]
    public void RootPath_CanBeSet()
    {
        // Arrange
        var options = new OpossumOptions
        {
            // Act
            RootPath = "/custom/path/to/store"
        };

        // Assert
        Assert.Equal("/custom/path/to/store", options.RootPath);
    }

    [Fact]
    public void RootPath_CanBeSetToRelativePath()
    {
        // Arrange
        var options = new OpossumOptions
        {
            // Act
            RootPath = "./data/events"
        };

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
    public void UseStore_WithValidNames_SetsStoreName(string storeName)
    {
        // Arrange
        var options = new OpossumOptions();

        // Act
        options.UseStore(storeName);

        // Assert
        Assert.Equal(storeName, options.StoreName);
    }

    [Fact]
    public void UseStore_ReturnsOptionsForFluentChaining()
    {
        // Arrange
        var options = new OpossumOptions();

        // Act
        var result = options.UseStore("CourseManagement");

        // Assert — same instance returned for fluent chaining
        Assert.Same(options, result);
    }

    [Fact]
    public void Constructor_SetsFlushEventsImmediatelyToTrue_ByDefault()
    {
        // Arrange & Act
        var options = new OpossumOptions();

        // Assert
        Assert.True(options.FlushEventsImmediately);
    }

    [Fact]
    public void FlushEventsImmediately_CanBeSetToFalse()
    {
        // Arrange
        var options = new OpossumOptions
        {
            // Act
            FlushEventsImmediately = false
        };

        // Assert
        Assert.False(options.FlushEventsImmediately);
    }

    [Fact]
    public void FlushEventsImmediately_CanBeSetToTrue()
    {
        // Arrange
        var options = new OpossumOptions { FlushEventsImmediately = false };

        // Act
        options.FlushEventsImmediately = true;

        // Assert
        Assert.True(options.FlushEventsImmediately);
    }

    [Fact]
    public void FlushEventsImmediately_DefaultValue_IsSafeForProduction()
    {
        // Arrange & Act
        var options = new OpossumOptions();

        // Assert
        // Default should be true to prevent data loss in production
        Assert.True(options.FlushEventsImmediately,
            "FlushEventsImmediately should default to true for production safety");
    }
}
