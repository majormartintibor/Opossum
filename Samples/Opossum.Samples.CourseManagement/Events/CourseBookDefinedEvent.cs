namespace Opossum.Samples.CourseManagement.Events;

/// <summary>A new course book was added to the catalog. Tags: bookId:{bookId}.</summary>
public sealed record CourseBookDefinedEvent(
    Guid BookId,
    string Title,
    string Author,
    string Isbn,
    decimal Price) : IEvent;
