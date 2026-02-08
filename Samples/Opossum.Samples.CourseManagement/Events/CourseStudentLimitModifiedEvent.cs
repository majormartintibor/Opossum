namespace Opossum.Samples.CourseManagement.Events;

public sealed record CourseStudentLimitModifiedEvent(
    Guid CourseId,
    int NewMaxStudentCount) : IEvent;
