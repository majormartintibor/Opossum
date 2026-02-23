namespace Opossum.Core;

/// <summary>
/// A single filter clause within a <see cref="Query"/>.
/// </summary>
/// <remarks>
/// Within one <see cref="QueryItem"/>:
/// <list type="bullet">
///   <item><description><see cref="EventTypes"/> entries are combined with OR — an event matches if it has <em>any</em> of the listed types.</description></item>
///   <item><description><see cref="Tags"/> entries are combined with AND — an event must carry <em>all</em> listed tags.</description></item>
///   <item><description>When both <see cref="EventTypes"/> and <see cref="Tags"/> are present the two groups are AND-ed together.</description></item>
/// </list>
/// Multiple <see cref="QueryItem"/>s in a <see cref="Query"/> are combined with OR.
/// </remarks>
public record QueryItem
{
    /// <summary>
    /// Event types to match. An event matches if it has ANY of these types (OR logic).
    /// </summary>
    public IReadOnlyList<string> EventTypes { get; init; } = [];

    /// <summary>
    /// Tags to match. An event matches if it has ALL of these tags (AND logic).
    /// </summary>
    public IReadOnlyList<Tag> Tags { get; init; } = [];
}
