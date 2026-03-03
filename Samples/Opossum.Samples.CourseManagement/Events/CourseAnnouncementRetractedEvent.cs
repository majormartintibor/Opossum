namespace Opossum.Samples.CourseManagement.Events;

public sealed record CourseAnnouncementRetractedEvent(
    Guid AnnouncementId,
    Guid CourseId,
    Guid IdempotencyToken) : IEvent;
