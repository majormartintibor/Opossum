using Opossum.Core;
using Opossum.Projections;
using Opossum.Samples.CourseManagement.Events;

namespace Opossum.Samples.CourseManagement.StudentPurchasedBooks;

/// <summary>A single deduplicated book entry in a student's purchase history.</summary>
public sealed record PurchasedBookEntry(
    Guid BookId,
    decimal TotalPaid,
    int PurchaseCount,
    DateTimeOffset FirstPurchasedAt,
    DateTimeOffset LastPurchasedAt);

/// <summary>Read model for all books a student has ever purchased.</summary>
public sealed record StudentPurchasedBooksState(
    Guid StudentId,
    IReadOnlyList<PurchasedBookEntry> Books);

/// <summary>
/// Persisted read-side projection for a student's purchased books.
/// Folds <see cref="CourseBookPurchasedEvent"/> and <see cref="CourseBooksOrderedEvent"/>
/// into a per-student read model keyed by <c>studentId</c>.
/// Book entries are deduplicated by <c>bookId</c>; repeated purchases accumulate into
/// <see cref="PurchasedBookEntry.TotalPaid"/> and <see cref="PurchasedBookEntry.PurchaseCount"/>.
/// </summary>
[ProjectionDefinition("StudentPurchasedBooks")]
public sealed class StudentPurchasedBooksProjection : IProjectionDefinition<StudentPurchasedBooksState>
{
    public string ProjectionName => "StudentPurchasedBooks";

    public string[] EventTypes =>
    [
        nameof(CourseBookPurchasedEvent),
        nameof(CourseBooksOrderedEvent)
    ];

    public string KeySelector(SequencedEvent evt)
    {
        var tag = evt.Event.Tags.FirstOrDefault(t => t.Key == "studentId")
            ?? throw new InvalidOperationException(
                $"Event {evt.Event.EventType} at position {evt.Position} is missing studentId tag.");

        return tag.Value;
    }

    public StudentPurchasedBooksState? Apply(StudentPurchasedBooksState? current, SequencedEvent evt)
    {
        var timestamp = evt.Metadata.Timestamp;

        return evt.Event.Event switch
        {
            CourseBookPurchasedEvent e =>
                Upsert(
                    current ?? new StudentPurchasedBooksState(e.StudentId, []),
                    e.BookId,
                    e.PricePaid,
                    timestamp),

            CourseBooksOrderedEvent e =>
                e.Items.Aggregate(
                    current ?? new StudentPurchasedBooksState(e.StudentId, []),
                    (state, item) => Upsert(state, item.BookId, item.PricePaid, timestamp)),

            _ => current
        };
    }

    private static StudentPurchasedBooksState Upsert(
        StudentPurchasedBooksState state,
        Guid bookId,
        decimal pricePaid,
        DateTimeOffset purchasedAt)
    {
        var existing = state.Books.FirstOrDefault(b => b.BookId == bookId);

        List<PurchasedBookEntry> updated;
        if (existing is null)
        {
            updated = [.. state.Books, new PurchasedBookEntry(bookId, pricePaid, 1, purchasedAt, purchasedAt)];
        }
        else
        {
            updated = state.Books
                .Select(b => b.BookId == bookId
                    ? b with
                    {
                        TotalPaid = b.TotalPaid + pricePaid,
                        PurchaseCount = b.PurchaseCount + 1,
                        LastPurchasedAt = purchasedAt > b.LastPurchasedAt ? purchasedAt : b.LastPurchasedAt
                    }
                    : b)
                .ToList();
        }

        return state with { Books = updated };
    }
}
