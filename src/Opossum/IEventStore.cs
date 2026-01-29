using Opossum.Core;

namespace Opossum;

public interface IEventStore
{
    Task AppendAsync(SequencedEvent[] events, AppendCondition? condition);    

    Task<SequencedEvent[]> ReadAsync(Query query, ReadOption[]? readOptions); 
}
