using Opossum.Core;
using Opossum.Projections;

namespace Opossum.Samples.CourseManagement.CourseShortInfo;

/// <summary>
/// Defines indexable tags for CourseShortInfo projection.
/// Enables efficient querying by full/not-full status without loading all courses into memory.
/// </summary>
public sealed class CourseShortInfoTagProvider : IProjectionTagProvider<CourseShortInfo>
{
    public IEnumerable<Tag> GetTags(CourseShortInfo state)
    {
        // Index by full status - allows queries like "all courses with available spots"
        yield return new Tag("IsFull", state.IsFull.ToString());
    }
}
