namespace Opossum.Core;

/// <summary>
/// Envelope that carries a domain event payload (<see cref="Event"/>) together with
/// its indexing metadata (<see cref="EventType"/> and <see cref="Tags"/>).
/// </summary>
/// <remarks>
/// You do not need to set <see cref="EventType"/> manually — when left unassigned it
/// defaults to the simple class name of the inner <see cref="Event"/> object
/// (e.g. <c>"StudentRegisteredEvent"</c>). Override it only when you want to decouple
/// the stored type name from the CLR class name.
/// </remarks>
public class DomainEvent
{
    /// <summary>
    /// The event type name used for indexing and querying.
    /// When not set explicitly, defaults to the simple class name of the inner <see cref="Event"/>
    /// (e.g. <c>"StudentRegisteredEvent"</c>). This makes construction via <c>new DomainEvent { Event = ... }</c>
    /// always produce a valid, consistent type name without any manual assignment.
    /// </summary>
    /// <remarks>
    /// You can still set a custom value (e.g. <c>"StudentRegistered"</c> without the suffix)
    /// but it must be consistent between writes (append) and reads (query), because it is
    /// what gets stored in the EventType index. It is intentionally decoupled from the
    /// <c>$type</c> AQN used for polymorphic deserialization, so namespace renames
    /// do not affect query behaviour.
    /// </remarks>
    public string EventType
    {
        get => field ?? (Event?.GetType().Name ?? string.Empty);
        set;
    }

    public required IEvent Event { get; set; }
    public List<Tag> Tags { get; set; } = [];
}
