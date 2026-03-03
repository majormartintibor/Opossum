using Opossum.Core;
using Opossum.DecisionModel;
using Opossum.Samples.CourseManagement.Events;

namespace Opossum.Samples.CourseManagement.CourseBookPurchase;

/// <summary>
/// Factory methods for Decision Model projections used by the Course Book purchase commands.
/// Implements Features 1, 2, and 3 of the DCB "Dynamic Product Price" example:
/// https://dcb.events/examples/dynamic-product-price/
/// </summary>
public static class CourseBookPriceProjections
{
    /// <summary>Grace period during which the previous price remains valid after a price change.</summary>
    public static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Feature 1 — Returns the current price of a course book, or <c>-1</c> when the book
    /// does not exist. No grace period — the latest price is the only valid price.
    /// </summary>
    public static IDecisionProjection<decimal> CurrentPrice(Guid bookId) =>
        new DecisionProjection<decimal>(
            initialState: -1m,
            query: Query.FromItems(new QueryItem
            {
                EventTypes = [nameof(CourseBookDefinedEvent)],
                Tags = [new Tag("bookId", bookId.ToString())]
            }),
            apply: (_, evt) => evt.Event.Event is CourseBookDefinedEvent e ? e.Price : -1m);

    /// <summary>
    /// Feature 2 — Returns the price state for a course book, supporting a configurable
    /// grace period during which both the old and new price are accepted after a price change.
    /// Inject a custom <see cref="TimeProvider"/> in tests to control wall-clock time.
    /// </summary>
    public static IDecisionProjection<CourseBookPriceState> PriceWithGracePeriod(
        Guid bookId,
        TimeProvider? timeProvider = null,
        TimeSpan? gracePeriod = null)
    {
        var gp = gracePeriod ?? DefaultGracePeriod;

        return new DecisionProjection<CourseBookPriceState>(
            initialState: CourseBookPriceState.Empty,
            query: Query.FromItems(new QueryItem
            {
                EventTypes =
                [
                    nameof(CourseBookDefinedEvent),
                    nameof(CourseBookPriceChangedEvent)
                ],
                Tags = [new Tag("bookId", bookId.ToString())]
            }),
            apply: (state, evt, tp) =>
            {
                var age = tp.GetUtcNow() - evt.Metadata.Timestamp;
                return evt.Event.Event switch
                {
                    CourseBookDefinedEvent e => state.ApplyDefined(e.Price, age, gp),
                    CourseBookPriceChangedEvent e => state.ApplyPriceChanged(e.NewPrice, age, gp),
                    _ => state
                };
            },
            timeProvider: timeProvider);
    }

    /// <summary>
    /// Guard projection — returns <see langword="true"/> when the book already exists.
    /// Used to prevent redefining an existing book and to guard <c>ChangeCourseBookPrice</c>.
    /// </summary>
    public static IDecisionProjection<bool> BookExists(Guid bookId) =>
        new DecisionProjection<bool>(
            initialState: false,
            query: Query.FromItems(new QueryItem
            {
                EventTypes = [nameof(CourseBookDefinedEvent)],
                Tags = [new Tag("bookId", bookId.ToString())]
            }),
            apply: (state, evt) => evt.Event.Event is CourseBookDefinedEvent || state);

    /// <summary>
    /// Returns the <see cref="CourseBookDefinedEvent.CourseId"/> for the given book,
    /// or <see langword="null"/> when the book does not exist.
    /// Used to tag purchase and order events with the course the book belongs to.
    /// </summary>
    public static IDecisionProjection<Guid?> CourseIdForBook(Guid bookId) =>
        new DecisionProjection<Guid?>(
            initialState: null,
            query: Query.FromItems(new QueryItem
            {
                EventTypes = [nameof(CourseBookDefinedEvent)],
                Tags = [new Tag("bookId", bookId.ToString())]
            }),
            apply: (_, evt) => evt.Event.Event is CourseBookDefinedEvent e ? e.CourseId : null);
}
