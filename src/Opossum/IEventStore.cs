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
    /// <exception cref="TimeoutException">
    /// Thrown when the cross-process append lock cannot be acquired within
    /// <see cref="Configuration.OpossumOptions.CrossProcessLockTimeout"/>. This can occur
    /// when multiple application instances share the same store directory and one instance
    /// holds the lock for an unusually long time. Increase
    /// <see cref="Configuration.OpossumOptions.CrossProcessLockTimeout"/> if this occurs
    /// regularly, or investigate the process that is holding the lock.
    /// </exception>
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

    /// <summary>
    /// Returns the event with the highest sequence position that matches <paramref name="query"/>,
    /// or <see langword="null"/> when the store contains no matching events.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the efficient read step for DCB patterns that require knowledge of only the most
    /// recent event of a given type — for example, reading the last <c>InvoiceCreatedEvent</c>
    /// to determine the next invoice number in an unbroken monotonic sequence.
    /// </para>
    /// <para>
    /// <b>Typical DCB pattern for consecutive sequences:</b>
    /// <code>
    /// // Step 1 — Read: find the last invoice event
    /// var query = Query.FromEventTypes(nameof(InvoiceCreatedEvent));
    /// var last = await eventStore.ReadLastAsync(query);
    ///
    /// // Step 2 — Decide: derive the next number
    /// var nextNumber = last is null ? 1 : ((InvoiceCreatedEvent)last.Event.Event).InvoiceNumber + 1;
    ///
    /// // Step 3 — Append with a guard that fails if any new invoice appeared in the meantime
    /// var condition = new AppendCondition
    /// {
    ///     FailIfEventsMatch     = query,
    ///     AfterSequencePosition = last?.Position   // null → reject if ANY invoice already exists
    /// };
    /// await eventStore.AppendAsync([new NewEvent { Event = new InvoiceCreatedEvent(nextNumber) }], condition);
    /// // Throws AppendConditionFailedException on conflict — retry the full cycle.
    /// </code>
    /// </para>
    /// <para>
    /// Only one event file is read from storage regardless of how many total events match
    /// the query, making this significantly more efficient than
    /// <c>ReadAsync(..., [ReadOption.Descending])[0]</c> for large event streams.
    /// </para>
    /// </remarks>
    /// <param name="query">Filter — use <see cref="Query.All()"/> to find the globally last event.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// The matching event with the highest sequence position, or <see langword="null"/> when
    /// no events in the store match <paramref name="query"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">When <paramref name="query"/> is <see langword="null"/>.</exception>
    Task<SequencedEvent?> ReadLastAsync(Query query, CancellationToken cancellationToken = default);
}
