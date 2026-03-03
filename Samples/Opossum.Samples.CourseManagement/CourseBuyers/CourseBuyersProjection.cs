using Opossum.Core;
using Opossum.Projections;
using Opossum.Samples.CourseManagement.Events;

namespace Opossum.Samples.CourseManagement.CourseBuyers;

/// <summary>A single student who purchased the course's textbook.</summary>
public sealed record CourseBuyerEntry(
    Guid StudentId,
    Guid BookId,
    decimal PricePaid,
    DateTimeOffset PurchasedAt);

/// <summary>Read model for all students who purchased the textbook of a given course.</summary>
public sealed record CourseBuyersState(
    Guid CourseId,
    string CourseName,
    Guid BookId,
    IReadOnlyList<CourseBuyerEntry> Buyers);

/// <summary>
/// Persisted read-side projection for a course's book buyers.
/// Folds <see cref="CourseCreatedEvent"/>, <see cref="CourseBookDefinedEvent"/>,
/// <see cref="CourseBookPurchasedEvent"/>, and <see cref="CourseBooksOrderedEvent"/>
/// into a per-course read model keyed by <c>courseId</c>.
/// <para>
/// Known limitation: a <see cref="CourseBooksOrderedEvent"/> that contains books from
/// multiple courses carries several <c>courseId</c> tags. The projection key selector
/// returns only the first tag, so only that course's state is updated from such an order.
/// This limitation is avoided by the DataSeeder, which constrains all books in one order
/// to the same course.
/// </para>
/// </summary>
[ProjectionDefinition("CourseBuyers")]
public sealed class CourseBuyersProjection : IProjectionDefinition<CourseBuyersState>
{
    public string ProjectionName => "CourseBuyers";

    public string[] EventTypes =>
    [
        nameof(CourseCreatedEvent),
        nameof(CourseBookDefinedEvent),
        nameof(CourseBookPurchasedEvent),
        nameof(CourseBooksOrderedEvent)
    ];

    public string KeySelector(SequencedEvent evt)
    {
        var tag = evt.Event.Tags.FirstOrDefault(t => t.Key == "courseId")
            ?? throw new InvalidOperationException(
                $"Event {evt.Event.EventType} at position {evt.Position} is missing courseId tag.");

        return tag.Value;
    }

    public CourseBuyersState? Apply(CourseBuyersState? current, SequencedEvent evt)
    {
        var timestamp = evt.Metadata.Timestamp;

        return evt.Event.Event switch
        {
            CourseCreatedEvent e =>
                current ?? new CourseBuyersState(e.CourseId, e.Name, Guid.Empty, []),

            CourseBookDefinedEvent e when current is not null =>
                current with { BookId = e.BookId },

            CourseBookPurchasedEvent e when current is not null && current.BookId != Guid.Empty =>
                current with
                {
                    Buyers = [.. current.Buyers, new CourseBuyerEntry(e.StudentId, e.BookId, e.PricePaid, timestamp)]
                },

            CourseBooksOrderedEvent e when current is not null && current.BookId != Guid.Empty =>
                ApplyOrder(current, e, timestamp),

            _ => current
        };
    }

    private static CourseBuyersState ApplyOrder(
        CourseBuyersState state,
        CourseBooksOrderedEvent e,
        DateTimeOffset purchasedAt)
    {
        var newEntries = e.Items
            .Where(item => item.BookId == state.BookId)
            .Select(item => new CourseBuyerEntry(e.StudentId, item.BookId, item.PricePaid, purchasedAt))
            .ToList();

        if (newEntries.Count == 0)
            return state;

        return state with { Buyers = [.. state.Buyers, .. newEntries] };
    }
}
