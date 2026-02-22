namespace Opossum.Core;

/// <summary>
/// Represents a single item in a Query that filters events by EventType and/or Tags.
/// Multiple types are combined with OR logic.
/// Multiple tags are combined with AND logic.
/// </summary>
public class QueryItem
{
    /// <summary>
    /// Event types to match. An event matches if it has ANY of these types (OR logic).
    /// </summary>
    public List<string> EventTypes { get; set; } = [];

    /// <summary>
    /// Tags to match. An event matches if it has ALL of these tags (AND logic).
    /// </summary>
    public List<Tag> Tags { get; set; } = [];
}
