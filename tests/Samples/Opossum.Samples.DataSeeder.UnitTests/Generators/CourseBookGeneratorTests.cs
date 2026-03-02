using Opossum.Samples.CourseManagement.Events;
using Opossum.Samples.DataSeeder;
using Opossum.Samples.DataSeeder.Core;
using Opossum.Samples.DataSeeder.Generators;

namespace Opossum.Samples.DataSeeder.UnitTests.Generators;

/// <summary>
/// Unit tests for <see cref="CourseBookGenerator"/>.
/// Verifies book definition, price-change consistency, purchase price accuracy,
/// multi-book order structure, and SeedContext population.
/// </summary>
public sealed class CourseBookGeneratorTests
{
    private static SeedingConfiguration DefaultConfig => new()
    {
        StudentCount               = 20,
        BasicTierPercentage        = 100,
        StandardTierPercentage     = 0,
        ProfessionalTierPercentage = 0,
        MasterTierPercentage       = 0,
        CourseCount                = 4,
        SmallCoursePercentage      = 100,
        MediumCoursePercentage     = 0,
        LargeCoursePercentage      = 0,
        CourseBookCount            = 8,
        PriceChangePercentage      = 50,
        SingleBookPurchasesPerBook = 3,
        MultiBookOrders            = 4
    };

    private static SeedContext BuildContext()
    {
        var context = new SeedContext(randomSeed: 42);
        new StudentGenerator().Generate(context, DefaultConfig);
        new CourseGenerator().Generate(context, DefaultConfig);
        return context;
    }

    private readonly CourseBookGenerator _sut = new();

    [Fact]
    public void Generate_ProducesExactBookDefinitionCount()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        var definedCount = events.Count(e => e.Event.Event is CourseBookDefinedEvent);

