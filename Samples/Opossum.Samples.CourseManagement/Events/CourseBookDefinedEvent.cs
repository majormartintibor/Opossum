namespace Opossum.Samples.CourseManagement.Events;

/// <summary>A new course book was added to the catalog. Tags: bookId:{bookId}, courseId:{courseId}.</summary>
public sealed record CourseBookDefinedEvent(
    Guid BookId,
    string Title,
    string Author,
    string Isbn,
    decimal Price,
    Guid CourseId) : IEvent;
