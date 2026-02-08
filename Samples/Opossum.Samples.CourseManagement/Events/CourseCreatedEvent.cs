namespace Opossum.Samples.CourseManagement.Events;

public sealed record CourseCreatedEvent(
    Guid CourseId,
    string Name,
    string Description,
    int MaxStudentCount) : IEvent;
