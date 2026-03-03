using Opossum.Projections;

namespace Opossum.UnitTests.Projections;

public class ProjectionDefinitionAttributeTests
{
    [Fact]
    public void Constructor_WithValidName_SetsName()
    {
        // Act
        var attribute = new ProjectionDefinitionAttribute("OrderSummary");

        // Assert
        Assert.Equal("OrderSummary", attribute.Name);
    }

    [Fact]
    public void Constructor_WithNullName_ThrowsArgumentException()
    {
        // Act and Assert - ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for null
        Assert.Throws<ArgumentNullException>(() => new ProjectionDefinitionAttribute(null!));
    }

    [Fact]
    public void Constructor_WithEmptyName_ThrowsArgumentException()
    {
        // Act and Assert
        Assert.Throws<ArgumentException>(() => new ProjectionDefinitionAttribute(string.Empty));
    }

    [Fact]
    public void Constructor_WithWhitespaceName_ThrowsArgumentException()
    {
        // Act and Assert
        Assert.Throws<ArgumentException>(() => new ProjectionDefinitionAttribute("   "));
    }

    [Fact]
    public void Attribute_CanBeAppliedToClass()
    {
        // Arrange and Act
        var type = typeof(AnnotatedProjection);
        var attributes = type.GetCustomAttributes(typeof(ProjectionDefinitionAttribute), inherit: false);

        // Assert
        Assert.Single(attributes);
        var projAttr = (ProjectionDefinitionAttribute)attributes[0];
        Assert.Equal("AnnotatedProjection", projAttr.Name);
    }

    [ProjectionDefinition("AnnotatedProjection")]
    private sealed class AnnotatedProjection;
}

