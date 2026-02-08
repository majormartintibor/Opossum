namespace Opossum.Samples.CourseManagement.Events;

public sealed record StudentRegisteredEvent(
    Guid StudentId,
    string FirstName,
    string LastName,
    string Email) : IEvent;
