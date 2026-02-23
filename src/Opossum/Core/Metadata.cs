namespace Opossum.Core;

/// <summary>Infrastructure metadata attached to an event at append time.</summary>
public record Metadata
{
    /// <summary>UTC timestamp assigned by the store when the event was appended. Set explicitly on <see cref="NewEvent"/> to record when the domain fact occurred.</summary>
    public DateTimeOffset Timestamp { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public Guid? OperationId { get; init; }
    public Guid? UserId { get; init; }
}
