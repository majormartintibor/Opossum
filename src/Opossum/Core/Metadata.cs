namespace Opossum.Core;

public class Metadata
{
    public DateTimeOffset Timestamp { get; set; }
    public Guid? CorrelationId { get; set; }
    public Guid? CausationId { get; set; }
    public Guid? OperationId { get; set; }
    public Guid? UserId { get; set; }
}
