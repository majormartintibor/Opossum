namespace Opossum.Core;

public class SequencedEvent
{ 
    public required DomainEvent Event { get; set; }
    public long Position { get; set; }
    public Metadata Metadata { get; set; } = new();
}
