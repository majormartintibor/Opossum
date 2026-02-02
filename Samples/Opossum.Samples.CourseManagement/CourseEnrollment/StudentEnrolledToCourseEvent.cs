namespace Opossum.Samples.CourseManagement.CourseEnrollment;

public sealed record StudentEnrolledToCourseEvent(Guid CourseId, Guid StudentId) : IEvent;
