using Opossum.Core;
using Opossum.Samples.CourseManagement.Events;
using Opossum.Samples.DataSeeder.Core;

namespace Opossum.Samples.DataSeeder.Generators;

/// <summary>
/// Generates course-book catalog events across four types:
/// <see cref="CourseBookDefinedEvent"/>, <see cref="CourseBookPriceChangedEvent"/>,
/// <see cref="CourseBookPurchasedEvent"/>, and <see cref="CourseBooksOrderedEvent"/>.
/// <para>
/// Invariants enforced in pure code:
/// <list type="bullet">
///   <item>Books are assigned to courses before price changes or purchases.</item>
///   <item>Price changes reference only books that have been defined.</item>
///   <item><c>PricePaid</c> always matches the current in-memory price — no event-store reads.</item>
///   <item>Multi-book orders select books from the same course when possible.</item>
///   <item>All four event types carry the correct <c>courseId</c> tag.</item>
/// </list>
/// </para>
/// Populates <see cref="SeedContext.Books"/> for downstream consumers.
/// </summary>
public sealed class CourseBookGenerator : ISeedGenerator
{
    private static readonly string[] _bookTypes =
    [
        "Introduction to", "Principles of", "Foundations of",
        "Advanced Topics in", "A Guide to", "Essentials of",
        "Fundamentals of", "Studies in", "Modern", "Applied"
    ];

    private static readonly string[] _subjects =
    [
        "Mathematics", "Physics", "Chemistry", "Biology", "Computer Science",
        "History", "Economics", "Psychology", "Philosophy", "Literature",
        "Calculus", "Statistics", "Linear Algebra", "Engineering", "Architecture",
        "Sociology", "Anthropology", "Political Science", "Geography", "Art History",
        "Thermodynamics", "Quantum Mechanics", "Organic Chemistry", "Genetics", "Ecology"
    ];

    private static readonly string[] _authorFirstNames =
    [
        "Robert", "James", "Jennifer", "Michael", "Sarah", "David",
        "Linda", "William", "Patricia", "Richard", "Barbara", "Charles",
        "Susan", "Joseph", "Jessica", "Thomas", "Karen", "Mark", "Nancy", "Donald"
    ];

    private static readonly string[] _authorLastNames =
    [
        "Anderson", "Clark", "Davis", "Evans", "Foster", "Garcia",
        "Hall", "Jackson", "King", "Lewis", "Martin", "Nelson",
        "Owen", "Parker", "Quinn", "Roberts", "Scott", "Taylor", "Walker", "Young"
    ];

