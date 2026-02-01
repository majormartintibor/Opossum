using Opossum.Core;
using Opossum.Projections;
using Opossum.Samples.CourseManagement.CourseCreation;
using Opossum.Samples.CourseManagement.CourseEnrollment;
using Opossum.Samples.CourseManagement.CourseStudentLimitModification;
using Opossum.Samples.CourseManagement.StudentRegistration;

namespace Opossum.Samples.CourseManagement.CourseDetails;

/// <summary>
/// Lightweight student info for enrolled students list
/// </summary>
public sealed record EnrolledStudentInfo(
    Guid StudentId,
    string FirstName,
    string LastName,
    string Email);

/// <summary>
/// Course details with enrolled students
/// </summary>
public sealed record CourseDetails(
    Guid CourseId,
    string Name,
    int MaxStudentCount,
    int CurrentEnrollmentCount,
    List<EnrolledStudentInfo> EnrolledStudents)
{
    public bool IsFull => CurrentEnrollmentCount >= MaxStudentCount;
}

[ProjectionDefinition("CourseDetails")]
public sealed class CourseDetailsProjection : IMultiStreamProjectionDefinition<CourseDetails>
{
    public string ProjectionName => "CourseDetails";

    public string[] EventTypes => new[]
    {
        nameof(CourseCreatedEvent),
        nameof(CourseStudentLimitModifiedEvent),
        nameof(StudentEnrolledToCourseEvent)
    };

    public string KeySelector(SequencedEvent evt)
    {
        var courseIdTag = evt.Event.Tags.FirstOrDefault(t => t.Key == "courseId");

        if (courseIdTag == null)
        {
            throw new InvalidOperationException($"Event {evt.Event.EventType} at position {evt.Position} is missing courseId tag");
        }

        return courseIdTag.Value;
    }

    public Query? GetRelatedEventsQuery(IEvent evt)
    {
        // When a student enrolls, we need to fetch their registration details
        if (evt is StudentEnrolledToCourseEvent enrolled)
        {
            return Query.FromItems(new QueryItem
            {
                Tags = [new Tag { Key = "studentId", Value = enrolled.StudentId.ToString() }],
                EventTypes = [nameof(StudentRegisteredEvent)]
            });
        }

        return null; // No related events needed for other event types
    }

    public CourseDetails? Apply(CourseDetails? current, IEvent evt, SequencedEvent[] relatedEvents)
    {
        return evt switch
        {
            CourseCreatedEvent created => new CourseDetails(
                CourseId: created.CourseId,
                Name: created.Name,
                MaxStudentCount: created.MaxStudentCount,
                CurrentEnrollmentCount: 0,
                EnrolledStudents: new List<EnrolledStudentInfo>()),

            CourseStudentLimitModifiedEvent limitModified when current != null =>
                current with { MaxStudentCount = limitModified.NewMaxStudentCount },

            StudentEnrolledToCourseEvent enrolled when current != null =>
                ApplyStudentEnrolled(current, enrolled, relatedEvents),

            _ => current
        };
    }

    private static CourseDetails ApplyStudentEnrolled(
        CourseDetails current, 
        StudentEnrolledToCourseEvent evt, 
        SequencedEvent[] relatedEvents)
    {
        // Check if student already in list (idempotency)
        if (current.EnrolledStudents.Any(s => s.StudentId == evt.StudentId))
            return current;

        // Extract student details from related events
        var studentRegistered = relatedEvents
            .Select(e => e.Event.Event)
            .OfType<StudentRegisteredEvent>()
            .FirstOrDefault(s => s.StudentId == evt.StudentId);

        if (studentRegistered == null)
        {
            // Should not happen if framework loaded related events correctly
            throw new InvalidOperationException(
                $"Could not find StudentRegisteredEvent for student {evt.StudentId}. " +
                "This indicates a framework issue with loading related events.");
        }

        var studentInfo = new EnrolledStudentInfo(
            StudentId: evt.StudentId,
            FirstName: studentRegistered.FirstName,
            LastName: studentRegistered.LastName,
            Email: studentRegistered.Email);

        var updatedStudents = new List<EnrolledStudentInfo>(current.EnrolledStudents) { studentInfo };

        return current with
        {
            CurrentEnrollmentCount = current.CurrentEnrollmentCount + 1,
            EnrolledStudents = updatedStudents
        };
    }
}
