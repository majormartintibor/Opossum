namespace Opossum.Mediator;

/// <summary>
/// Marks a class as a message handler for explicit discovery
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MessageHandlerAttribute : Attribute
{
}
