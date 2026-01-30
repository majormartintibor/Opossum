using Opossum.Core;

namespace Opossum.UnitTests.Core;

/// <summary>
/// Unit tests for ReadOption enum
/// </summary>
public class ReadOptionTests
{
    #region Basic Enum Tests

    [Fact]
    public void ReadOption_None_HasValueZero()
    {
        // Act
        var value = (int)ReadOption.None;

        // Assert
        Assert.Equal(0, value);
    }

    [Fact]
    public void ReadOption_Descending_HasValueOne()
    {
        // Act
        var value = (int)ReadOption.Descending;

        // Assert
        Assert.Equal(1, value);
    }

    [Fact]
    public void ReadOption_HasFlagsAttribute()
    {
        // Act
        var hasFlagsAttribute = typeof(ReadOption)
            .GetCustomAttributes(typeof(FlagsAttribute), false)
            .Any();

        // Assert
        Assert.True(hasFlagsAttribute, "ReadOption enum should have [Flags] attribute");
    }

    #endregion

    #region Flags Behavior Tests

    [Fact]
    public void ReadOption_None_IsDefaultValue()
    {
        // Arrange
        ReadOption defaultValue = default;

        // Assert
        Assert.Equal(ReadOption.None, defaultValue);
    }

    [Fact]
    public void ReadOption_CanCheckForNone()
    {
        // Arrange
        var option = ReadOption.None;

        // Act & Assert
        Assert.True(option == ReadOption.None);
        Assert.False(option.HasFlag(ReadOption.Descending));
    }

    [Fact]
    public void ReadOption_CanCheckForDescending()
    {
        // Arrange
        var option = ReadOption.Descending;

        // Act & Assert
        Assert.True(option.HasFlag(ReadOption.Descending));
        Assert.False(option == ReadOption.None);
    }

    [Fact]
    public void ReadOption_HasFlag_WorksWithNone()
    {
        // Arrange
        var option = ReadOption.None;

        // Act
        var hasNone = option.HasFlag(ReadOption.None);

        // Assert
        Assert.True(hasNone);
    }

    #endregion

    #region Usage Scenarios

    [Fact]
    public void ReadOption_DefaultParameter_IsNone()
    {
        // This tests the common pattern of defaulting to None
        void TestMethod(ReadOption option = ReadOption.None)
        {
            Assert.Equal(ReadOption.None, option);
        }

        // Act & Assert
        TestMethod();
    }

    [Fact]
    public void ReadOption_CanBePassedAsParameter()
    {
        // Arrange
        void TestMethod(ReadOption option)
        {
            Assert.Equal(ReadOption.Descending, option);
        }

        // Act & Assert
        TestMethod(ReadOption.Descending);
    }

    [Fact]
    public void ReadOption_CanBeUsedInIfStatement()
    {
        // Arrange
        var option = ReadOption.Descending;
        var wasDescending = false;

        // Act
        if (option.HasFlag(ReadOption.Descending))
        {
            wasDescending = true;
        }

        // Assert
        Assert.True(wasDescending);
    }

    [Fact]
    public void ReadOption_CanBeUsedInSwitchStatement()
    {
        // Arrange
        var option = ReadOption.Descending;
        var result = "";

        // Act
        switch (option)
        {
            case ReadOption.None:
                result = "ascending";
                break;
            case ReadOption.Descending:
                result = "descending";
                break;
        }

        // Assert
        Assert.Equal("descending", result);
    }

    #endregion

    #region Future Extensibility Tests

    [Fact]
    public void ReadOption_SupportsValueComparison()
    {
        // Arrange
        var option1 = ReadOption.None;
        var option2 = ReadOption.None;
        var option3 = ReadOption.Descending;

        // Act & Assert
        Assert.Equal(option1, option2);
        Assert.NotEqual(option1, option3);
    }

    [Fact]
    public void ReadOption_CanBeConvertedToInt()
    {
        // Act
        int noneValue = (int)ReadOption.None;
        int descendingValue = (int)ReadOption.Descending;

        // Assert
        Assert.Equal(0, noneValue);
        Assert.Equal(1, descendingValue);
    }

    [Fact]
    public void ReadOption_CanBeConvertedFromInt()
    {
        // Act
        var none = (ReadOption)0;
        var descending = (ReadOption)1;

        // Assert
        Assert.Equal(ReadOption.None, none);
        Assert.Equal(ReadOption.Descending, descending);
    }

    #endregion

    #region Integration Scenarios

    [Theory]
    [InlineData(ReadOption.None, false)]
    [InlineData(ReadOption.Descending, true)]
    public void ReadOption_HasFlag_WorksCorrectly(ReadOption option, bool expectedDescending)
    {
        // Act
        var hasDescending = option.HasFlag(ReadOption.Descending);

        // Assert
        Assert.Equal(expectedDescending, hasDescending);
    }

    [Fact]
    public void ReadOption_ToString_ReturnsName()
    {
        // Act
        var noneString = ReadOption.None.ToString();
        var descendingString = ReadOption.Descending.ToString();

        // Assert
        Assert.Equal("None", noneString);
        Assert.Equal("Descending", descendingString);
    }

    [Fact]
    public void ReadOption_CanParseFromString()
    {
        // Act
        var none = Enum.Parse<ReadOption>("None");
        var descending = Enum.Parse<ReadOption>("Descending");

        // Assert
        Assert.Equal(ReadOption.None, none);
        Assert.Equal(ReadOption.Descending, descending);
    }

    #endregion
}
