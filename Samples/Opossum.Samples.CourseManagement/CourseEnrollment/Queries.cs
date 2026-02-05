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
        /// <summary>
        /// Builds a query to retrieve all events relevant for enrollment validation.
        /// 
        /// Query Logic (Boolean Algebra):
        /// ================================
        /// Returns events that match: (QueryItem1 OR QueryItem2)
        /// 
        /// QueryItem1 (Course-related events):
        ///   - Must have: courseId tag (specific course)
        ///   - AND must be one of: CourseCreatedEvent OR CourseStudentLimitModifiedEvent OR StudentEnrolledToCourseEvent
        /// 
        /// QueryItem2 (Student-related events):
        ///   - Must have: studentId tag (specific student)
        ///   - AND must be one of: StudentRegisteredEvent OR StudentSubscriptionUpdatedEvent OR StudentEnrolledToCourseEvent
        /// 
        /// Example matches:
        ///   ✅ CourseCreatedEvent with courseId=X (matches QueryItem1)
        ///   ✅ StudentRegisteredEvent with studentId=Y (matches QueryItem2)
        ///   ✅ StudentEnrolledToCourseEvent with courseId=X AND studentId=Y (matches BOTH items)
        ///   ❌ CourseCreatedEvent with courseId=Z (different course)
        ///   ❌ PaymentReceivedEvent (wrong event type)
        /// 
        /// Why this structure?
        /// - We need ALL course events (capacity, limit changes, enrollments)
        /// - We need ALL student events (registration, tier changes, enrollments)
        /// - StudentEnrolledToCourseEvent appears in both because it affects both aggregates
        /// - This ensures we build complete state for both Course and Student aggregates
        /// </summary>
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
        /// <summary>
        /// Builds a query for the FailIfEventsMatch append condition to prevent duplicate enrollments.
        /// 
        /// Query Logic (Boolean Algebra):
        /// ================================
        /// Returns events that match: (QueryItem1) - single item, no OR
        /// 
        /// QueryItem1 (Exact duplicate enrollment):
        ///   - Must have: courseId tag (specific course)
        ///   - AND must have: studentId tag (specific student)
        ///   - AND must be: StudentEnrolledToCourseEvent
        /// 
        /// Example matches:
        ///   ✅ StudentEnrolledToCourseEvent with courseId=X AND studentId=Y (exact duplicate!)
        ///   ❌ StudentEnrolledToCourseEvent with courseId=X AND studentId=Z (different student)
        ///   ❌ StudentEnrolledToCourseEvent with courseId=Z AND studentId=Y (different course)
        ///   ❌ CourseCreatedEvent with courseId=X (wrong event type)
        /// 
        /// Why this structure?
        /// - Single QueryItem = no OR logic, very specific check
        /// - Both tags required = AND logic ensures EXACT match on course + student combo
        /// - Used in AppendCondition.FailIfEventsMatch to fail fast on duplicate enrollments
        /// - Complements AfterSequencePosition by providing explicit duplicate detection
        /// </summary>
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
