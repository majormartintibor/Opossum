namespace Opossum.Samples.CourseManagement.Events;

/// <summary>The price of a course book was changed by an administrator. Tags: bookId:{bookId}.</summary>
public sealed record CourseBookPriceChangedEvent(
    Guid BookId,
    decimal NewPrice) : IEvent;
