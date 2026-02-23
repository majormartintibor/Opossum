using Opossum.Core;

namespace Opossum;

public interface IEventStore
{
    /// <summary>
    /// Atomically appends one or more events to the event store,
    /// optionally enforcing a concurrency guard.
    /// </summary>
    /// <param name="events">
    /// One or more events to append. The array must not be empty and every
    /// <see cref="Core.NewEvent.Event"/> must have a non-empty
    /// <see cref="Core.DomainEvent.EventType"/>.
    /// </param>
    /// <param name="condition">
    /// Optional <see cref="Core.AppendCondition"/> that guards the append against
    /// concurrent writes. Pass <see langword="null"/> for an unconditional append.
    /// </param>
    /// <exception cref="Exceptions.AppendConditionFailedException">
    /// Thrown when <paramref name="condition"/> is set and a conflicting event was
    /// appended between the caller's read and this append. Catch this exception and
    /// retry the full read → decide → append cycle.
    /// </exception>
    /// <param name="cancellationToken">
    /// Token to cancel the operation. When cancelled, the semaphore wait is aborted and
    /// no events are written.
    /// </param>
    /// <exception cref="ArgumentNullException">When <paramref name="events"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">When <paramref name="events"/> is empty or contains an event with a null/empty <see cref="Core.DomainEvent.EventType"/>.</exception>
    Task AppendAsync(NewEvent[] events, AppendCondition? condition, CancellationToken cancellationToken = default);

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
