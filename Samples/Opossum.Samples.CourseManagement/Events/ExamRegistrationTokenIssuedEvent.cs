namespace Opossum.Samples.CourseManagement.Events;

public sealed record ExamRegistrationTokenIssuedEvent(
    Guid TokenId,
    Guid ExamId,
    Guid CourseId) : IEvent;
