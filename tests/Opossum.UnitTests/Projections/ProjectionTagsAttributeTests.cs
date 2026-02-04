using Opossum.Core;
using Opossum.Projections;

namespace Opossum.UnitTests.Projections;

public class ProjectionTagsAttributeTests
{
    [Fact]
    public void Constructor_WithValidTagProvider_Succeeds()
    {
        // Arrange & Act
        var attribute = new ProjectionTagsAttribute(typeof(TestTagProvider));

        // Assert
        Assert.Equal(typeof(TestTagProvider), attribute.TagProviderType);
    }

    [Fact]
    public void Constructor_WithNullType_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ProjectionTagsAttribute(null!));
    }

    [Fact]
    public void Constructor_WithNonTagProviderType_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            new ProjectionTagsAttribute(typeof(string)));
        
        Assert.Contains("must implement IProjectionTagProvider<TState>", exception.Message);
    }

    [Fact]
    public void Constructor_WithTagProviderInterface_ThrowsArgumentException()
    {
        // Act & Assert - Can't use the interface itself, must be implementation
        var exception = Assert.Throws<ArgumentException>(() => 
            new ProjectionTagsAttribute(typeof(IProjectionTagProvider<TestState>)));
        
        Assert.Contains("must implement IProjectionTagProvider<TState>", exception.Message);
    }

    // Test helper classes
    private class TestState
    {
        public string Id { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    private class TestTagProvider : IProjectionTagProvider<TestState>
    {
        public IEnumerable<Tag> GetTags(TestState state)
        {
            yield return new Tag { Key = "Status", Value = state.Status };
        }
    }
}
