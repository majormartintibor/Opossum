using Opossum.Core;

namespace Opossum.Extensions;

/// <summary>
/// Extension methods for IEvent to provide fluent API for building domain events
/// </summary>
public static class EventExtensions
{
    /// <summary>
    /// Converts an IEvent into a DomainEventBuilder for fluent configuration
    /// </summary>
    /// <param name="event">The event to convert</param>
    /// <returns>A DomainEventBuilder for fluent configuration</returns>
    public static DomainEventBuilder ToDomainEvent(this IEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        return new DomainEventBuilder(@event);
    }
}

/// <summary>
/// Fluent builder for constructing SequencedEvent instances from domain events
/// </summary>
public class DomainEventBuilder
{
    private readonly IEvent _event;
    private readonly List<Tag> _tags = [];
    private Metadata? _metadata;

    internal DomainEventBuilder(IEvent @event)
    {
        _event = @event ?? throw new ArgumentNullException(nameof(@event));
    }

    /// <summary>
    /// Adds a single tag to the event
    /// </summary>
    /// <param name="key">Tag key</param>
    /// <param name="value">Tag value</param>
    /// <returns>This builder for fluent chaining</returns>
    public DomainEventBuilder WithTag(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        _tags.Add(new Tag(key, value));
        return this;
    }

    /// <summary>
    /// Adds multiple tags to the event
    /// </summary>
    /// <param name="tags">Tags to add</param>
    /// <returns>This builder for fluent chaining</returns>
    public DomainEventBuilder WithTags(params Tag[] tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        _tags.AddRange(tags);
        return this;
    }

    /// <summary>
    /// Sets the metadata for the event
    /// </summary>
    /// <param name="metadata">Metadata to attach</param>
    /// <returns>This builder for fluent chaining</returns>
    public DomainEventBuilder WithMetadata(Metadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        _metadata = metadata;
        return this;
    }

    /// <summary>
    /// Sets the correlation ID in the metadata
    /// </summary>
    /// <param name="correlationId">Correlation ID for tracking related operations</param>
    /// <returns>This builder for fluent chaining</returns>
    public DomainEventBuilder WithCorrelationId(Guid correlationId)
    {
        EnsureMetadata();
        _metadata = _metadata! with { CorrelationId = correlationId };
        return this;
    }

    /// <summary>
    /// Sets the causation ID in the metadata
    /// </summary>
    /// <param name="causationId">Causation ID for tracking what caused this event</param>
    /// <returns>This builder for fluent chaining</returns>
    public DomainEventBuilder WithCausationId(Guid causationId)
    {
        EnsureMetadata();
        _metadata = _metadata! with { CausationId = causationId };
        return this;
    }

    /// <summary>
    /// Sets the operation ID in the metadata
    /// </summary>
    /// <param name="operationId">Operation ID for tracking the operation</param>
    /// <returns>This builder for fluent chaining</returns>
    public DomainEventBuilder WithOperationId(Guid operationId)
    {
        EnsureMetadata();
        _metadata = _metadata! with { OperationId = operationId };
        return this;
    }

    /// <summary>
    /// Sets the user ID in the metadata
    /// </summary>
    /// <param name="userId">User ID who triggered the event</param>
    /// <returns>This builder for fluent chaining</returns>
    public DomainEventBuilder WithUserId(Guid userId)
    {
        EnsureMetadata();
        _metadata = _metadata! with { UserId = userId };
        return this;
    }

    /// <summary>
    /// Sets a custom timestamp in the metadata (defaults to UtcNow if not set)
    /// </summary>
    /// <param name="timestamp">Custom timestamp for the event</param>
    /// <returns>This builder for fluent chaining</returns>
    public DomainEventBuilder WithTimestamp(DateTimeOffset timestamp)
    {
        EnsureMetadata();
        _metadata = _metadata! with { Timestamp = timestamp };
        return this;
    }

    /// <summary>
    /// Builds the final <see cref="NewEvent"/> ready to be passed to
    /// <see cref="IEventStore.AppendAsync"/>.
    /// </summary>
    /// <returns>A <see cref="NewEvent"/> ready to be appended to the event store</returns>
    public NewEvent Build()
    {
        return new NewEvent
        {
            Event = new DomainEvent
            {
                EventType = _event.GetType().Name,
                Event = _event,
                Tags = _tags
            },
            Metadata = _metadata ?? new Metadata
            {
                Timestamp = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid()
            }
        };
    }

    /// <summary>
    /// Implicit conversion to <see cref="NewEvent"/> for convenience
    /// </summary>
    public static implicit operator NewEvent(DomainEventBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Build();
    }

    private void EnsureMetadata()
    {
        _metadata ??= new Metadata { Timestamp = DateTimeOffset.UtcNow };
    }
}

/// <summary>
/// Extension methods for IEventStore to provide convenient overloads and helpers
/// </summary>
public static class EventStoreExtensions
{
    /// <summary>
    /// Appends a single event to the event store
    /// </summary>
    /// <param name="eventStore">The event store</param>
    /// <param name="event">The new event to append</param>
    /// <param name="condition">Optional append condition for optimistic concurrency control</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public static Task AppendAsync(
        this IEventStore eventStore,
        NewEvent @event,
        AppendCondition? condition = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(@event);

