namespace Opossum.Core;

/// <summary>
/// A key-value label attached to an event that enables tag-based querying.
/// </summary>
/// <remarks>
/// Tags create a secondary index alongside the event-type index.
/// Use them to scope queries to a specific entity identity, for example:
/// <code>
/// new Tag { Key = "courseId", Value = courseId.ToString() }
/// </code>
/// Within a <see cref="QueryItem"/> multiple tags are combined with AND logic —
/// an event must carry <em>all</em> specified tags to match.
/// </remarks>
public class Tag
{
    /// <summary>The tag key (e.g. <c>"courseId"</c>, <c>"studentId"</c>).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The tag value (e.g. a GUID or domain identifier as string).</summary>
    public string Value { get; set; } = string.Empty;
}
