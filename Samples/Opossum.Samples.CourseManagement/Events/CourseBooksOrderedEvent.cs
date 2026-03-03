namespace Opossum.Samples.CourseManagement.Events;

/// <summary>A student ordered multiple course books in a single transaction. Tags: bookId:{bookId} per item, studentId:{studentId}.</summary>
public sealed record CourseBooksOrderedEvent(
    Guid StudentId,
    IReadOnlyList<CourseBookOrderItem> Items) : IEvent;

/// <summary>A single line-item within a <see cref="CourseBooksOrderedEvent"/>.</summary>
public sealed record CourseBookOrderItem(Guid BookId, decimal PricePaid);
