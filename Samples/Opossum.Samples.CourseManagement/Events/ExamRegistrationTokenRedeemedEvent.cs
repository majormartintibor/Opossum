namespace Opossum.Samples.CourseManagement.Events;

public sealed record ExamRegistrationTokenRedeemedEvent(
    Guid TokenId,
    Guid ExamId,
    Guid StudentId) : IEvent;