    public IReadOnlyList<SeedEvent> Generate(SeedContext context, SeedingConfiguration config)
    {
        if (config.CourseBookCount <= 0 || context.Courses.Count == 0) return [];

        var random    = context.Random;
        var bookCount = config.CourseBookCount;
        var events    = new List<SeedEvent>(
            bookCount * (1 + config.SingleBookPurchasesPerBook) + config.MultiBookOrders);

        // Shuffle courses so that the 1-per-course initial assignment is random,
        // then wrap around when CourseBookCount exceeds CourseCount.
        var shuffledCourses = context.Courses.OrderBy(_ => random.Next()).ToList();

        var bookToCourse  = new Dictionary<Guid, Guid>(bookCount);
        var booksByCourse = new Dictionary<Guid, List<Guid>>(context.Courses.Count);
        foreach (var c in context.Courses)
            booksByCourse[c.CourseId] = [];

        // Track current prices for the price-consistency invariant.
        var currentPrices = new Dictionary<Guid, decimal>(bookCount);

        // --- Phase 1: Define books ---------------------------------------------------
        for (var i = 0; i < bookCount; i++)
        {
            var bookId       = Guid.NewGuid();
            var courseId     = shuffledCourses[i % shuffledCourses.Count].CourseId;
            var initialPrice = Math.Round(9.99m + random.Next(0, 9001) / 100m, 2);
            var title        = $"{_bookTypes[random.Next(_bookTypes.Length)]} {_subjects[random.Next(_subjects.Length)]}";
            var author       = $"{_authorLastNames[random.Next(_authorLastNames.Length)]}, {_authorFirstNames[random.Next(_authorFirstNames.Length)]}";
            var isbn         = GenerateIsbn(random);

            bookToCourse[bookId]  = courseId;
            currentPrices[bookId] = initialPrice;
            context.Books.Add(new BookInfo(bookId, courseId));
            booksByCourse[courseId].Add(bookId);

            Tag[] definedTags =
            [
                new("bookId",   bookId.ToString()),
                new("courseId", courseId.ToString())
            ];

            events.Add(GeneratorHelper.CreateSeedEvent(
                new CourseBookDefinedEvent(bookId, title, author, isbn, initialPrice, courseId),
                definedTags,
                GeneratorHelper.RandomTimestamp(random, 300, 250)));
        }

        var allBookIds = bookToCourse.Keys.ToList();

        // --- Phase 2: Price changes --------------------------------------------------
        var priceChangeCount = bookCount * config.PriceChangePercentage / 100;
        foreach (var bookId in allBookIds.OrderBy(_ => random.Next()).Take(priceChangeCount))
        {
            // Vary price by ±20 %.
            var factor   = 0.80m + random.Next(0, 41) / 100m;
            var newPrice = Math.Max(1.00m, Math.Round(currentPrices[bookId] * factor, 2));
            currentPrices[bookId] = newPrice;

            Tag[] priceTags = [new("bookId", bookId.ToString())];

            events.Add(GeneratorHelper.CreateSeedEvent(
                new CourseBookPriceChangedEvent(bookId, newPrice),
                priceTags,
                GeneratorHelper.RandomTimestamp(random, 200, 100)));
        }

        // --- Phase 3: Single purchases -----------------------------------------------
        if (context.Students.Count > 0 && config.SingleBookPurchasesPerBook > 0)
        {
            foreach (var bookId in allBookIds)
            {
                var courseId  = bookToCourse[bookId];
                var pricePaid = currentPrices[bookId];

                for (var p = 0; p < config.SingleBookPurchasesPerBook; p++)
                {
                    var studentId = context.Students[random.Next(context.Students.Count)].StudentId;

                    Tag[] purchaseTags =
                    [
                        new("bookId",    bookId.ToString()),
                        new("studentId", studentId.ToString()),
                        new("courseId",  courseId.ToString())
                    ];

                    events.Add(GeneratorHelper.CreateSeedEvent(
                        new CourseBookPurchasedEvent(bookId, studentId, pricePaid),
                        purchaseTags,
                        GeneratorHelper.RandomTimestamp(random, 100, 7)));
                }
            }
        }

        // --- Phase 4: Multi-book orders ----------------------------------------------
        if (context.Students.Count > 0 && config.MultiBookOrders > 0 && allBookIds.Count >= 2)
        {
            // Prefer courses that have 2+ books so all items in one order share a courseId.
            var coursesWithMultipleBooks = booksByCourse
                .Where(kvp => kvp.Value.Count >= 2)
                .ToList();

            var ordersCreated = 0;
            var maxAttempts   = config.MultiBookOrders * 3;
            var attempts      = 0;

            while (ordersCreated < config.MultiBookOrders && attempts++ < maxAttempts)
            {
                List<Guid> selectedBooks;

                if (coursesWithMultipleBooks.Count > 0)
                {
                    var (_, courseBookList) = coursesWithMultipleBooks[random.Next(coursesWithMultipleBooks.Count)];
                    var orderSize           = Math.Min(random.Next(2, 5), courseBookList.Count);
                    if (orderSize < 2) continue;
                    selectedBooks = [.. courseBookList.OrderBy(_ => random.Next()).Take(orderSize)];
                }
                else
                {
                    // Fallback: cross-course order — projection limitation applies (see §13.2).
                    var orderSize = Math.Min(random.Next(2, 5), allBookIds.Count);
                    if (orderSize < 2) break;
                    selectedBooks = [.. allBookIds.OrderBy(_ => random.Next()).Take(orderSize)];
                }

                var studentId = context.Students[random.Next(context.Students.Count)].StudentId;
                var items     = selectedBooks
                    .Select(bid => new CourseBookOrderItem(bid, currentPrices[bid]))
                    .ToList();

                var uniqueCourseIds = selectedBooks
                    .Select(bid => bookToCourse[bid])
                    .Distinct()
                    .ToList();

                var orderTags = new List<Tag> { new("studentId", studentId.ToString()) };
                foreach (var bid in selectedBooks)
                    orderTags.Add(new Tag("bookId", bid.ToString()));
                foreach (var cid in uniqueCourseIds)
                    orderTags.Add(new Tag("courseId", cid.ToString()));

                events.Add(GeneratorHelper.CreateSeedEvent(
                    new CourseBooksOrderedEvent(studentId, items),
                    [.. orderTags],
                    GeneratorHelper.RandomTimestamp(random, 100, 7)));

                ordersCreated++;
            }
        }

        return events;
    }

    private static string GenerateIsbn(Random random)
    {
        var prefix = random.Next(2) == 0 ? "978" : "979";
        var group  = random.Next(10).ToString();
        var pub    = random.Next(10000, 100000).ToString();
        var title  = random.Next(100, 1000).ToString();
        var raw    = $"{prefix}{group}{pub}{title}";

        // Compute ISBN-13 check digit.
        var sum = 0;
        for (var i = 0; i < 12; i++)
            sum += (raw[i] - '0') * (i % 2 == 0 ? 1 : 3);
        var check = (10 - sum % 10) % 10;

        return $"{prefix}-{group}-{pub}-{title}-{check}";
    }
}
