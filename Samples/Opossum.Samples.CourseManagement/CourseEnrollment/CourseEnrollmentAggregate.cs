using Opossum.Samples.CourseManagement.CourseCreation;
using Opossum.Samples.CourseManagement.CourseStudentLimitModification;
using Opossum.Samples.CourseManagement.EnrollmentTier;
using Opossum.Samples.CourseManagement.StudentRegistration;
using Opossum.Samples.CourseManagement.StudentSubscription;
using Tier = Opossum.Samples.CourseManagement.EnrollmentTier.EnrollmentTier;

namespace Opossum.Samples.CourseManagement.CourseEnrollment;

public sealed record CourseEnrollmentAggregate
{
    // Identity (from command - immutable)
    public Guid CourseId { get; private init; }
    public Guid StudentId { get; private init; }

    // Course capacity tracking
    public int CourseMaxCapacity { get; private init; }
    public int CourseCurrentEnrollmentCount { get; private init; }

    // Student enrollment tracking
    public Tier StudentEnrollmentTier { get; private init; }
    public int StudentCurrentCourseEnrollmentCount { get; private init; }

    // Computed properties
    public int StudentMaxCourseEnrollmentLimit => StudentMaxCourseEnrollment.GetMaxCoursesAllowed(StudentEnrollmentTier);
    public bool IsStudentAlreadyEnrolledInThisCourse { get; private init; }

    private CourseEnrollmentAggregate() { }

    public CourseEnrollmentAggregate(Guid courseId, Guid studentId) 
    { 
        CourseId = courseId;
        StudentId = studentId;
        StudentEnrollmentTier = Tier.Basic; // Default tier
    }

    public CourseEnrollmentAggregate Apply(object @event) => @event switch
    {
        // Course events - track course capacity
        CourseCreatedEvent created when created.CourseId == CourseId =>
            this with { CourseMaxCapacity = created.MaxStudentCount },

        CourseStudentLimitModifiedEvent limitModified when limitModified.CourseId == CourseId =>
            this with { CourseMaxCapacity = limitModified.NewMaxStudentCount },

        // Student events - track student tier
        StudentRegisteredEvent registered when registered.StudentId == StudentId =>
            this with { StudentEnrollmentTier = Tier.Basic },

        StudentSubscriptionUpdatedEvent subscriptionUpdated when subscriptionUpdated.StudentId == StudentId =>
            this with { StudentEnrollmentTier = subscriptionUpdated.EnrollmentTier },

        // Enrollment events - track enrollment counts
        StudentEnrolledToCourseEvent enrolled when enrolled.CourseId == CourseId && enrolled.StudentId == StudentId =>
            this with { IsStudentAlreadyEnrolledInThisCourse = true },

        StudentEnrolledToCourseEvent enrolled when enrolled.CourseId == CourseId =>
            this with { CourseCurrentEnrollmentCount = CourseCurrentEnrollmentCount + 1 },

        StudentEnrolledToCourseEvent enrolled when enrolled.StudentId == StudentId =>
            this with { StudentCurrentCourseEnrollmentCount = StudentCurrentCourseEnrollmentCount + 1 },

        _ => this
    };
}
