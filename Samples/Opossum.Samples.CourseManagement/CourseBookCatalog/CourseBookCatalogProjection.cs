using Opossum.Core;
using Opossum.Projections;
using Opossum.Samples.CourseManagement.Events;

namespace Opossum.Samples.CourseManagement.CourseBookCatalog;

/// <summary>A single entry in the course book catalog read model.</summary>
public sealed record CourseBookCatalogEntry(
    Guid BookId,
    string Title,
    string Author,
    string Isbn,
    decimal CurrentPrice);

/// <summary>
/// Persisted read-side projection for the course book catalog.
/// Folds <see cref="CourseBookDefinedEvent"/> and <see cref="CourseBookPriceChangedEvent"/>
/// into a per-book catalog entry keyed by <c>bookId</c>.
/// </summary>
[ProjectionDefinition("CourseBookCatalog")]
public sealed class CourseBookCatalogProjection : IProjectionDefinition<CourseBookCatalogEntry>
{
    public string ProjectionName => "CourseBookCatalog";

    public string[] EventTypes =>
    [
        nameof(CourseBookDefinedEvent),
        nameof(CourseBookPriceChangedEvent)
    ];

    public string KeySelector(SequencedEvent evt)
    {
        var tag = evt.Event.Tags.FirstOrDefault(t => t.Key == "bookId")
            ?? throw new InvalidOperationException(
                $"Event {evt.Event.EventType} at position {evt.Position} is missing bookId tag.");

        return tag.Value;
    }

    public CourseBookCatalogEntry? Apply(CourseBookCatalogEntry? current, SequencedEvent evt) =>
        evt.Event.Event switch
        {
            CourseBookDefinedEvent e => new CourseBookCatalogEntry(
                BookId: e.BookId,
                Title: e.Title,
                Author: e.Author,
                Isbn: e.Isbn,
                CurrentPrice: e.Price),

            CourseBookPriceChangedEvent e when current is not null =>
                current with { CurrentPrice = e.NewPrice },

            _ => current
        };
}
