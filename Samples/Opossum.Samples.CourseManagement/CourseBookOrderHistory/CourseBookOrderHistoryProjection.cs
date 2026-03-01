using Opossum.Core;
using Opossum.Projections;
using Opossum.Samples.CourseManagement.Events;

namespace Opossum.Samples.CourseManagement.CourseBookOrderHistory;

/// <summary>A single entry in the course book order history read model.</summary>
public sealed record CourseBookOrderHistoryEntry(
    Guid OrderId,
    Guid StudentId,
    IReadOnlyList<CourseBookOrderHistoryItem> Items,
    DateTimeOffset OrderedAt);

/// <summary>A single line-item in an order history entry.</summary>
public sealed record CourseBookOrderHistoryItem(Guid BookId, decimal PricePaid);

/// <summary>
/// Persisted read-side projection for course book order history.
/// Folds <see cref="CourseBookPurchasedEvent"/> (single-book) and
/// <see cref="CourseBooksOrderedEvent"/> (shopping cart) into per-order entries
/// keyed by a synthetic order ID (the event position as string).
/// </summary>
[ProjectionDefinition("CourseBookOrderHistory")]
public sealed class CourseBookOrderHistoryProjection : IProjectionDefinition<CourseBookOrderHistoryEntry>
{
    public string ProjectionName => "CourseBookOrderHistory";

    public string[] EventTypes =>
    [
        nameof(CourseBookPurchasedEvent),
        nameof(CourseBooksOrderedEvent)
    ];

    public string KeySelector(SequencedEvent evt) => evt.Position.ToString();

    public CourseBookOrderHistoryEntry? Apply(CourseBookOrderHistoryEntry? current, SequencedEvent evt) =>
        evt.Event.Event switch
        {
            CourseBookPurchasedEvent e => new CourseBookOrderHistoryEntry(
                OrderId: Guid.NewGuid(),
                StudentId: e.StudentId,
                Items: [new CourseBookOrderHistoryItem(e.BookId, e.PricePaid)],
                OrderedAt: evt.Metadata.Timestamp),

            CourseBooksOrderedEvent e => new CourseBookOrderHistoryEntry(
                OrderId: Guid.NewGuid(),
                StudentId: e.StudentId,
                Items: e.Items
                    .Select(i => new CourseBookOrderHistoryItem(i.BookId, i.PricePaid))
                    .ToList(),
                OrderedAt: evt.Metadata.Timestamp),

            _ => current
        };
}
