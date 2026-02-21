using Opossum.Core;
using Opossum.Projections;
using Opossum.Samples.CourseManagement.EnrollmentTier;
using Opossum.Samples.CourseManagement.Events;
using Tier = Opossum.Samples.CourseManagement.EnrollmentTier.EnrollmentTier;

namespace Opossum.Samples.CourseManagement.StudentDetails;

/// <summary>
/// Lightweight course info for enrolled courses list
/// </summary>
public sealed record EnrolledCourseInfo(
    Guid CourseId,
    string Name);

/// <summary>
/// Student details with enrolled courses
/// </summary>
public sealed record StudentDetails(
    Guid StudentId,
    string FirstName,
    string LastName,
    string Email,
    Tier EnrollmentTier,
    int MaxEnrollmentCount,
    int CurrentEnrollmentCount,
    List<EnrolledCourseInfo> EnrolledCourses)
{
    public bool IsMaxedOut => CurrentEnrollmentCount >= MaxEnrollmentCount;
}

[ProjectionDefinition("StudentDetails")]
public sealed class StudentDetailsProjection : IProjectionWithRelatedEvents<StudentDetails>
{
    public string ProjectionName => "StudentDetails";

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

    public Query? GetRelatedEventsQuery(IEvent evt)
    {
        // When a student enrolls in a course, we need to fetch the course creation details
        if (evt is StudentEnrolledToCourseEvent enrolled)
        {
            return Query.FromItems(new QueryItem
            {
                Tags = [new Tag { Key = "courseId", Value = enrolled.CourseId.ToString() }],
                EventTypes = [nameof(CourseCreatedEvent)]
            });
        }

        return null; // No related events needed for other event types
    }

    public StudentDetails? Apply(StudentDetails? current, IEvent evt, SequencedEvent[] relatedEvents)
    {
        return evt switch
        {
            StudentRegisteredEvent registered => new StudentDetails(
                StudentId: registered.StudentId,
                FirstName: registered.FirstName,
                LastName: registered.LastName,
                Email: registered.Email,
                EnrollmentTier: Tier.Basic,
                MaxEnrollmentCount: StudentMaxCourseEnrollment.GetMaxCoursesAllowed(Tier.Basic),
                CurrentEnrollmentCount: 0,
                EnrolledCourses: new List<EnrolledCourseInfo>()),

            StudentSubscriptionUpdatedEvent updated when current != null =>
                current with
                {
                    EnrollmentTier = updated.EnrollmentTier,
                    MaxEnrollmentCount = StudentMaxCourseEnrollment.GetMaxCoursesAllowed(updated.EnrollmentTier)
                },

            StudentEnrolledToCourseEvent enrolled when current != null =>
                ApplyStudentEnrolled(current, enrolled, relatedEvents),

            _ => current
        };
    }

    private static StudentDetails ApplyStudentEnrolled(
        StudentDetails current, 
        StudentEnrolledToCourseEvent evt, 
        SequencedEvent[] relatedEvents)
    {
        // Check if course already in list (idempotency)
        if (current.EnrolledCourses.Any(c => c.CourseId == evt.CourseId))
            return current;

        // Extract course details from related events
        var courseCreated = relatedEvents
            .Select(e => e.Event.Event)
            .OfType<CourseCreatedEvent>()
            .FirstOrDefault(c => c.CourseId == evt.CourseId);

        if (courseCreated == null)
        {
            // Should not happen if framework loaded related events correctly
            throw new InvalidOperationException(
                $"Could not find CourseCreatedEvent for course {evt.CourseId}. " +
                "This indicates a framework issue with loading related events.");
        }

        var courseInfo = new EnrolledCourseInfo(
            CourseId: evt.CourseId,
            Name: courseCreated.Name);

        var updatedCourses = new List<EnrolledCourseInfo>(current.EnrolledCourses) { courseInfo };

        return current with
        {
            CurrentEnrollmentCount = current.CurrentEnrollmentCount + 1,
            EnrolledCourses = updatedCourses
        };
    }
}
