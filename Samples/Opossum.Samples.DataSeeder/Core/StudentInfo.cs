using Opossum.Samples.CourseManagement.EnrollmentTier;

namespace Opossum.Samples.DataSeeder.Core;

/// <summary>In-memory representation of a registered student, used by <see cref="SeedContext"/>.</summary>
public record StudentInfo(Guid StudentId, EnrollmentTier Tier, int MaxCourses);
