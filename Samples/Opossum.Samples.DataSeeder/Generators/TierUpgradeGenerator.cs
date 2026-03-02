using Opossum.Core;
using Opossum.Samples.CourseManagement.EnrollmentTier;
using Opossum.Samples.CourseManagement.Events;
using Opossum.Samples.DataSeeder.Core;
using Tier = Opossum.Samples.CourseManagement.EnrollmentTier.EnrollmentTier;

namespace Opossum.Samples.DataSeeder.Generators;

/// <summary>
/// Generates <see cref="StudentSubscriptionUpdatedEvent"/> records for a configurable
/// percentage of non-Master students.
/// Updates <see cref="SeedContext.Students"/> with the upgraded tier and revised course limit
/// so downstream generators (e.g. <see cref="EnrollmentGenerator"/>) see the new values.
/// </summary>
public sealed class TierUpgradeGenerator : ISeedGenerator
{
    public IReadOnlyList<SeedEvent> Generate(SeedContext context, SeedingConfiguration config)
    {
        var random = context.Random;
        var upgradeCount = context.Students.Count * config.TierUpgradePercentage / 100;

        var eligibleIndices = Enumerable.Range(0, context.Students.Count)
            .Where(i => context.Students[i].Tier != Tier.Master)
            .OrderBy(_ => random.Next())
            .Take(upgradeCount)
            .ToList();

        var events = new List<SeedEvent>(eligibleIndices.Count);

        foreach (var idx in eligibleIndices)
        {
            var student    = context.Students[idx];
            var newTier    = GetNextTier(student.Tier);
            var maxCourses = StudentMaxCourseEnrollment.GetMaxCoursesAllowed(newTier);

            context.Students[idx] = new StudentInfo(student.StudentId, newTier, maxCourses);

            Tag[] tags = [new("studentId", student.StudentId.ToString())];

            events.Add(GeneratorHelper.CreateSeedEvent(
                new StudentSubscriptionUpdatedEvent(student.StudentId, newTier),
                tags,
                GeneratorHelper.RandomTimestamp(random, 180, 30)));
        }

        return events;
    }

    private static Tier GetNextTier(Tier current) => current switch
    {
        Tier.Basic        => Tier.Standard,
        Tier.Standard     => Tier.Professional,
        Tier.Professional => Tier.Master,
        _                 => current
    };
}
