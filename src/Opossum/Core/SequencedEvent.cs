namespace Opossum.Core;

/// <summary>
/// An event that has been committed to the event store and assigned a sequence position.
/// </summary>
/// <remarks>
/// This is the read-side counterpart of <see cref="NewEvent"/>.
/// <see cref="Position"/> is the store-assigned, globally unique, monotonically
/// increasing sequence number for this event within its context.
/// It is used as the <see cref="AppendCondition.AfterSequencePosition"/> guard
/// when building a <see cref="Opossum.DecisionModel.DecisionModel{TState}"/>.
/// </remarks>
public class SequencedEvent
{
    /// <summary>The domain event payload and its type metadata.</summary>
    public required DomainEvent Event { get; set; }

    /// <summary>
    /// The 1-based sequence position assigned by the event store.
    /// Positions are contiguous and globally ordered within a context.
    /// </summary>
    public long Position { get; set; }

    /// <summary>Metadata attached to the event (timestamp, correlation IDs, etc.).</summary>
    public Metadata Metadata { get; set; } = new();
}
