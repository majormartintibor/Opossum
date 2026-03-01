namespace Opossum.Samples.CourseManagement.Events;

public sealed record ExamRegistrationTokenRevokedEvent(
    Guid TokenId,
    Guid ExamId) : IEvent;
