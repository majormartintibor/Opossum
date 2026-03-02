using Opossum.Samples.CourseManagement.Events;
using Opossum.Samples.DataSeeder;
using Opossum.Samples.DataSeeder.Core;
using Opossum.Samples.DataSeeder.Generators;

namespace Opossum.Samples.DataSeeder.UnitTests.Generators;

/// <summary>
/// Unit tests for <see cref="CourseGenerator"/>.
/// </summary>
public sealed class CourseGeneratorTests
{
    private static SeedingConfiguration DefaultConfig => new()
    {
        CourseCount            = 100,
        SmallCoursePercentage  = 27,
        MediumCoursePercentage = 53,
        LargeCoursePercentage  = 20
    };

    private static SeedContext NewContext() => new(randomSeed: 42);

    private readonly CourseGenerator _sut = new();

    [Fact]
    public void Generate_ProducesExactCourseCount()
    {
        // 27 + 53 + 20 = 100 — all three size categories sum to CourseCount.
        var events = _sut.Generate(NewContext(), DefaultConfig);

        Assert.Equal(100, events.Count);
    }

    [Fact]
    public void Generate_EachEventHasCourseIdTag()
    {
        var events = _sut.Generate(NewContext(), DefaultConfig);

        Assert.All(events, e => Assert.Contains(e.Event.Tags, t => t.Key == "courseId"));
    }

    [Fact]
    public void Generate_PopulatesContextCourses()
    {
        var context = NewContext();
        _sut.Generate(context, DefaultConfig);

        Assert.Equal(100, context.Courses.Count);
    }

    [Fact]
    public void Generate_AllCourseIdsAreUnique()
    {
        var context = NewContext();
        _sut.Generate(context, DefaultConfig);

        var ids = context.Courses.Select(c => c.CourseId).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Generate_EventPayloadsAreCourseCreatedEvent()
    {
        var events = _sut.Generate(NewContext(), DefaultConfig);

        Assert.All(events, e => Assert.IsType<CourseCreatedEvent>(e.Event.Event));
    }

    [Fact]
    public void Generate_SmallCoursesHaveCapacityBetween10And15()
    {
        var context = NewContext();
        var events  = _sut.Generate(context, DefaultConfig);

        var smallCapacities = context.Courses
            .Take(DefaultConfig.CourseCount * DefaultConfig.SmallCoursePercentage / 100)
            .Select(c => c.MaxCapacity)
            .ToList();

        Assert.All(smallCapacities, cap => Assert.InRange(cap, 10, 15));
    }

    [Fact]
    public void Generate_LargeCoursesHaveCapacityBetween40And60()
    {
        var context = NewContext();
        var events  = _sut.Generate(context, DefaultConfig);

        // Large courses are generated last in the distribution.
        var largeStart     = DefaultConfig.CourseCount - DefaultConfig.CourseCount * DefaultConfig.LargeCoursePercentage / 100;
        var largeCapacities = context.Courses
            .Skip(largeStart)
            .Select(c => c.MaxCapacity)
            .ToList();

        Assert.All(largeCapacities, cap => Assert.InRange(cap, 40, 60));
    }

    [Fact]
    public void Generate_TimestampsAreWithinExpectedWindow()
    {
        var events = _sut.Generate(NewContext(), DefaultConfig);
        var now    = DateTimeOffset.UtcNow;

        // 365–200 days ago (±1-day margin)
        Assert.All(events, e =>
        {
            var daysAgo = (now - e.Metadata.Timestamp).TotalDays;
            Assert.InRange(daysAgo, 199, 366);
        });
    }

    [Fact]
    public void Generate_CourseIdTagMatchesContextCourseId()
    {
        var context = NewContext();
        var events  = _sut.Generate(context, DefaultConfig);

        var taggedIds  = events.Select(e => e.Event.Tags.First(t => t.Key == "courseId").Value).ToHashSet();
        var contextIds = context.Courses.Select(c => c.CourseId.ToString()).ToHashSet();

        Assert.Equal(contextIds, taggedIds);
    }
}