        Assert.Equal(DefaultConfig.CourseBookCount, definedCount);
    }

    [Fact]
    public void Generate_PopulatesSeedContextBooks()
    {
        var context = BuildContext();
        _sut.Generate(context, DefaultConfig);

        Assert.Equal(DefaultConfig.CourseBookCount, context.Books.Count);
    }

    [Fact]
    public void Generate_SeedContextBookCourseIdsAreFromKnownCourses()
    {
        var context    = BuildContext();
        var courseIds  = context.Courses.Select(c => c.CourseId).ToHashSet();
        _sut.Generate(context, DefaultConfig);

        Assert.All(context.Books, b => Assert.Contains(b.CourseId, courseIds));
    }

    [Fact]
    public void Generate_EachDefinedBookHasBookIdAndCourseIdTags()
    {
        var events  = _sut.Generate(BuildContext(), DefaultConfig);
        var defined = events.Where(e => e.Event.Event is CourseBookDefinedEvent);

        Assert.All(defined, e =>
        {
            Assert.Contains(e.Event.Tags, t => t.Key == "bookId");
            Assert.Contains(e.Event.Tags, t => t.Key == "courseId");
        });
    }

    [Fact]
    public void Generate_BookIdTagMatchesPayloadBookId()
    {
        var events  = _sut.Generate(BuildContext(), DefaultConfig);
        var defined = events.Where(e => e.Event.Event is CourseBookDefinedEvent);

        Assert.All(defined, e =>
        {
            var evt       = (CourseBookDefinedEvent)e.Event.Event;
            var bookIdTag = e.Event.Tags.First(t => t.Key == "bookId").Value;
            Assert.Equal(evt.BookId.ToString(), bookIdTag);
        });
    }

    [Fact]
    public void Generate_CourseIdTagOnDefinedBookMatchesPayloadCourseId()
    {
        var events  = _sut.Generate(BuildContext(), DefaultConfig);
        var defined = events.Where(e => e.Event.Event is CourseBookDefinedEvent);

        Assert.All(defined, e =>
        {
            var evt          = (CourseBookDefinedEvent)e.Event.Event;
            var courseIdTag  = e.Event.Tags.First(t => t.Key == "courseId").Value;
            Assert.Equal(evt.CourseId.ToString(), courseIdTag);
        });
    }

    [Fact]
    public void Generate_PurchasePricePaidMatchesCurrentPriceAfterAnyChanges()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        // Build the final price for each book by replaying defined + changed events in timestamp order.
        var currentPrices = new Dictionary<Guid, decimal>();
        var sortedEvents  = events.OrderBy(e => e.Metadata.Timestamp).ToList();

        foreach (var e in sortedEvents)
        {
            switch (e.Event.Event)
            {
                case CourseBookDefinedEvent d:
                    currentPrices[d.BookId] = d.Price;
                    break;
                case CourseBookPriceChangedEvent p:
                    currentPrices[p.BookId] = p.NewPrice;
                    break;
            }
        }

        var purchaseEvents = events.Where(e => e.Event.Event is CourseBookPurchasedEvent);

        Assert.All(purchaseEvents, e =>
        {
            var purchase = (CourseBookPurchasedEvent)e.Event.Event;
            Assert.True(currentPrices.TryGetValue(purchase.BookId, out var expectedPrice));
            Assert.Equal(expectedPrice, purchase.PricePaid);
        });
    }

    [Fact]
    public void Generate_PriceChangedEventsReferenceDefinedBooks()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        var definedBookIds = events
            .Where(e => e.Event.Event is CourseBookDefinedEvent)
            .Select(e => ((CourseBookDefinedEvent)e.Event.Event).BookId)
            .ToHashSet();

        var priceChangedEvents = events.Where(e => e.Event.Event is CourseBookPriceChangedEvent);

        Assert.All(priceChangedEvents, e =>
        {
            var changed = (CourseBookPriceChangedEvent)e.Event.Event;
            Assert.Contains(changed.BookId, definedBookIds);
        });
    }

    [Fact]
    public void Generate_PurchaseEventsReferenceDefinedBooks()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        var definedBookIds = events
            .Where(e => e.Event.Event is CourseBookDefinedEvent)
            .Select(e => ((CourseBookDefinedEvent)e.Event.Event).BookId)
            .ToHashSet();

        var purchaseEvents = events.Where(e => e.Event.Event is CourseBookPurchasedEvent);

        Assert.All(purchaseEvents, e =>
        {
            var purchase = (CourseBookPurchasedEvent)e.Event.Event;
            Assert.Contains(purchase.BookId, definedBookIds);
        });
    }

    [Fact]
    public void Generate_ProducesExpectedSinglePurchaseCount()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        var purchaseCount = events.Count(e => e.Event.Event is CourseBookPurchasedEvent);

        Assert.Equal(DefaultConfig.CourseBookCount * DefaultConfig.SingleBookPurchasesPerBook, purchaseCount);
    }

    [Fact]
    public void Generate_EachSinglePurchaseHasBookIdStudentIdAndCourseIdTags()
    {
        var events    = _sut.Generate(BuildContext(), DefaultConfig);
        var purchases = events.Where(e => e.Event.Event is CourseBookPurchasedEvent);

        Assert.All(purchases, e =>
        {
            Assert.Contains(e.Event.Tags, t => t.Key == "bookId");
            Assert.Contains(e.Event.Tags, t => t.Key == "studentId");
            Assert.Contains(e.Event.Tags, t => t.Key == "courseId");
        });
    }

    [Fact]
    public void Generate_MultiBookOrdersHaveAtLeastOneItem()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);
        var orders  = events.Where(e => e.Event.Event is CourseBooksOrderedEvent);

        Assert.All(orders, e =>
        {
            var order = (CourseBooksOrderedEvent)e.Event.Event;
            Assert.NotEmpty(order.Items);
        });
    }

    [Fact]
    public void Generate_MultiBookOrderItemsHavePositivePricePaid()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);
        var orders  = events.Where(e => e.Event.Event is CourseBooksOrderedEvent);

        Assert.All(orders, e =>
        {
            var order = (CourseBooksOrderedEvent)e.Event.Event;
            Assert.All(order.Items, item => Assert.True(item.PricePaid > 0));
        });
    }

    [Fact]
    public void Generate_MultiBookOrdersHaveStudentIdTag()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);
        var orders  = events.Where(e => e.Event.Event is CourseBooksOrderedEvent);

        Assert.All(orders, e => Assert.Contains(e.Event.Tags, t => t.Key == "studentId"));
    }

    [Fact]
    public void Generate_MultiBookOrdersHaveBookIdTagPerItem()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);
        var orders  = events.Where(e => e.Event.Event is CourseBooksOrderedEvent);

        Assert.All(orders, e =>
        {
            var order        = (CourseBooksOrderedEvent)e.Event.Event;
            var bookIdTags   = e.Event.Tags.Where(t => t.Key == "bookId").ToList();
            Assert.Equal(order.Items.Count, bookIdTags.Count);
        });
    }

    [Fact]
    public void Generate_MultiBookOrdersHaveAtLeastOneCourseIdTag()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);
        var orders  = events.Where(e => e.Event.Event is CourseBooksOrderedEvent);

        Assert.All(orders, e => Assert.Contains(e.Event.Tags, t => t.Key == "courseId"));
    }

    [Fact]
    public void Generate_ReturnsEmptyWhenCourseBookCountIsZero()
    {
        var config = new SeedingConfiguration
        {
            StudentCount               = DefaultConfig.StudentCount,
            BasicTierPercentage        = DefaultConfig.BasicTierPercentage,
            StandardTierPercentage     = DefaultConfig.StandardTierPercentage,
            ProfessionalTierPercentage = DefaultConfig.ProfessionalTierPercentage,
            MasterTierPercentage       = DefaultConfig.MasterTierPercentage,
            CourseCount                = DefaultConfig.CourseCount,
            SmallCoursePercentage      = DefaultConfig.SmallCoursePercentage,
            MediumCoursePercentage     = DefaultConfig.MediumCoursePercentage,
            LargeCoursePercentage      = DefaultConfig.LargeCoursePercentage,
            CourseBookCount            = 0
        };
        var events = _sut.Generate(BuildContext(), config);

        Assert.Empty(events);
    }

    [Fact]
    public void Generate_ReturnsEmptyWhenNoCoursesExist()
    {
        var events = _sut.Generate(new SeedContext(randomSeed: 42), DefaultConfig);

        Assert.Empty(events);
    }

    [Fact]
    public void Generate_OrderItemPricePaidMatchesCurrentPriceAtOrderTime()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        // Build final price for each book (same replay as purchase-price test).
        var currentPrices = new Dictionary<Guid, decimal>();
        foreach (var e in events.OrderBy(e => e.Metadata.Timestamp))
        {
            switch (e.Event.Event)
            {
                case CourseBookDefinedEvent d:
                    currentPrices[d.BookId] = d.Price;
                    break;
                case CourseBookPriceChangedEvent p:
                    currentPrices[p.BookId] = p.NewPrice;
                    break;
            }
        }

        var orderEvents = events.Where(e => e.Event.Event is CourseBooksOrderedEvent);

        Assert.All(orderEvents, e =>
        {
            var order = (CourseBooksOrderedEvent)e.Event.Event;
            Assert.All(order.Items, item =>
            {
                Assert.True(currentPrices.TryGetValue(item.BookId, out var expectedPrice));
                Assert.Equal(expectedPrice, item.PricePaid);
            });
        });
    }

    [Fact]
    public void Generate_DefinedBookTimestampsAreOlderThanPriceChanges()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        var latestDefinedAt = events
            .Where(e => e.Event.Event is CourseBookDefinedEvent)
            .Max(e => e.Metadata.Timestamp);

        var earliestPriceChangeAt = events
            .Where(e => e.Event.Event is CourseBookPriceChangedEvent)
            .Select(e => e.Metadata.Timestamp)
            .DefaultIfEmpty(DateTimeOffset.MaxValue)
            .Min();

        Assert.True(latestDefinedAt <= earliestPriceChangeAt,
            "All book definitions should have timestamps older than or equal to the earliest price change.");
    }
}
