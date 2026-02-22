namespace Opossum;

/// <summary>
/// Marker interface for domain event payloads.
/// </summary>
/// <remarks>
/// Implement this interface on every domain event class in your application.
/// The concrete type is preserved during serialization so the original CLR type
/// is restored when the event is read back from the store.
/// <para>
/// Example:
/// <code>
/// public record StudentRegisteredEvent(Guid StudentId, string Name) : IEvent;
/// </code>
/// </para>
/// </remarks>
public interface IEvent
{
}
