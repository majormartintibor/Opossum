using Opossum.Core;
using Opossum.Projections;
using Opossum.Samples.CourseManagement.Events;

namespace Opossum.Samples.CourseManagement.CourseShortInfo;

/// <summary>
/// Projection definition for CourseShortInfo materialized view
/// </summary>
[ProjectionDefinition("CourseShortInfo")]
[ProjectionTags(typeof(CourseShortInfoTagProvider))]
public sealed class CourseShortInfoProjection : IProjectionDefinition<CourseShortInfo>
{
    public string ProjectionName => "CourseShortInfo";

    public string[] EventTypes =>
    [
        nameof(CourseCreatedEvent),
        nameof(CourseStudentLimitModifiedEvent),
        nameof(StudentEnrolledToCourseEvent)
    ];

    public string KeySelector(SequencedEvent evt)
    {
        var courseIdTag = evt.Event.Tags.FirstOrDefault(t => t.Key == "courseId") ?? throw new InvalidOperationException($"Event {evt.Event.EventType} at position {evt.Position} is missing courseId tag");

        return courseIdTag.Value;
    }

    public CourseShortInfo? Apply(CourseShortInfo? current, SequencedEvent evt)
    {
        return evt.Event.Event switch
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
