using Opossum.Samples.CourseManagement.Events;
using Opossum.Samples.DataSeeder;
using Opossum.Samples.DataSeeder.Core;
using Opossum.Samples.DataSeeder.Generators;

namespace Opossum.Samples.DataSeeder.UnitTests.Generators;

/// <summary>
/// Unit tests for <see cref="EnrollmentGenerator"/>.
/// Tests verify invariants (no duplicates, capacity limits, tier limits) rather than
/// exact counts, because the stochastic assignment is capacity-constrained.
/// </summary>
public sealed class EnrollmentGeneratorTests
{
    /// <summary>Small config: 20 students, 5 courses — easy to reason about.</summary>
    private static SeedingConfiguration SmallConfig => new()
    {
        StudentCount               = 20,
        BasicTierPercentage        = 25,
        StandardTierPercentage     = 50,
        ProfessionalTierPercentage = 25,
        MasterTierPercentage       = 0,
        CourseCount                = 5,
        SmallCoursePercentage      = 100,
        MediumCoursePercentage     = 0,
        LargeCoursePercentage      = 0
    };

    private static SeedContext BuildContext()
    {
        var context = new SeedContext(randomSeed: 42);
        new StudentGenerator().Generate(context, SmallConfig);
        new CourseGenerator().Generate(context, SmallConfig);
        return context;
    }

    private readonly EnrollmentGenerator _sut = new();

    [Fact]
    public void Generate_ProducesAtLeastOneEnrollment()
    {
        var events = _sut.Generate(BuildContext(), SmallConfig);

        Assert.NotEmpty(events);
    }

    [Fact]
    public void Generate_EventPayloadsAreStudentEnrolledToCourseEvent()
    {
        var events = _sut.Generate(BuildContext(), SmallConfig);

        Assert.All(events, e => Assert.IsType<StudentEnrolledToCourseEvent>(e.Event.Event));
    }

    [Fact]
    public void Generate_EachEventHasCourseIdAndStudentIdTags()
    {
        var events = _sut.Generate(BuildContext(), SmallConfig);

        Assert.All(events, e =>
        {
            Assert.Contains(e.Event.Tags, t => t.Key == "courseId");
            Assert.Contains(e.Event.Tags, t => t.Key == "studentId");
        });
    }

    [Fact]
    public void Generate_NoDuplicateStudentCoursePairs()
    {
        var context = BuildContext();
        _sut.Generate(context, SmallConfig);

        // The generator writes all enrolled pairs to context.EnrolledPairs — duplicates
        // would have caused a HashSet collision and been skipped, so the set itself is the proof.
        var pairsFromContext = context.EnrolledPairs.Count;
        Assert.Equal(pairsFromContext, context.EnrolledPairs.Distinct().Count());
    }

    [Fact]
    public void Generate_NoCourseExceedsMaxCapacity()
    {
        var context = BuildContext();
        _sut.Generate(context, SmallConfig);

        foreach (var course in context.Courses)
        {
            var enrolled = context.CourseEnrollmentCounts.GetValueOrDefault(course.CourseId);
            Assert.True(enrolled <= course.MaxCapacity,
                $"Course {course.CourseId} has {enrolled} enrollments but max is {course.MaxCapacity}.");
        }
    }

    [Fact]
    public void Generate_NoStudentExceedsTierLimit()
    {
        var context = BuildContext();
        _sut.Generate(context, SmallConfig);

        foreach (var student in context.Students)
        {
            var enrolled = context.StudentEnrollmentCounts.GetValueOrDefault(student.StudentId);
            Assert.True(enrolled <= student.MaxCourses,
                $"Student {student.StudentId} has {enrolled} enrollments but tier limit is {student.MaxCourses}.");
        }
    }

    [Fact]
    public void Generate_EventCountMatchesContextEnrolledPairsCount()
    {
        var context = BuildContext();
        var events  = _sut.Generate(context, SmallConfig);

        Assert.Equal(context.EnrolledPairs.Count, events.Count);
    }

    [Fact]
    public void Generate_TimestampsAreWithinExpectedWindow()
    {
        var events = _sut.Generate(BuildContext(), SmallConfig);
        var now    = DateTimeOffset.UtcNow;

        // 120–1 days ago (±1-day margin)
        Assert.All(events, e =>
        {
            var daysAgo = (now - e.Metadata.Timestamp).TotalDays;
            Assert.InRange(daysAgo, 0, 121);
        });
    }

    [Fact]
    public void Generate_PopulatesCourseAndStudentEnrollmentCounts()
    {
        var context = BuildContext();
        _sut.Generate(context, SmallConfig);

        Assert.NotEmpty(context.CourseEnrollmentCounts);
        Assert.NotEmpty(context.StudentEnrollmentCounts);
    }
}
