using Opossum.Mediator;

namespace Opossum.UnitTests.Mediator;

public class MessageHandlerAttributeTests
{
    [Fact]
    public void Attribute_CanBeAppliedToClass()
    {
        // Arrange & Act
        var attribute = typeof(AttributeTestHandler)
            .GetCustomAttributes(typeof(MessageHandlerAttribute), false)
            .FirstOrDefault();
        
        // Assert
        Assert.NotNull(attribute);
        Assert.IsType<MessageHandlerAttribute>(attribute);
    }
    
    [Fact]
    public void Attribute_IsNotInherited()
    {
        // Arrange
        var baseAttribute = typeof(AttributeTestHandler)
            .GetCustomAttributes(typeof(MessageHandlerAttribute), false)
            .FirstOrDefault();

        var derivedAttribute = typeof(DerivedFromAttributeTest)
            .GetCustomAttributes(typeof(MessageHandlerAttribute), false)
            .FirstOrDefault();

        // Assert
        Assert.NotNull(baseAttribute);
        Assert.Null(derivedAttribute);
    }
    
    [Fact]
    public void Attribute_AllowsOnlyOneInstance()
    {
        // Arrange
        var attributes = typeof(AttributeTestHandler)
            .GetCustomAttributes(typeof(MessageHandlerAttribute), false);
        
        // Assert
        Assert.Single(attributes);
    }
    
    [Fact]
    public void Attribute_CanOnlyBeAppliedToClasses()
    {
        // This is enforced by the AttributeUsage on MessageHandlerAttribute
        // which specifies AttributeTargets.Class

        // Arrange & Act
        var attributeUsage = (AttributeUsageAttribute?)Attribute.GetCustomAttribute(
            typeof(MessageHandlerAttribute), 
            typeof(AttributeUsageAttribute));

        // Assert
        Assert.NotNull(attributeUsage);
        Assert.Equal(AttributeTargets.Class, attributeUsage.ValidOn);
        Assert.False(attributeUsage.AllowMultiple);
        Assert.False(attributeUsage.Inherited);
    }
}

[MessageHandler]
public class AttributeTestHandler
{
    public AttributeTestResponse Handle(AttributeTestMessage message)
    {
        return new AttributeTestResponse();
    }
}

// DerivedFromAttributeTest is only used to test attribute inheritance, not as an actual handler
// It doesn't have the [MessageHandler] attribute and doesn't end with "Handler", so it won't be discovered
public class DerivedFromAttributeTest : AttributeTestHandler
{
    // Inherits Handle method but doesn't have [MessageHandler] attribute
    // and doesn't end with "Handler" suffix, so won't be discovered
}

public record AttributeTestMessage();
public record AttributeTestResponse();
