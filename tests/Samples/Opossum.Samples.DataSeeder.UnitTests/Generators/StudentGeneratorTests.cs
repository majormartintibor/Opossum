using Opossum.Samples.CourseManagement.Events;
using Opossum.Samples.DataSeeder;
using Opossum.Samples.DataSeeder.Core;
using Opossum.Samples.DataSeeder.Generators;
using Tier = Opossum.Samples.CourseManagement.EnrollmentTier.EnrollmentTier;

namespace Opossum.Samples.DataSeeder.UnitTests.Generators;

/// <summary>
/// Unit tests for <see cref="StudentGenerator"/>.
/// All assertions operate on pure in-memory output — no I/O.
/// </summary>
public sealed class StudentGeneratorTests
{
    private static SeedingConfiguration DefaultConfig => new()
    {
        StudentCount             = 100,
        BasicTierPercentage      = 20,
        StandardTierPercentage   = 40,
        ProfessionalTierPercentage = 30,
        MasterTierPercentage     = 10
    };

    private static SeedContext NewContext() => new(randomSeed: 42);

    private readonly StudentGenerator _sut = new();

    [Fact]
    public void Generate_ProducesExactStudentCount()
    {
        var events = _sut.Generate(NewContext(), DefaultConfig);

        Assert.Equal(100, events.Count);
    }

    [Fact]
    public void Generate_EachEventHasStudentEmailTag()
    {
        var events = _sut.Generate(NewContext(), DefaultConfig);

        Assert.All(events, e => Assert.Contains(e.Event.Tags, t => t.Key == "studentEmail"));
    }

    [Fact]
    public void Generate_EachEventHasStudentIdTag()
    {
        var events = _sut.Generate(NewContext(), DefaultConfig);

        Assert.All(events, e => Assert.Contains(e.Event.Tags, t => t.Key == "studentId"));
    }

    [Fact]
    public void Generate_AllEmailsAreUnique()
    {
        var events = _sut.Generate(NewContext(), DefaultConfig);

        var emails = events
            .Select(e => e.Event.Tags.First(t => t.Key == "studentEmail").Value)
            .ToList();

        Assert.Equal(emails.Count, emails.Distinct().Count());
    }

    [Fact]
    public void Generate_PopulatesContextStudents()
    {
        var context = NewContext();
        _sut.Generate(context, DefaultConfig);

        Assert.Equal(100, context.Students.Count);
    }

    [Fact]
    public void Generate_TierDistributionMatchesConfig()
    {
        var context = NewContext();
        _sut.Generate(context, DefaultConfig);

        Assert.Equal(20, context.Students.Count(s => s.Tier == Tier.Basic));
        Assert.Equal(40, context.Students.Count(s => s.Tier == Tier.Standard));
        Assert.Equal(30, context.Students.Count(s => s.Tier == Tier.Professional));
        Assert.Equal(10, context.Students.Count(s => s.Tier == Tier.Master));
    }

    [Fact]
    public void Generate_EventPayloadsAreStudentRegisteredEvent()
    {
        var events = _sut.Generate(NewContext(), DefaultConfig);

        Assert.All(events, e => Assert.IsType<StudentRegisteredEvent>(e.Event.Event));
    }

    [Fact]
    public void Generate_TimestampsAreWithinExpectedWindow()
    {
        var events = _sut.Generate(NewContext(), DefaultConfig);
        var now    = DateTimeOffset.UtcNow;

        // 365–180 days ago (±1-day margin for test execution time)
        Assert.All(events, e =>
        {
            var daysAgo = (now - e.Metadata.Timestamp).TotalDays;
            Assert.InRange(daysAgo, 179, 366);
        });
    }

    [Fact]
    public void Generate_StudentIdTagMatchesContextStudentId()
    {
        var context = NewContext();
        var events  = _sut.Generate(context, DefaultConfig);

        var taggedIds   = events.Select(e => e.Event.Tags.First(t => t.Key == "studentId").Value).ToHashSet();
        var contextIds  = context.Students.Select(s => s.StudentId.ToString()).ToHashSet();

        Assert.Equal(contextIds, taggedIds);
    }
}
