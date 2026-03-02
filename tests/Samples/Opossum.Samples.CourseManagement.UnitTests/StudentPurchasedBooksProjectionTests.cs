using Opossum.Core;
using Opossum.Samples.CourseManagement.Events;
using Opossum.Samples.CourseManagement.StudentPurchasedBooks;

namespace Opossum.Samples.CourseManagement.UnitTests;

/// <summary>
/// Unit tests for <see cref="StudentPurchasedBooksProjection"/>.
/// All tests are pure in-memory folds — no event store, no file system.
/// </summary>
public class StudentPurchasedBooksProjectionTests
{
    private readonly Guid _studentId = Guid.NewGuid();
    private readonly Guid _bookId1 = Guid.NewGuid();
    private readonly Guid _bookId2 = Guid.NewGuid();

    private static readonly DateTimeOffset _t1 = new(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _t2 = new(2025, 1, 2, 10, 0, 0, TimeSpan.Zero);

    private readonly StudentPurchasedBooksProjection _projection = new();

    private SequencedEvent MakeEvent(
        IEvent payload,
        DateTimeOffset timestamp,
        long position = 1,
        params (string Key, string Value)[] tags) =>
        new()
        {
            Position = position,
            Event = new DomainEvent
            {
                EventType = payload.GetType().Name,
                Event = payload,
                Tags = [.. tags.Select(t => new Tag(t.Key, t.Value))]
            },
            Metadata = new Metadata { Timestamp = timestamp }
        };

    // -------------------------------------------------------------------------
    // KeySelector
    // -------------------------------------------------------------------------

    [Fact]
    public void KeySelector_CourseBookPurchasedEvent_ReturnsStudentId()
    {
        var evt = MakeEvent(
            new CourseBookPurchasedEvent(_bookId1, _studentId, 29.99m),
            _t1,
            tags: ("studentId", _studentId.ToString()));

        var key = _projection.KeySelector(evt);

        Assert.Equal(_studentId.ToString(), key);
    }

    [Fact]
    public void KeySelector_MissingStudentIdTag_Throws()
    {
        var evt = MakeEvent(
            new CourseBookPurchasedEvent(_bookId1, _studentId, 29.99m),
            _t1);

        Assert.Throws<InvalidOperationException>(() => _projection.KeySelector(evt));
    }

    // -------------------------------------------------------------------------
    // Apply — CourseBookPurchasedEvent
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_NullState_CourseBookPurchasedEvent_CreatesNewState()
    {
        var evt = MakeEvent(
            new CourseBookPurchasedEvent(_bookId1, _studentId, 25m),
            _t1,
            tags: ("studentId", _studentId.ToString()));

        var state = _projection.Apply(null, evt);

        Assert.NotNull(state);
        Assert.Equal(_studentId, state.StudentId);
        var entry = Assert.Single(state.Books);
        Assert.Equal(_bookId1, entry.BookId);
        Assert.Equal(25m, entry.TotalPaid);
        Assert.Equal(1, entry.PurchaseCount);
        Assert.Equal(_t1, entry.FirstPurchasedAt);
        Assert.Equal(_t1, entry.LastPurchasedAt);
    }

    [Fact]
    public void Apply_ExistingState_NewBook_AddsEntry()
    {
        var existing = new StudentPurchasedBooksState(_studentId,
        [
            new PurchasedBookEntry(_bookId1, 25m, 1, _t1, _t1)
        ]);

        var evt = MakeEvent(
            new CourseBookPurchasedEvent(_bookId2, _studentId, 40m),
            _t2,
            tags: ("studentId", _studentId.ToString()));

        var state = _projection.Apply(existing, evt);

        Assert.NotNull(state);
        Assert.Equal(2, state.Books.Count);
        var newEntry = state.Books.Single(b => b.BookId == _bookId2);
        Assert.Equal(40m, newEntry.TotalPaid);
        Assert.Equal(1, newEntry.PurchaseCount);
    }

    [Fact]
    public void Apply_SameBookPurchasedAgain_IncrementsCountAndTotalPaid()
    {
        var existing = new StudentPurchasedBooksState(_studentId,
        [
            new PurchasedBookEntry(_bookId1, 25m, 1, _t1, _t1)
        ]);

        var evt = MakeEvent(
            new CourseBookPurchasedEvent(_bookId1, _studentId, 25m),
            _t2,
            tags: ("studentId", _studentId.ToString()));

        var state = _projection.Apply(existing, evt);

        Assert.NotNull(state);
        var entry = Assert.Single(state.Books);
        Assert.Equal(50m, entry.TotalPaid);
        Assert.Equal(2, entry.PurchaseCount);
        Assert.Equal(_t1, entry.FirstPurchasedAt);
        Assert.Equal(_t2, entry.LastPurchasedAt);
    }

    [Fact]
    public void Apply_SameBookPurchasedAgain_FirstPurchasedAtPreserved()
    {
        var existing = new StudentPurchasedBooksState(_studentId,
        [
            new PurchasedBookEntry(_bookId1, 25m, 1, _t1, _t1)
        ]);

        var evt = MakeEvent(
            new CourseBookPurchasedEvent(_bookId1, _studentId, 30m),
            _t2,
            tags: ("studentId", _studentId.ToString()));

        var state = _projection.Apply(existing, evt);

        Assert.NotNull(state);
        Assert.Equal(_t1, state.Books[0].FirstPurchasedAt);
    }

    // -------------------------------------------------------------------------
    // Apply — CourseBooksOrderedEvent
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_NullState_CourseBooksOrderedEvent_CreatesEntriesPerItem()
    {
        var items = new List<CourseBookOrderItem>
        {
            new(_bookId1, 20m),
            new(_bookId2, 35m)
        };

        var evt = MakeEvent(
            new CourseBooksOrderedEvent(_studentId, items),
            _t1,
            tags: ("studentId", _studentId.ToString()));

        var state = _projection.Apply(null, evt);

        Assert.NotNull(state);
        Assert.Equal(_studentId, state.StudentId);
        Assert.Equal(2, state.Books.Count);
        Assert.Contains(state.Books, b => b.BookId == _bookId1 && b.TotalPaid == 20m && b.PurchaseCount == 1);
        Assert.Contains(state.Books, b => b.BookId == _bookId2 && b.TotalPaid == 35m && b.PurchaseCount == 1);
    }

    [Fact]
    public void Apply_OrderWithDuplicateBook_AggregatesWithinOrder()
    {
        var items = new List<CourseBookOrderItem>
        {
            new(_bookId1, 20m),
            new(_bookId1, 20m)  // same book twice in one order
        };

        var evt = MakeEvent(
            new CourseBooksOrderedEvent(_studentId, items),
            _t1,
            tags: ("studentId", _studentId.ToString()));

        var state = _projection.Apply(null, evt);

        Assert.NotNull(state);
        var entry = Assert.Single(state.Books);
        Assert.Equal(_bookId1, entry.BookId);
        Assert.Equal(40m, entry.TotalPaid);
        Assert.Equal(2, entry.PurchaseCount);
    }

    [Fact]
    public void Apply_OrderAfterSinglePurchase_AccumulatesCorrectly()
    {
        var existing = new StudentPurchasedBooksState(_studentId,
        [
            new PurchasedBookEntry(_bookId1, 25m, 1, _t1, _t1)
        ]);

        var items = new List<CourseBookOrderItem>
        {
            new(_bookId1, 25m),  // already purchased
            new(_bookId2, 40m)   // new book
        };

        var evt = MakeEvent(
            new CourseBooksOrderedEvent(_studentId, items),
            _t2,
            tags: ("studentId", _studentId.ToString()));

        var state = _projection.Apply(existing, evt);

        Assert.NotNull(state);
        Assert.Equal(2, state.Books.Count);

        var book1 = state.Books.Single(b => b.BookId == _bookId1);
        Assert.Equal(50m, book1.TotalPaid);
        Assert.Equal(2, book1.PurchaseCount);
        Assert.Equal(_t1, book1.FirstPurchasedAt);
        Assert.Equal(_t2, book1.LastPurchasedAt);

        var book2 = state.Books.Single(b => b.BookId == _bookId2);
        Assert.Equal(40m, book2.TotalPaid);
        Assert.Equal(1, book2.PurchaseCount);
    }
}
