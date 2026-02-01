using Opossum;
using Opossum.Core;
using Opossum.Projections;
using Opossum.Samples.CourseManagement.CourseEnrollment;
using Opossum.Samples.CourseManagement.EnrollmentTier;
using Opossum.Samples.CourseManagement.StudentRegistration;
using Opossum.Samples.CourseManagement.StudentSubscription;
using Tier = Opossum.Samples.CourseManagement.EnrollmentTier.EnrollmentTier;

namespace Opossum.Samples.CourseManagement.StudentShortInfo;

/// <summary>
/// Projection definition for StudentShortInfo materialized view
/// </summary>
[ProjectionDefinition("StudentShortInfo")]
public sealed class StudentShortInfoProjection : IProjectionDefinition<StudentShortInfo>
{
    public string ProjectionName => "StudentShortInfo";

    public string[] EventTypes => new[]
    {
        nameof(StudentRegisteredEvent),
        nameof(StudentSubscriptionUpdatedEvent),
        nameof(StudentEnrolledToCourseEvent)
    };

    public string KeySelector(SequencedEvent evt)
    {
        var studentIdTag = evt.Event.Tags.FirstOrDefault(t => t.Key == "studentId");
        
        if (studentIdTag == null)
        {
            throw new InvalidOperationException($"Event {evt.Event.EventType} at position {evt.Position} is missing studentId tag");
        }

        return studentIdTag.Value;
    }

    public StudentShortInfo? Apply(StudentShortInfo? current, IEvent evt)
    {
        return evt switch
        {
            StudentRegisteredEvent registered => new StudentShortInfo(
                StudentId: registered.StudentId,
                FirstName: registered.FirstName,
                LastName: registered.LastName,
                Email: registered.Email,
                EnrollmentTier: Tier.Basic,
                CurrentEnrollmentCount: 0,
                MaxEnrollmentCount: StudentMaxCourseEnrollment.GetMaxCoursesAllowed(Tier.Basic)),

            StudentSubscriptionUpdatedEvent updated when current != null =>
                current with
                {
                    EnrollmentTier = updated.EnrollmentTier,
                    MaxEnrollmentCount = StudentMaxCourseEnrollment.GetMaxCoursesAllowed(updated.EnrollmentTier)
                },

            StudentEnrolledToCourseEvent enrolled when current != null =>
                current with { CurrentEnrollmentCount = current.CurrentEnrollmentCount + 1 },

            _ => current
        };
    }
}
