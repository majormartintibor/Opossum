using Opossum.Samples.CourseManagement.Events;
using Opossum.Samples.DataSeeder;
using Opossum.Samples.DataSeeder.Core;
using Opossum.Samples.DataSeeder.Generators;

namespace Opossum.Samples.DataSeeder.UnitTests.Generators;

/// <summary>
/// Unit tests for <see cref="CapacityChangeGenerator"/>.
/// </summary>
public sealed class CapacityChangeGeneratorTests
{
    private static SeedingConfiguration DefaultConfig => new()
    {
        CourseCount               = 100,
        SmallCoursePercentage     = 27,
        MediumCoursePercentage    = 53,
        LargeCoursePercentage     = 20,
        CapacityChangePercentage  = 20
    };

    private static SeedContext BuildContext()
    {
        var context = new SeedContext(randomSeed: 42);
        new CourseGenerator().Generate(context, DefaultConfig);
        return context;
    }

    private readonly CapacityChangeGenerator _sut = new();

    [Fact]
    public void Generate_ProducesExpectedModificationCount()
    {
        var context  = BuildContext();
        var expected = context.Courses.Count * DefaultConfig.CapacityChangePercentage / 100;

        var events = _sut.Generate(context, DefaultConfig);

        Assert.Equal(expected, events.Count);
    }

    [Fact]
    public void Generate_EventPayloadsAreCourseStudentLimitModifiedEvent()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        Assert.All(events, e => Assert.IsType<CourseStudentLimitModifiedEvent>(e.Event.Event));
    }

    [Fact]
    public void Generate_EachEventHasCourseIdTag()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        Assert.All(events, e => Assert.Contains(e.Event.Tags, t => t.Key == "courseId"));
    }

    [Fact]
    public void Generate_NewCapacityIsNeverBelowTen()
    {
        var context = BuildContext();
        var events  = _sut.Generate(context, DefaultConfig);

        Assert.All(events, e =>
        {
            var modified = (CourseStudentLimitModifiedEvent)e.Event.Event;
            Assert.True(modified.NewMaxStudentCount >= 10,
                $"Capacity {modified.NewMaxStudentCount} is below the minimum of 10.");
        });
    }

    [Fact]
    public void Generate_UpdatesContextCoursesCapacity()
    {
        var context      = BuildContext();
        var originalCaps = context.Courses.ToDictionary(c => c.CourseId, c => c.MaxCapacity);

        var events = _sut.Generate(context, DefaultConfig);

        // For each event, the corresponding context entry should reflect the new capacity.
        foreach (var e in events)
        {
            var modified = (CourseStudentLimitModifiedEvent)e.Event.Event;
            var updated  = context.Courses.First(c => c.CourseId == modified.CourseId);
            Assert.Equal(modified.NewMaxStudentCount, updated.MaxCapacity);
        }
    }

    [Fact]
    public void Generate_TimestampsAreWithinExpectedWindow()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);
        var now    = DateTimeOffset.UtcNow;

        // 150–60 days ago (±1-day margin)
        Assert.All(events, e =>
        {
            var daysAgo = (now - e.Metadata.Timestamp).TotalDays;
            Assert.InRange(daysAgo, 59, 151);
        });
    }
}
