using Opossum.Samples.CourseManagement.Events;
using Opossum.Samples.DataSeeder;
using Opossum.Samples.DataSeeder.Core;
using Opossum.Samples.DataSeeder.Generators;
using Tier = Opossum.Samples.CourseManagement.EnrollmentTier.EnrollmentTier;

namespace Opossum.Samples.DataSeeder.UnitTests.Generators;

/// <summary>
/// Unit tests for <see cref="TierUpgradeGenerator"/>.
/// Tests are run against a pre-populated <see cref="SeedContext"/> built by
/// <see cref="StudentGenerator"/> so that real student state is present.
/// </summary>
public sealed class TierUpgradeGeneratorTests
{
    private static SeedingConfiguration DefaultConfig => new()
    {
        StudentCount               = 100,
        BasicTierPercentage        = 20,
        StandardTierPercentage     = 40,
        ProfessionalTierPercentage = 30,
        MasterTierPercentage       = 10,
        TierUpgradePercentage      = 30
    };

    private static SeedContext BuildContext()
    {
        var context = new SeedContext(randomSeed: 42);
        new StudentGenerator().Generate(context, DefaultConfig);
        return context;
    }

    private readonly TierUpgradeGenerator _sut = new();

    [Fact]
    public void Generate_ProducesExpectedUpgradeCount()
    {
        var context       = BuildContext();
        var upgradeCount  = context.Students.Count * DefaultConfig.TierUpgradePercentage / 100;
        var nonMasterCount = context.Students.Count(s => s.Tier != Tier.Master);
        var expected      = Math.Min(upgradeCount, nonMasterCount);

        var events = _sut.Generate(context, DefaultConfig);

        Assert.Equal(expected, events.Count);
    }

    [Fact]
    public void Generate_EventPayloadsAreStudentSubscriptionUpdatedEvent()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        Assert.All(events, e => Assert.IsType<StudentSubscriptionUpdatedEvent>(e.Event.Event));
    }

    [Fact]
    public void Generate_EachEventHasStudentIdTag()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);

        Assert.All(events, e => Assert.Contains(e.Event.Tags, t => t.Key == "studentId"));
    }

    [Fact]
    public void Generate_NeverUpgradesMasterStudents()
    {
        var context = BuildContext();
        var masterIdsBefore = context.Students
            .Where(s => s.Tier == Tier.Master)
            .Select(s => s.StudentId)
            .ToHashSet();

        _sut.Generate(context, DefaultConfig);

        // Students that were Master before should still be Master (not promoted beyond it).
        Assert.All(
            context.Students.Where(s => masterIdsBefore.Contains(s.StudentId)),
            s => Assert.Equal(Tier.Master, s.Tier));
    }

    [Fact]
    public void Generate_MasterCountDoesNotDecrease()
    {
        var context          = BuildContext();
        var masterBefore     = context.Students.Count(s => s.Tier == Tier.Master);

        _sut.Generate(context, DefaultConfig);

        var masterAfter = context.Students.Count(s => s.Tier == Tier.Master);
        Assert.True(masterAfter >= masterBefore);
    }

    [Fact]
    public void Generate_UpgradedStudentTierInContextMatchesEvent()
    {
        var context = BuildContext();
        var events  = _sut.Generate(context, DefaultConfig);

        foreach (var e in events)
        {
            var upgraded    = (StudentSubscriptionUpdatedEvent)e.Event.Event;
            var studentInfo = context.Students.First(s => s.StudentId == upgraded.StudentId);
            Assert.Equal(upgraded.EnrollmentTier, studentInfo.Tier);
        }
    }

    [Fact]
    public void Generate_TimestampsAreWithinExpectedWindow()
    {
        var events = _sut.Generate(BuildContext(), DefaultConfig);
        var now    = DateTimeOffset.UtcNow;

        // 180–30 days ago (±1-day margin for test execution time)
        Assert.All(events, e =>
        {
            var daysAgo = (now - e.Metadata.Timestamp).TotalDays;
            Assert.InRange(daysAgo, 29, 181);
        });
    }
}
