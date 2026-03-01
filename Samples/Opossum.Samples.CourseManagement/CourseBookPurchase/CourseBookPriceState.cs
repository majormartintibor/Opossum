namespace Opossum.Samples.CourseManagement.CourseBookPurchase;

/// <summary>
/// State produced by <see cref="CourseBookPriceProjections.PriceWithGracePeriod"/>.
/// Tracks which prices are currently valid for a course book, taking into account
/// a grace period during which both the old and the new price are accepted.
/// </summary>
/// <param name="CurrentPrice">
/// The most recently established price for this book, or <see langword="null"/> if the
/// book does not yet exist in the catalog.
/// </param>
/// <param name="GracePeriodPrice">
/// The previous price that is still valid within the grace window, or
/// <see langword="null"/> when no grace window is active.
/// </param>
public sealed record CourseBookPriceState(decimal? CurrentPrice, decimal? GracePeriodPrice)
{
    /// <summary>Initial state — book has not been defined yet.</summary>
    public static readonly CourseBookPriceState Empty = new(null, null);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="displayed"/> matches
    /// either the current price or the grace-period price.
    /// </summary>
    public bool IsValidPrice(decimal displayed) =>
        CurrentPrice == displayed || GracePeriodPrice == displayed;

    /// <summary>
    /// Applies a <c>CourseBookDefinedEvent</c>.
    /// If the event is within the grace period the initial price is the current price;
    /// otherwise it is still the current price (grace not relevant for a new book).
    /// </summary>
    internal CourseBookPriceState ApplyDefined(decimal price, TimeSpan age, TimeSpan gracePeriod) =>
        age <= gracePeriod
            ? new CourseBookPriceState(CurrentPrice: price, GracePeriodPrice: null)
            : new CourseBookPriceState(CurrentPrice: price, GracePeriodPrice: null);

    /// <summary>
    /// Applies a <c>CourseBookPriceChangedEvent</c>.
    /// If the event is within the grace period the old price is preserved as the
    /// grace-period price; otherwise only the new price is valid.
    /// </summary>
    internal CourseBookPriceState ApplyPriceChanged(decimal newPrice, TimeSpan age, TimeSpan gracePeriod) =>
        age <= gracePeriod
            ? new CourseBookPriceState(CurrentPrice: newPrice, GracePeriodPrice: CurrentPrice)
            : new CourseBookPriceState(CurrentPrice: newPrice, GracePeriodPrice: null);
}
