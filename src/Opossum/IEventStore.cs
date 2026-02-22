using Opossum.Core;

namespace Opossum;

public interface IEventStore
{
    Task AppendAsync(NewEvent[] events, AppendCondition? condition);

    /// <summary>
    /// Reads events matching <paramref name="query"/> from the event store.
    /// </summary>
    /// <param name="query">Filter — use <see cref="Query.All()"/> to read every event.</param>
    /// <param name="readOptions">Optional read options (e.g. <see cref="ReadOption.Descending"/>).</param>
    /// <param name="fromPosition">
    /// When provided, only events with <c>Position &gt; fromPosition</c> are returned.
    /// Pass the highest position already processed to resume reading from a known checkpoint.
    /// When <see langword="null"/> (the default) all matching events are returned.
    /// </param>
    Task<SequencedEvent[]> ReadAsync(Query query, ReadOption[]? readOptions, long? fromPosition = null);
}
