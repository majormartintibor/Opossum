using Opossum.Core;
using Opossum.Samples.CourseManagement.Events;
using Opossum.Samples.DataSeeder.Core;

namespace Opossum.Samples.DataSeeder.Generators;

/// <summary>
/// Generates <see cref="CourseStudentLimitModifiedEvent"/> records for a configurable
/// percentage of courses.
/// Enforces a minimum capacity of 10 after modification.
/// Updates <see cref="SeedContext.Courses"/> so the <see cref="EnrollmentGenerator"/>
/// respects the revised limits.
/// </summary>
public sealed class CapacityChangeGenerator : ISeedGenerator
{
    public IReadOnlyList<SeedEvent> Generate(SeedContext context, SeedingConfiguration config)
    {
        var random      = context.Random;
        var modifyCount = context.Courses.Count * config.CapacityChangePercentage / 100;

        var indices = Enumerable.Range(0, context.Courses.Count)
            .OrderBy(_ => random.Next())
            .Take(modifyCount)
            .ToList();

        var events = new List<SeedEvent>(indices.Count);

        foreach (var idx in indices)
        {
            var course      = context.Courses[idx];
            var change      = random.Next(-10, 11);
            var newCapacity = Math.Max(10, course.MaxCapacity + change);

            context.Courses[idx] = new CourseInfo(course.CourseId, course.Name, newCapacity);

            Tag[] tags = [new("courseId", course.CourseId.ToString())];

            events.Add(GeneratorHelper.CreateSeedEvent(
                new CourseStudentLimitModifiedEvent(course.CourseId, newCapacity),
                tags,
                GeneratorHelper.RandomTimestamp(random, 150, 60)));
        }

        return events;
    }
}
