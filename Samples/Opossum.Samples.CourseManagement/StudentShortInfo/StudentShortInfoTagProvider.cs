using Opossum.Core;
using Opossum.Projections;

namespace Opossum.Samples.CourseManagement.StudentShortInfo;

/// <summary>
/// Defines indexable tags for StudentShortInfo projection.
/// Enables efficient querying by enrollment tier and maxed-out status without loading all students into memory.
/// </summary>
public sealed class StudentShortInfoTagProvider : IProjectionTagProvider<StudentShortInfo>
{
    public IEnumerable<Tag> GetTags(StudentShortInfo state)
    {
        // Index by enrollment tier - allows queries like "all Premium students"
        yield return new Tag
        {
            Key = "EnrollmentTier",
            Value = state.EnrollmentTier.ToString()
        };

        // Index by maxed-out status - allows queries like "all students who can't enroll in more courses"
        yield return new Tag
        {
            Key = "IsMaxedOut",
            Value = state.IsMaxedOut.ToString()
        };
    }
}
