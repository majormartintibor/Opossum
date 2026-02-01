using Opossum;
using Opossum.Core;
using Opossum.Projections;
using Opossum.Samples.CourseManagement.CourseCreation;
using Opossum.Samples.CourseManagement.CourseEnrollment;
using Opossum.Samples.CourseManagement.CourseStudentLimitModification;

namespace Opossum.Samples.CourseManagement.CourseShortInfo;

/// <summary>
/// Projection definition for CourseShortInfo materialized view
/// </summary>
[ProjectionDefinition("CourseShortInfo")]
public sealed class CourseShortInfoProjection : IProjectionDefinition<CourseShortInfo>
{
    public string ProjectionName => "CourseShortInfo";

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

    public CourseShortInfo? Apply(CourseShortInfo? current, IEvent evt)
    {
        return evt switch
        {
            CourseCreatedEvent created => new CourseShortInfo(
                CourseId: created.CourseId,
                Name: created.Name,
                MaxStudentCount: created.MaxStudentCount,
                CurrentEnrollmentCount: 0),

            CourseStudentLimitModifiedEvent limitModified when current != null =>
                current with { MaxStudentCount = limitModified.NewMaxStudentCount },

            StudentEnrolledToCourseEvent enrolled when current != null =>
                current with { CurrentEnrollmentCount = current.CurrentEnrollmentCount + 1 },

            _ => current
        };
    }
}
