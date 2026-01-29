namespace Opossum.Core;

public class DomainEvent
{
    public string EventType { get; set; } = string.Empty;
    public required IEvent Event { get; set; }
    public List<Tag> Tags { get; set; } = [];
}
