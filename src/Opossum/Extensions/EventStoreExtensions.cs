using Opossum.Core;

namespace Opossum.Extensions;

/// <summary>
/// Extension methods for IEventStore to provide convenient overloads and helpers
/// </summary>
public static class EventStoreExtensions
{
    /// <summary>
    /// Appends a single event to the event store
    /// </summary>
    /// <param name="eventStore">The event store</param>
    /// <param name="event">The sequenced event to append</param>
    /// <param name="condition">Optional append condition for optimistic concurrency control</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public static Task AppendAsync(
        this IEventStore eventStore,
        SequencedEvent @event,
        AppendCondition? condition = null)
    {
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(@event);

        return eventStore.AppendAsync([@event], condition);
    }

    /// <summary>
    /// Appends multiple events to the event store without an append condition
    /// </summary>
    /// <param name="eventStore">The event store</param>
    /// <param name="events">The sequenced events to append</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public static Task AppendAsync(
        this IEventStore eventStore,
        SequencedEvent[] events)
    {
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(events);

        return eventStore.AppendAsync(events, condition: null);
    }

    /// <summary>
    /// Reads events from the event store with a single read option
    /// </summary>
    /// <param name="eventStore">The event store</param>
    /// <param name="query">The query to filter events</param>
    /// <param name="readOption">Optional read option (e.g., Descending)</param>
    /// <returns>A task representing the asynchronous operation returning the matching events</returns>
    public static Task<SequencedEvent[]> ReadAsync(
        this IEventStore eventStore,
        Query query,
        ReadOption readOption)
    {
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(query);

        return eventStore.ReadAsync(query, [readOption]);
    }

    /// <summary>
    /// Reads events from the event store without any read options
    /// </summary>
    /// <param name="eventStore">The event store</param>
    /// <param name="query">The query to filter events</param>
    /// <returns>A task representing the asynchronous operation returning the matching events</returns>
    public static Task<SequencedEvent[]> ReadAsync(
        this IEventStore eventStore,
        Query query)
    {
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(query);

        return eventStore.ReadAsync(query, readOptions: null);
    }
}
