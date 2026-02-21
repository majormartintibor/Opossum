namespace Opossum.Core;

/// <summary>
/// Represents an event to be appended to the event store.
/// The sequence position is unknown at this stage and will be assigned by the event store.
/// </summary>
/// <remarks>
/// This is the write-side counterpart of <see cref="SequencedEvent"/>.
/// The DCB specification distinguishes between <c>Event</c> (write input) and
/// <c>SequencedEvent</c> (read output). <c>NewEvent</c> maps to the spec's <c>Event</c>.
/// </remarks>
public class NewEvent
{
    /// <summary>
    /// The domain event payload including its type and tags.
    /// </summary>
    public required DomainEvent Event { get; set; }

    /// <summary>
    /// Optional metadata such as timestamp, correlation ID, and causation ID.
    /// </summary>
    public Metadata Metadata { get; set; } = new();
}
