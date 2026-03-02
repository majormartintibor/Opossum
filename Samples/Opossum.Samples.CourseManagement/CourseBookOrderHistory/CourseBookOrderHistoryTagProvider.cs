using Opossum.Core;
using Opossum.Projections;

namespace Opossum.Samples.CourseManagement.CourseBookOrderHistory;

/// <summary>
/// Defines indexable tags for CourseBookOrderHistory projection.
/// Enables efficient querying by student ID without loading all orders into memory.
/// </summary>
public sealed class CourseBookOrderHistoryTagProvider : IProjectionTagProvider<CourseBookOrderHistoryEntry>
{
    public IEnumerable<Tag> GetTags(CourseBookOrderHistoryEntry state)
    {
        // Index by student ID - allows queries like "all orders for a specific student"
        yield return new Tag("StudentId", state.StudentId.ToString());
    }
}