        return eventStore.AppendAsync([@event], condition, cancellationToken);
    }

    /// <summary>
    /// Appends multiple events to the event store without an append condition
    /// </summary>
    /// <param name="eventStore">The event store</param>
    /// <param name="events">The new events to append</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public static Task AppendAsync(
        this IEventStore eventStore,
        NewEvent[] events,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(events);

        return eventStore.AppendAsync(events, condition: null, cancellationToken);
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

    /// <summary>
    /// Reads events from the event store that were appended after <paramref name="fromPosition"/>,
    /// without any read options. Useful for incrementally polling new events from a known checkpoint.
    /// </summary>
    /// <param name="eventStore">The event store</param>
    /// <param name="query">The query to filter events</param>
    /// <param name="fromPosition">
    /// Only events with <c>Position &gt; fromPosition</c> are returned.
    /// Pass the last processed sequence position to receive only new events.
    /// </param>
    /// <returns>A task representing the asynchronous operation returning the matching events</returns>
    public static Task<SequencedEvent[]> ReadAsync(
        this IEventStore eventStore,
        Query query,
        long fromPosition)
    {
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(query);

        return eventStore.ReadAsync(query, readOptions: null, fromPosition: fromPosition);
    }

    /// <summary>
    /// Appends a single domain event to the event store with simplified syntax
    /// </summary>
    /// <param name="eventStore">The event store</param>
    /// <param name="event">The domain event to append</param>
    /// <param name="tags">Optional tags to attach to the event</param>
    /// <param name="metadata">Optional metadata (timestamp and correlation ID auto-generated if not provided)</param>
    /// <param name="condition">Optional append condition for optimistic concurrency control</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public static Task AppendEventAsync(
        this IEventStore eventStore,
        IEvent @event,
        IEnumerable<Tag>? tags = null,
        Metadata? metadata = null,
        AppendCondition? condition = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(@event);

        var newEvent = new NewEvent
        {
            Event = new DomainEvent
            {
                EventType = @event.GetType().Name,
                Event = @event,
                Tags = tags?.ToList() ?? []
            },
            Metadata = metadata ?? new Metadata
            {
                Timestamp = DateTimeOffset.UtcNow,
                CorrelationId = Guid.NewGuid()
            }
        };

        return eventStore.AppendAsync([newEvent], condition, cancellationToken);
    }

    /// <summary>
    /// Appends multiple domain events to the event store with simplified syntax
    /// </summary>
    /// <param name="eventStore">The event store</param>
    /// <param name="events">The domain events to append</param>
    /// <param name="tags">Optional tags to attach to all events</param>
    /// <param name="metadata">Optional metadata (timestamp and correlation ID auto-generated if not provided)</param>
    /// <param name="condition">Optional append condition for optimistic concurrency control</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public static Task AppendEventsAsync(
        this IEventStore eventStore,
        IEvent[] events,
        IEnumerable<Tag>? tags = null,
        Metadata? metadata = null,
        AppendCondition? condition = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(events);

        var tagList = tags?.ToList() ?? [];
        var sharedMetadata = metadata ?? new Metadata
        {
            Timestamp = DateTimeOffset.UtcNow,
            CorrelationId = Guid.NewGuid()
        };

        var newEvents = events.Select(e => new NewEvent
        {
            Event = new DomainEvent
            {
                EventType = e.GetType().Name,
                Event = e,
                Tags = tagList
            },
            Metadata = sharedMetadata
        }).ToArray();

        return eventStore.AppendAsync(newEvents, condition, cancellationToken);
    }

    /// <summary>
    /// Builds projection objects by grouping events by a shared key and folding them sequentially.
    /// Each unique key value produces one projection instance — equivalent to a <c>GroupBy</c> followed by a left-fold.
    /// </summary>
    /// <typeparam name="TProjection">The projection type to build</typeparam>
    /// <param name="events">Events to process</param>
    /// <param name="keySelector">
    /// Function that extracts the grouping key from each event.
    /// All events returning the same key are folded into a single projection instance.
    /// In DCB terms this is typically the value of a domain identity tag (e.g. the value of a <c>studentId</c> tag).
    /// </param>
    /// <param name="applyEvent">Function to apply an event to the current projection state (<see langword="null"/> on the first event for each key)</param>
    /// <returns>Enumerable of built projections, one per unique key value</returns>
    /// <example>
    /// <code>
    /// var students = events.BuildProjections&lt;StudentShortInfo&gt;(
    ///     keySelector: e => e.Event.Tags.First(t => t.Key == "studentId").Value,
    ///     applyEvent: (evt, current) => evt switch
    ///     {
    ///         StudentRegisteredEvent registered => new StudentShortInfo(...),
    ///         StudentUpdatedEvent updated when current != null => current with { ... },
    ///         _ => current
    ///     }
    /// );
    /// </code>
    /// </example>
    public static IEnumerable<TProjection> BuildProjections<TProjection>(
        this SequencedEvent[] events,
        Func<SequencedEvent, string> keySelector,
        Func<IEvent, TProjection?, TProjection?> applyEvent)
        where TProjection : class
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(applyEvent);

        return events
            .GroupBy(keySelector)
            .Select(eventGroup => eventGroup.Aggregate(
                seed: (TProjection?)null,
                func: (current, seqEvent) => applyEvent(seqEvent.Event.Event, current)
            ))
            .Where(projection => projection != null)
            .Cast<TProjection>();
    }
}
