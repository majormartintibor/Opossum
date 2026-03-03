using Opossum.Samples.CourseManagement.Events;
using Opossum.Samples.DataSeeder;
using Opossum.Samples.DataSeeder.Core;
using Opossum.Samples.DataSeeder.Generators;

namespace Opossum.Samples.DataSeeder.UnitTests.Generators;

/// <summary>
/// Unit tests for <see cref="InvoiceGenerator"/>.
/// </summary>
public sealed class InvoiceGeneratorTests
{
    private static SeedingConfiguration DefaultConfig => new()
    {
        StudentCount  = 20,
        InvoiceCount  = 30,
        BasicTierPercentage        = 100,
        StandardTierPercentage     = 0,
        ProfessionalTierPercentage = 0,
        MasterTierPercentage       = 0,
        CourseCount                = 0,
        SmallCoursePercentage      = 0,
        MediumCoursePercentage     = 0,
        LargeCoursePercentage      = 0
    };

    private static SeedContext BuildContext()
    {
        var context = new SeedContext(randomSeed: 42);
        new StudentGenerator().Generate(context, DefaultConfig);
        return context;
    }

    private readonly InvoiceGenerator _sut = new();

    [Fact]
    public void Generate_ProducesExactInvoiceCount()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        Assert.Equal(30, events.Count);
    }

    [Fact]
    public void Generate_ReturnsEmptyWhenInvoiceCountIsZero()
    {
        var config = new SeedingConfiguration { InvoiceCount = 0 };

        var events = _sut.Generate(new SeedContext(randomSeed: 42), config);

        Assert.Empty(events);
    }

    [Fact]
    public void Generate_EventPayloadsAreInvoiceCreatedEvent()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        Assert.All(events, e => Assert.IsType<InvoiceCreatedEvent>(e.Event.Event));
    }

    [Fact]
    public void Generate_EachEventHasInvoiceNumberTag()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        Assert.All(events, e => Assert.Contains(e.Event.Tags, t => t.Key == "invoiceNumber"));
    }

    [Fact]
    public void Generate_InvoiceNumbersAreSequentialStartingAtOne()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        var numbers = events
            .Select(e => ((InvoiceCreatedEvent)e.Event.Event).InvoiceNumber)
            .OrderBy(n => n)
            .ToList();

        for (var i = 0; i < numbers.Count; i++)
            Assert.Equal(i + 1, numbers[i]);
    }

    [Fact]
    public void Generate_InvoiceNumberTagMatchesPayloadInvoiceNumber()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        Assert.All(events, e =>
        {
            var payload    = (InvoiceCreatedEvent)e.Event.Event;
            var taggedNum  = e.Event.Tags.First(t => t.Key == "invoiceNumber").Value;
            Assert.Equal(payload.InvoiceNumber.ToString(), taggedNum);
        });
    }

    [Fact]
    public void Generate_IssuedAtMatchesMetadataTimestamp()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        Assert.All(events, e =>
        {
            var payload = (InvoiceCreatedEvent)e.Event.Event;
            Assert.Equal(payload.IssuedAt, e.Metadata.Timestamp);
        });
    }

    [Fact]
    public void Generate_AllAmountsArePositive()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        Assert.All(events, e =>
        {
            var payload = (InvoiceCreatedEvent)e.Event.Event;
            Assert.True(payload.Amount > 0, $"Amount {payload.Amount} must be positive.");
        });
    }

    [Fact]
    public void Generate_TimestampsAreWithinExpectedWindow()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);
        var now    = DateTimeOffset.UtcNow;

        // 90–1 days ago (±1-day margin)
        Assert.All(events, e =>
        {
            var daysAgo = (now - e.Metadata.Timestamp).TotalDays;
            Assert.InRange(daysAgo, 0, 91);
        });
    }

    [Fact]
    public void Generate_CustomerIdsAreDrawnFromContextStudents()
    {
        var context    = BuildContext();
        var studentIds = context.Students.Select(s => s.StudentId).ToHashSet();

        var events = _sut.Generate(context, DefaultConfig);

        Assert.All(events, e =>
        {
            var payload = (InvoiceCreatedEvent)e.Event.Event;
            Assert.Contains(payload.CustomerId, studentIds);
        });
    }
}
