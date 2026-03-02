using Opossum.Core;
using Opossum.Samples.CourseManagement.CourseBookPurchase;
using Opossum.Samples.CourseManagement.Events;

namespace Opossum.Samples.CourseManagement.UnitTests;

/// <summary>
/// Unit tests for <see cref="CourseBookPriceProjections"/> and <see cref="CourseBookPriceState"/>.
///
/// Covers the DCB "Dynamic Product Price" pattern:
/// https://dcb.events/examples/dynamic-product-price/
///
/// All tests are pure in-memory folds — no event store, no file system.
/// </summary>
public class CourseBookPriceProjectionTests
{
    private readonly Guid _bookId = Guid.NewGuid();
    private readonly Guid _courseId = Guid.NewGuid();

    // Fixed reference time for deterministic tests
    private static readonly DateTimeOffset _eventTime =
        new(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static SequencedEvent MakeEvent(
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
    // Feature 1 — CurrentPrice projection
    // -------------------------------------------------------------------------

    [Fact]
    public void CurrentPrice_InitialState_IsNegativeOne()
    {
        var projection = CourseBookPriceProjections.CurrentPrice(_bookId);

        Assert.Equal(-1m, projection.InitialState);
    }

    [Fact]
    public void CurrentPrice_Apply_CourseBookDefinedEvent_ReturnsPrice()
    {
        var projection = CourseBookPriceProjections.CurrentPrice(_bookId);
        var evt = MakeEvent(
            new CourseBookDefinedEvent(_bookId, "Title", "Author", "ISBN", 29.99m, _courseId),
            _eventTime,
            tags: ("bookId", _bookId.ToString()));
    }

    [Fact]
    public void CurrentPrice_Query_ContainsBookIdTag()
    {
        var projection = CourseBookPriceProjections.CurrentPrice(_bookId);

        var tag = projection.Query.QueryItems
            .SelectMany(qi => qi.Tags)
            .Single(t => t.Key == "bookId");

        Assert.Equal(_bookId.ToString(), tag.Value);
    }

    [Fact]
    public void CurrentPrice_Query_DoesNotMatchDifferentBookId()
    {
        var projection = CourseBookPriceProjections.CurrentPrice(_bookId);
        var otherBookId = Guid.NewGuid();
        var evt = MakeEvent(
            new CourseBookDefinedEvent(otherBookId, "Other", "Author", "ISBN", 9.99m, Guid.NewGuid()),
            _eventTime,
            tags: ("bookId", otherBookId.ToString()));

        Assert.False(projection.Query.Matches(evt));
    }

    // -------------------------------------------------------------------------
    // Feature 2 — PriceWithGracePeriod projection
    // -------------------------------------------------------------------------

    [Fact]
    public void PriceWithGracePeriod_InitialState_IsEmpty()
    {
        var projection = CourseBookPriceProjections.PriceWithGracePeriod(_bookId);

        Assert.Same(CourseBookPriceState.Empty, projection.InitialState);
        Assert.Null(projection.InitialState.CurrentPrice);
        Assert.Null(projection.InitialState.GracePeriodPrice);
    }

    [Fact]
    public void PriceWithGracePeriod_Apply_CourseBookDefinedEvent_SetsCurrentPrice()
    {
        // "now" is right at the event time — within grace period
        var tp = new FixedTimeProvider(_eventTime);
        var projection = CourseBookPriceProjections.PriceWithGracePeriod(_bookId, tp);

        var evt = MakeEvent(
            new CourseBookDefinedEvent(_bookId, "Title", "Author", "ISBN", 29.99m, _courseId),
            _eventTime,
            tags: ("bookId", _bookId.ToString()));

        var state = projection.Apply(projection.InitialState, evt);

        Assert.Equal(29.99m, state.CurrentPrice);
        Assert.Null(state.GracePeriodPrice);
    }

    [Fact]
    public void PriceWithGracePeriod_PriceChanged_WithinGracePeriod_BothPricesValid()
    {
        var tp = new FixedTimeProvider(_eventTime + TimeSpan.FromMinutes(5));
        var projection = CourseBookPriceProjections.PriceWithGracePeriod(_bookId, tp);

        // First: book defined
        var defineEvt = MakeEvent(
            new CourseBookDefinedEvent(_bookId, "Title", "Author", "ISBN", 20m, _courseId),
            _eventTime - TimeSpan.FromHours(1),
            position: 1,
            tags: ("bookId", _bookId.ToString()));

        // Then: price changed 5 minutes ago — within grace
        var priceChangeEvt = MakeEvent(
            new CourseBookPriceChangedEvent(_bookId, 25m),
            _eventTime,
            position: 2,
            tags: ("bookId", _bookId.ToString()));

        var state = projection.Apply(projection.InitialState, defineEvt);
        state = projection.Apply(state, priceChangeEvt);

        Assert.Equal(25m, state.CurrentPrice);
        Assert.Equal(20m, state.GracePeriodPrice); // old price still valid
        Assert.True(state.IsValidPrice(25m));       // new price valid
        Assert.True(state.IsValidPrice(20m));       // old price valid within grace
    }

    [Fact]
    public void PriceWithGracePeriod_PriceChanged_AfterGracePeriodExpired_OnlyNewPriceValid()
    {
        // "now" is 2 hours after the price change — grace period expired
        var tp = new FixedTimeProvider(_eventTime + TimeSpan.FromHours(2));
        var projection = CourseBookPriceProjections.PriceWithGracePeriod(_bookId, tp);

        var defineEvt = MakeEvent(
            new CourseBookDefinedEvent(_bookId, "Title", "Author", "ISBN", 20m, _courseId),
            _eventTime - TimeSpan.FromDays(1),
            position: 1,
            tags: ("bookId", _bookId.ToString()));

        var priceChangeEvt = MakeEvent(
            new CourseBookPriceChangedEvent(_bookId, 25m),
            _eventTime, // changed at eventTime, but "now" is 2h later
            position: 2,
            tags: ("bookId", _bookId.ToString()));

        var state = projection.Apply(projection.InitialState, defineEvt);
        state = projection.Apply(state, priceChangeEvt);

        Assert.Equal(25m, state.CurrentPrice);
        Assert.Null(state.GracePeriodPrice);        // grace expired
        Assert.True(state.IsValidPrice(25m));
        Assert.False(state.IsValidPrice(20m));      // old price no longer valid
    }

    [Fact]
    public void PriceWithGracePeriod_IsValidPrice_NeverValidPrice_ReturnsFalse()
    {
        var tp = new FixedTimeProvider(_eventTime);
        var projection = CourseBookPriceProjections.PriceWithGracePeriod(_bookId, tp);

        var evt = MakeEvent(
            new CourseBookDefinedEvent(_bookId, "Title", "Author", "ISBN", 29.99m, _courseId),
            _eventTime,
            tags: ("bookId", _bookId.ToString()));
    }

    [Fact]
    public void PriceWithGracePeriod_IsValidPrice_BookNotDefined_ReturnsFalse()
    {
        var state = CourseBookPriceState.Empty;

        Assert.False(state.IsValidPrice(0m));
        Assert.False(state.IsValidPrice(29.99m));
    }

    [Fact]
    public void PriceWithGracePeriod_MultiplePriceChanges_OnlyLastChangePreservesGrace()
    {
        // Only the most recent price change's previous price is in grace
        var tp = new FixedTimeProvider(_eventTime + TimeSpan.FromMinutes(5));
        var projection = CourseBookPriceProjections.PriceWithGracePeriod(_bookId, tp);

        var defineEvt = MakeEvent(
            new CourseBookDefinedEvent(_bookId, "T", "A", "I", 10m, _courseId),
            _eventTime - TimeSpan.FromHours(2),
            position: 1,
            tags: ("bookId", _bookId.ToString()));

        // First price change — old (expired)
        var change1 = MakeEvent(
            new CourseBookPriceChangedEvent(_bookId, 20m),
            _eventTime - TimeSpan.FromHours(1),
            position: 2,
            tags: ("bookId", _bookId.ToString()));

        // Second price change — recent (within grace)
        var change2 = MakeEvent(
            new CourseBookPriceChangedEvent(_bookId, 30m),
            _eventTime,
            position: 3,
            tags: ("bookId", _bookId.ToString()));

        var state = projection.Apply(projection.InitialState, defineEvt);
        state = projection.Apply(state, change1);
        state = projection.Apply(state, change2);

        Assert.Equal(30m, state.CurrentPrice);
        Assert.Equal(20m, state.GracePeriodPrice);  // previous price valid within grace
        Assert.False(state.IsValidPrice(10m));       // first price long expired
    }

    // -------------------------------------------------------------------------
    // BookExists guard projection
    // -------------------------------------------------------------------------

    [Fact]
    public void BookExists_InitialState_IsFalse()
    {
        var projection = CourseBookPriceProjections.BookExists(_bookId);

        Assert.False(projection.InitialState);
    }

    [Fact]
    public void BookExists_Apply_CourseBookDefinedEvent_ReturnsTrue()
    {
        var projection = CourseBookPriceProjections.BookExists(_bookId);
        var evt = MakeEvent(
            new CourseBookDefinedEvent(_bookId, "T", "A", "I", 9m, _courseId),
            _eventTime,
            tags: ("bookId", _bookId.ToString()));

        var state = projection.Apply(false, evt);

        Assert.True(state);
    }

    // -------------------------------------------------------------------------
    // CourseIdForBook projection
    // -------------------------------------------------------------------------

    [Fact]
    public void CourseIdForBook_InitialState_IsNull()
    {
        var projection = CourseBookPriceProjections.CourseIdForBook(_bookId);

        Assert.Null(projection.InitialState);
    }

    [Fact]
    public void CourseIdForBook_Apply_CourseBookDefinedEvent_ReturnsCourseId()
    {
        var projection = CourseBookPriceProjections.CourseIdForBook(_bookId);
        var evt = MakeEvent(
            new CourseBookDefinedEvent(_bookId, "T", "A", "I", 9m, _courseId),
            _eventTime,
            tags: ("bookId", _bookId.ToString()));

        var state = projection.Apply(null, evt);

        Assert.Equal(_courseId, state);
    }

    [Fact]
    public void CourseIdForBook_Query_ContainsBookIdTag()
    {
        var projection = CourseBookPriceProjections.CourseIdForBook(_bookId);

        var tag = projection.Query.QueryItems
            .SelectMany(qi => qi.Tags)
            .Single(t => t.Key == "bookId");

        Assert.Equal(_bookId.ToString(), tag.Value);
    }

    [Fact]
    public void CourseIdForBook_Query_DoesNotMatchDifferentBookId()
    {
        var projection = CourseBookPriceProjections.CourseIdForBook(_bookId);
        var otherBookId = Guid.NewGuid();
        var evt = MakeEvent(
            new CourseBookDefinedEvent(otherBookId, "T", "A", "I", 9m, Guid.NewGuid()),
            _eventTime,
            tags: ("bookId", otherBookId.ToString()));

        Assert.False(projection.Query.Matches(evt));
    }
}
