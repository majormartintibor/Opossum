using Opossum.Core;
using Opossum.Samples.CourseManagement.CourseCreation;
using Opossum.Samples.CourseManagement.CourseStudentLimitModification;
using Opossum.Samples.CourseManagement.StudentRegistration;
using Opossum.Samples.CourseManagement.StudentSubscription;

namespace Opossum.Samples.CourseManagement.CourseEnrollment;

public static class EnrollStudentToCourseCommandExtensions
{ 
    extension(EnrollStudentToCourseCommand command)
    {
        public Query GetCourseEnrollmentQuery() =>
            Query.FromItems(
            new QueryItem
            {
                Tags = [new Tag { Key = "courseId", Value = command.CourseId.ToString() }],
                EventTypes = [
                    nameof(CourseCreatedEvent),
                    nameof(CourseStudentLimitModifiedEvent),
                    nameof(StudentEnrolledToCourseEvent)
                ]
            },
            new QueryItem
            {
                Tags = [new Tag { Key = "studentId", Value = command.StudentId.ToString() }],
                EventTypes = [
                    nameof(StudentRegisteredEvent),
                    nameof(StudentSubscriptionUpdatedEvent),
                    nameof(StudentEnrolledToCourseEvent)
                ]
            });
    }

    extension(EnrollStudentToCourseCommand command)
    {
        public Query GetFailIfMatchQuery() =>
            Query.FromItems(
            new QueryItem
            {
                Tags = [
                    new Tag { Key = "courseId", Value = command.CourseId.ToString() },
                    new Tag { Key = "studentId", Value = command.StudentId.ToString() }
                ],
                EventTypes = [nameof(StudentEnrolledToCourseEvent)]
            });
    }
}
