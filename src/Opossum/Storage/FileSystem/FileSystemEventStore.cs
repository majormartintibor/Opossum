using Opossum.Core;

namespace Opossum.Storage.FileSystem;

internal class FileSystemEventStore : IEventStore
{
    public Task AppendAsync(SequencedEvent[] events, AppendCondition? condition)
    {
        throw new NotImplementedException();
    }    

    public Task<SequencedEvent[]> ReadAsync(Query query, ReadOption[]? readOptions)
    {
        throw new NotImplementedException();
    }
}
