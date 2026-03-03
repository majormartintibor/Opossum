namespace Opossum.Samples.CourseManagement.Events;

public sealed record CourseAnnouncementPostedEvent(
    Guid AnnouncementId,
    Guid CourseId,
    string Title,
    string Body,
    Guid IdempotencyToken) : IEvent;
