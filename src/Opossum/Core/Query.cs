namespace Opossum.Core;

/// <summary>
/// Represents a query for filtering events from the event store.
/// Multiple QueryItems are combined with OR logic.
/// </summary>
/// <example>
/// Example JSON representation of a Query that matches Events that are either:
///  - of type EventType1 OR EventType2
///  - tagged tag1 AND tag2
///  - of type EventType2 OR EventType3 AND tagged tag1 AND tag3
/// <code>
/// {
///   "items": [
///     {
///       "types": ["EventType1", "EventType2"]
///     },
///     {
///       "tags": ["tag1", "tag2"]
///     },
///     {
///       "types": ["EventType2", "EventType3"],
///       "tags": ["tag1", "tag3"]
///     }
///   ]
/// }
/// </code>
/// </example>
public class Query
{
    /// <summary>
    /// List of query items. Events matching ANY of these items will be returned (OR logic).
    /// </summary>
    public List<QueryItem> QueryItems { get; set; } = [];

    /// <summary>
    /// Creates a query that matches all events.
    /// </summary>
    public static Query All() => new();

    /// <summary>
    /// Creates a query from a collection of query items.
    /// </summary>
    public static Query FromItems(params QueryItem[] items) => new()
    { 
        QueryItems = [.. items]
    };

    /// <summary>
    /// Creates a query that matches events of any of the specified types.
    /// </summary>
    public static Query FromEventTypes(params string[] eventTypes) => new()
    {
        QueryItems = [new QueryItem { EventTypes = [.. eventTypes] }]
    };

    /// <summary>
    /// Creates a query that matches events with all of the specified tags.
    /// </summary>
    public static Query FromTags(params Tag[] tags) => new Query
    {
        QueryItems = [new QueryItem { Tags = [.. tags] }]
    };

    /// <summary>
    /// Returns true if the given event matches this query using the same OR/AND logic
    /// applied by the event store. Useful for in-memory filtering and unit testing.
    /// </summary>
    /// <remarks>
    /// <para><c>Query.All()</c> (empty QueryItems) matches every event.</para>
    /// <para>
    /// Within a <see cref="QueryItem"/>: types are OR'd, tags are AND'd, then the two
    /// groups are AND'd together. Multiple QueryItems are OR'd.
    /// </para>
    /// </remarks>
    public bool Matches(SequencedEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentNullException.ThrowIfNull(evt.Event);

        if (QueryItems.Count == 0)
            return true;

        return QueryItems.Any(item =>
        {
            var typeMatch = item.EventTypes.Count == 0 ||
                            item.EventTypes.Contains(evt.Event.EventType);

            var tagMatch = item.Tags.Count == 0 ||
                           item.Tags.All(t => evt.Event.Tags.Any(et =>
                               et.Key == t.Key && et.Value == t.Value));

            return typeMatch && tagMatch;
        });
    }
}
