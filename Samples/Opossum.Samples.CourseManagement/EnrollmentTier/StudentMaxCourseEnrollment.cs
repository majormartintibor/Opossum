namespace Opossum.Samples.CourseManagement.EnrollmentTier;

public static class StudentMaxCourseEnrollment
{
    public static int GetMaxCoursesAllowed(EnrollmentTier enrollmentTier) => enrollmentTier switch
    {
        EnrollmentTier.Basic => 2,
        EnrollmentTier.Standard => 5,
        EnrollmentTier.Professional => 10,
        EnrollmentTier.Master => 25,
        _ => throw new ArgumentOutOfRangeException(nameof(enrollmentTier), $"Not expected enrollment tier value: {enrollmentTier}"),
    };
}
