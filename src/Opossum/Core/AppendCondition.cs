namespace Opossum.Core;

public class AppendCondition
{
    public required Query FailIfEventsMatch { get; set; }
    public long? AfterSequencePosition { get; set; }
}
