namespace Opossum.Samples.CourseManagement.Events;

public sealed record StudentEnrolledToCourseEvent(
    Guid CourseId,
    Guid StudentId) : IEvent;
