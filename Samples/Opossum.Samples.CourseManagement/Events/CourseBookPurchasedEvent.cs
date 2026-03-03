namespace Opossum.Samples.CourseManagement.Events;

/// <summary>A student purchased a single course book. Tags: bookId:{bookId}, studentId:{studentId}.</summary>
public sealed record CourseBookPurchasedEvent(
    Guid BookId,
    Guid StudentId,
    decimal PricePaid) : IEvent;
