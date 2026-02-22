using Opossum.Projections;

namespace Opossum.UnitTests.Projections;

public class ProjectionDefinitionAttributeTests
{
    [Fact]
    public void Constructor_WithValidName_SetsName()
    {
        // Arrange & Act
        var attribute = new ProjectionDefinitionAttribute("TestProjection");

        // Assert
        Assert.Equal("TestProjection", attribute.Name);
    }

    [Fact]
    public void Constructor_WithNullName_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ProjectionDefinitionAttribute(null!));
    }

    [Fact]
    public void Constructor_WithEmptyName_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new ProjectionDefinitionAttribute(""));
    }

    [Fact]
    public void Constructor_WithWhitespaceName_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new ProjectionDefinitionAttribute("   "));
    }

    [Fact]
    public void Attribute_CanBeAppliedToClass()
    {
        // Arrange
        var type = typeof(TestProjection);

        // Act
        var attributes = type.GetCustomAttributes(typeof(ProjectionDefinitionAttribute), false);

        // Assert
        var attribute = Assert.Single(attributes) as ProjectionDefinitionAttribute;
        Assert.NotNull(attribute);
        Assert.Equal("Test", attribute.Name);
    }

    [ProjectionDefinition("Test")]
    private class TestProjection : IProjectionDefinition<TestState>
    {
        public string ProjectionName => "Test";
        public string[] EventTypes => ["TestEvent"];
        public string KeySelector(Opossum.Core.SequencedEvent evt) => "key";
        public TestState? Apply(TestState? current, IEvent evt) => current;
    }

    private record TestState;
}
